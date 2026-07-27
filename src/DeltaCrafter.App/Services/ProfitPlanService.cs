using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L3;
using Microsoft.UI.Dispatching;
using Serilog;

namespace DeltaCrafter.App.Services;

/// <summary>
/// 利润模式编排:CraftMode 为利润优先时,按 ProfitPlanCoordinator.RefreshInterval
/// 周期抓取 kkrb.net 推荐并自动填充制造计划;Custom 模式下完全静默。
/// 线程约束:抓取在后台线程,计划/设置写入与 UI 刷新一律回派发队列
/// (与「设置仅 UI 线程写入」约束一致)。抓取失败记 Warning 并在计划页横幅
/// 亮明,10 分钟后自动重试——失败不清空、不回退已填充的计划。
/// </summary>
public sealed class ProfitPlanService
{
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(10);

    private readonly AppHost _host;
    private readonly ProfitPlanCoordinator _coordinator;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private DateTimeOffset _lastSuccessAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAttemptAt = DateTimeOffset.MinValue;

    /// <summary>计划页横幅的最近一次刷新结论(UI 线程读写)。</summary>
    public string LastStatus { get; private set; } = "";

    public ProfitPlanService(AppHost host, ProfitPlanCoordinator coordinator, ILogger log)
    {
        _host = host;
        _coordinator = coordinator;
        _log = log;
        // 组合根在 UI 线程构造本服务,借此拿到派发队列(后台循环回 UI 用)。
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>后台巡检:到期(2 小时)或失败重试窗口(10 分钟)满足时刷新。</summary>
    public async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                if (_host.Settings.CraftMode != CraftMode.Custom && IsRefreshDue())
                    await TryRefreshAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        // 只有应用退出的取消才允许安静收场;其他任何异常终止都大声记下——
        // 循环无声停摆会让「每 2 小时更新」变成假承诺。
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* 应用退出 */ }
        catch (Exception ex)
        {
            _log.Error(ex, "利润推荐巡检循环异常终止,自动填充已停止;重启应用可恢复。");
        }
    }

    /// <summary>设置页切换制造模式后调用(UI 线程)。切到利润模式立即抓取一次,
    /// 不等下一个巡检周期;切回自定义仅记录,已填充的物品保留给用户继续编辑。</summary>
    public void OnModeChanged()
    {
        var mode = _host.Settings.CraftMode;
        if (mode == CraftMode.Custom)
        {
            _log.Information("制造模式已切换为自定义,停止自动填充;当前计划物品保留。");
            SetStatus("");
            return;
        }
        _lastSuccessAt = DateTimeOffset.MinValue;
        _lastAttemptAt = DateTimeOffset.MinValue;
        SetStatus("正在获取利润推荐…");
        _ = Task.Run(() => TryRefreshAsync(CancellationToken.None));
    }

    private bool IsRefreshDue()
    {
        var now = DateTimeOffset.Now;
        return now - _lastSuccessAt >= ProfitPlanCoordinator.RefreshInterval
            && now - _lastAttemptAt >= FailureRetryDelay;
    }

    /// <summary>抓取并应用。并发保护:抓取进行中时后来者直接放弃(巡检下分钟再看)。</summary>
    private async Task TryRefreshAsync(CancellationToken ct)
    {
        if (!await _refreshGate.WaitAsync(0, ct)) return;
        try
        {
            _lastAttemptAt = DateTimeOffset.Now;
            var recommendations = await _coordinator.FetchRecommendationsAsync(ct);
            _lastSuccessAt = DateTimeOffset.Now;
            _dispatcher.TryEnqueue(() => ApplyRecommendations(recommendations));
        }
        // 砖内超时已翻译为 TimeoutException;此过滤只放行「调用方令牌真被取消」的
        // 取消异常(应用退出),其余取消异常一律按普通失败告警,防循环被静默杀死。
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.Warning("利润推荐获取失败:{Reason}(10 分钟后自动重试)。", ex.Message);
            string reason = ex.Message;
            _dispatcher.TryEnqueue(() =>
                SetStatus($"上次获取失败({DateTimeOffset.Now:HH:mm}):{reason} 将自动重试,当前计划保持不变。"));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>把推荐写入计划(UI 线程)。抓取期间用户可能已切回自定义——
    /// 应用前再验一次模式,绝不覆盖用户手选的物品。只定点刷新物品变化的卡片,
    /// 其余卡片保留输入状态(正在编辑的备注不被后台刷新打断)。</summary>
    private void ApplyRecommendations(IReadOnlyList<ProfitRecommendation> recommendations)
    {
        var mode = _host.Settings.CraftMode;
        if (mode == CraftMode.Custom) return;

        bool planDirty = false;
        var replacedItems = new List<FacilityKey>();
        foreach (var rec in recommendations)
        {
            var plan = _host.Plan.For(rec.Facility);
            string resolved = _host.ResolveCatalogMatchKey(rec.Facility, rec.ItemName) ?? "";
            if (plan.ItemName == rec.ItemName)
            {
                // 物品未变也补算匹配键:目录后来收录该物品时把 OCR 匹配键补写进计划
                // (利润模式下物品锁定,不能指望人工修);解析不出保留原值不清空。
                if (resolved.Length > 0 && plan.MatchName != resolved)
                {
                    plan.MatchName = resolved;
                    planDirty = true;
                }
                continue;
            }
            _log.Information("{Facility} 计划物品:{Old} → {New}(小时利润 {Hourly:N0} / 总利润 {Total:N0})。",
                FacilityKeys.DisplayName(rec.Facility),
                plan.ItemName.Length > 0 ? plan.ItemName : "未选物品",
                rec.ItemName, rec.HourlyProfit, rec.TotalProfit);
            plan.ItemName = rec.ItemName;
            plan.MatchName = resolved;
            planDirty = true;
            replacedItems.Add(rec.Facility);
        }
        if (planDirty) _host.SavePlan();
        if (replacedItems.Count > 0) _host.PlanVm.RefreshFacilities(replacedItems);
        string metric = mode == CraftMode.HourlyProfit ? "每小时利润" : "总利润";
        SetStatus($"推荐已更新({_lastSuccessAt:HH:mm},{metric}口径):" + string.Join(";",
            recommendations.Select(r => $"{FacilityKeys.DisplayName(r.Facility)}={r.ItemName}")));
    }

    /// <summary>更新横幅结论并通知计划页(调用方保证在 UI 线程)。</summary>
    private void SetStatus(string status)
    {
        LastStatus = status;
        _host.PlanVm.NotifyProfitStatusChanged();
    }
}
