using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L3;
using Microsoft.UI.Dispatching;
using Serilog;

namespace DeltaCrafter.App.Services;

/// <summary>
/// kkrb.net 行情缓存与利润计划编排。应用启动立即预热,之后每个本地整点刷新,
/// 与设施模式、启用状态无关;模式切换优先消费最近成功缓存,仅在缓存为空时立即联网。
/// 抓取在后台线程;计划写入与 UI 刷新回派发队列,并等待当前制造轮结束。
/// 抓取失败保留旧缓存、亮明原因并按协调器策略重试。
/// </summary>
public sealed class ProfitPlanService
{
    private readonly AppHost _host;
    private readonly ProfitPlanCoordinator _coordinator;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly ProfitPlanApplier _applier;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private int _missingCacheRefreshQueued;

    private sealed record RecommendationCache(
        ProfitRecommendationSet Recommendations,
        DateTimeOffset FetchedAt);

    // 缓存与时间必须作为一个不可变快照原子替换,否则模式切换可能读到新数据配旧时间。
    private RecommendationCache? _latestCache;
    private string? _lastRefreshError;

    /// <summary>计划页横幅的最近一次刷新结论(UI 线程读写)。</summary>
    public string LastStatus { get; private set; } = "";

    public ProfitPlanService(AppHost host, ProfitPlanCoordinator coordinator, ILogger log)
    {
        _host = host;
        _coordinator = coordinator;
        _log = log;
        _applier = new ProfitPlanApplier(host, log);
        // 组合根在 UI 线程构造本服务,借此拿到派发队列(后台循环回 UI 用)。
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>启动立即预热;成功后等下一个整点,失败则 10 分钟重试且不跨过整点。</summary>
    public async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            var nextAttemptAt = DateTimeOffset.Now;
            while (true)
            {
                var delay = nextAttemptAt - DateTimeOffset.Now;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                bool succeeded = await RefreshCacheAsync(onlyIfMissing: false, ct);
                nextAttemptAt = ProfitPlanCoordinator.NextRefreshAttemptAt(
                    DateTimeOffset.Now, succeeded);
                _log.Debug("下次利润行情刷新计划:{Next:yyyy-MM-dd HH:mm:ss}。",
                    nextAttemptAt);
            }
        }
        // 只有应用退出的取消才允许安静收场;其他任何异常终止都大声记下——
        // 循环无声停摆会让「每个整点更新」变成假承诺。
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* 应用退出 */ }
        catch (Exception ex)
        {
            _log.Error(ex, "利润推荐巡检循环异常终止,自动填充已停止;重启应用可恢复。");
        }
    }

    /// <summary>设施卡切换制造模式后调用(UI 线程)。利润模式优先应用后台缓存,
    /// 缓存为空则立即请求;切回自定义仅解除接管并保留当前物品。</summary>
    public void OnFacilityModeChanged(FacilityKey key, CraftMode mode)
    {
        if (mode == CraftMode.Custom)
        {
            _log.Information("{Facility} 已切换为自定义物品,停止自动填充;当前物品保留。",
                FacilityKeys.DisplayName(key));
            if (!HasProfitFacilities()) SetStatus("");
            return;
        }
        _log.Information("{Facility} 已切换为 {Mode},准备应用最近行情缓存。",
            FacilityKeys.DisplayName(key), DescribeMetric(mode));

        var cache = Volatile.Read(ref _latestCache);
        if (cache is null)
        {
            SetStatus("行情缓存为空,正在立即获取;成功后会自动应用当前利润模式。");
            RequestMissingCacheRefresh();
            return;
        }

        SetStatus($"正在应用 {cache.FetchedAt:HH:mm} 行情缓存…");
        _ = Task.Run(() => ApplyLatestCacheBetweenRoundsAsync(
            [key], CancellationToken.None));
    }

    private void RequestMissingCacheRefresh()
    {
        // 用户可能连续调整多张卡片。请求只合并、不丢失:在途任务结束后标记归零,
        // 若仍无缓存,下一次模式切换仍可再次立即请求。
        if (Interlocked.CompareExchange(ref _missingCacheRefreshQueued, 1, 0) != 0)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshCacheAsync(onlyIfMissing: true, CancellationToken.None);
            }
            finally
            {
                Volatile.Write(ref _missingCacheRefreshQueued, 0);
            }
        });
    }

    private bool HasProfitFacilities() =>
        _host.Plan.Facilities.Any(f => f.Mode != CraftMode.Custom);

    /// <summary>
    /// 所有刷新共用一条执行通道。整点刷新总是联网;模式切换只在缓存仍为空时联网。
    /// 若启动预热正在进行,模式切换会等待其结论:成功则复用,失败则紧接着再尝试一次。
    /// </summary>
    private async Task<bool> RefreshCacheAsync(bool onlyIfMissing, CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            if (onlyIfMissing && Volatile.Read(ref _latestCache) is not null)
                return true;
            return await TryRefreshCacheCoreAsync(ct);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>执行一次网络抓取;返回值只表示行情是否成功取得。</summary>
    private async Task<bool> TryRefreshCacheCoreAsync(CancellationToken ct)
    {
        try
        {
            var recommendations = await _coordinator.FetchRecommendationsAsync(ct);
            var completedAt = DateTimeOffset.Now;
            var cache = new RecommendationCache(recommendations, completedAt);
            Volatile.Write(ref _latestCache, cache);
            Volatile.Write(ref _lastRefreshError, null);
            _log.Information("利润行情缓存已更新({Time:HH:mm}),下次正常刷新在下一个整点。",
                completedAt);

            // 缓存发布不等待制造轮;只有计划写入需要执行闸门。即使当前全部为自定义,
            // 行情也已经可供稍后的模式切换立即使用。
            if (HasProfitFacilities())
                await ApplyLatestCacheBetweenRoundsAsync(targetKeys: null, ct);
            return true;
        }
        // 砖内超时已翻译为 TimeoutException;此过滤只放行「调用方令牌真被取消」的
        // 取消异常(应用退出),其余取消异常一律按普通失败告警,防循环被静默杀死。
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            string cacheState = Volatile.Read(ref _latestCache) is null
                ? "当前没有可用缓存。"
                : "继续使用最近一次成功缓存。";
            string error = $"{DateTimeOffset.Now:HH:mm} 获取失败:{ex.Message}";
            Volatile.Write(ref _lastRefreshError, error);
            _log.Warning("利润行情获取失败:{Reason}(10 分钟后或下一整点重试)。",
                ex.Message);
            _dispatcher.TryEnqueue(() =>
                SetStatus($"{error} 将自动重试,{cacheState}"));
            return false;
        }
    }

    /// <summary>等待当前制造轮结束后,在 UI 线程应用指定缓存;应用失败不伪装成抓取失败。</summary>
    private async Task ApplyLatestCacheBetweenRoundsAsync(
        IReadOnlyCollection<FacilityKey>? targetKeys,
        CancellationToken ct)
    {
        try
        {
            await _host.Coordinator.RunBetweenRoundsAsync(
                () => RunOnUiAsync(() =>
                {
                    // 模式切换与整点刷新可能同时排队。真正取得执行闸门后必须重读最新
                    // 快照,防止较早排队的任务在新行情之后反向覆盖计划。
                    var latest = Volatile.Read(ref _latestCache)
                        ?? throw new InvalidOperationException("利润行情缓存意外丢失。");
                    string status = _applier.Apply(
                        latest.Recommendations,
                        latest.FetchedAt,
                        targetKeys,
                        Volatile.Read(ref _lastRefreshError));
                    SetStatus(status);
                }), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "行情缓存已取得,但写入制造计划失败。");
            _dispatcher.TryEnqueue(() =>
                SetStatus($"行情缓存已取得,但写入制造计划失败:{ex.Message}"));
        }
    }

    private static string DescribeMetric(CraftMode mode) => mode switch
    {
        CraftMode.HourlyProfit => "每小时利润最高",
        CraftMode.TotalProfit => "总利润最高",
        CraftMode.Custom => throw new InvalidOperationException("自定义制造模式没有利润口径。"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知制造模式。"),
    };

    private Task RunOnUiAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
            throw new InvalidOperationException("应用界面派发队列不可用,利润推荐未写入计划。");
        return completion.Task;
    }

    /// <summary>更新横幅结论并通知计划页(调用方保证在 UI 线程)。</summary>
    private void SetStatus(string status)
    {
        LastStatus = status;
        _host.PlanVm.NotifyProfitStatusChanged();
    }
}
