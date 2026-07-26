using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>
/// 调度计算与状态持久化(state.json)。时间依据只有一条:游戏内倒计时的 OCR 读数。
/// NeedsManual 的设施不参与触发计算(避免反复空跑),但每轮执行仍会重新观察它,
/// 用户补齐材料后下一轮自动恢复。失败退避闸门由成功一轮清除。
/// </summary>
public sealed class ScheduleEngine
{
    private readonly JsonStoreBrick _store;
    private readonly string _statePath;
    private readonly IClock _clock;
    private readonly ILogger _log;

    public ScheduleState State { get; }

    public ScheduleEngine(JsonStoreBrick store, string statePath, IClock clock, ILogger log)
    {
        _store = store;
        _statePath = statePath;
        _clock = clock;
        _log = log.ForContext<ScheduleEngine>();
        State = store.LoadOrCreate(statePath, ScheduleState.CreateDefault);
    }

    public void RecordObservation(FacilityKey key, FacilityPhase phase, string itemName,
        DateTimeOffset? readyAt, string? manualReason)
    {
        var rt = State.For(key);
        rt.Phase = phase;
        rt.ItemName = itemName;
        rt.ReadyAt = readyAt;
        rt.ManualReason = manualReason;
        rt.ObservedAt = _clock.Now;
        Save();
    }

    /// <summary>
    /// 下次执行时刻。取启用设施中最早的需求点:
    /// 制造中 → 完成时刻+缓冲;可领取/空闲/从未观察 → 现在;NeedsManual → 不触发。
    /// 失败退避期内不早于退避截止。没有任何启用设施需要动作时返回 null。
    /// </summary>
    public DateTimeOffset? ComputeNextRunAt(CraftPlanConfig plan, AppSettings settings)
    {
        var now = _clock.Now;
        DateTimeOffset? next = null;
        foreach (var fp in plan.Facilities.Where(f => f.Enabled))
        {
            var rt = State.For(fp.Key);
            DateTimeOffset? candidate = rt.Phase switch
            {
                FacilityPhase.Crafting when rt.ReadyAt is { } ready =>
                    ready + TimeSpan.FromSeconds(settings.RunBufferSeconds),
                FacilityPhase.Crafting => now, // 制造中但没有读数,重新观察
                FacilityPhase.ReadyToCollect or FacilityPhase.Idle or FacilityPhase.Unknown => now,
                FacilityPhase.NeedsManual => null,
                _ => null,
            };
            if (candidate is { } c && (next is null || c < next)) next = c;
        }

        if (next is { } n && State.FailureBackoffUntil is { } backoff && backoff > n)
            next = backoff;
        return next;
    }

    public void MarkRunStarted()
    {
        State.LastRunAt = _clock.Now;
        Save();
    }

    public void MarkRunFinished(string summary, bool failed, int failureRetryMinutes)
    {
        State.LastRunSummary = summary;
        State.LastRunFailed = failed;
        State.FailureBackoffUntil = failed ? _clock.Now.AddMinutes(failureRetryMinutes) : null;
        if (failed)
            _log.Warning("本轮失败,{Minutes} 分钟后才会再次自动尝试。", failureRetryMinutes);
        Save();
    }

    public void Save() => _store.Save(_statePath, State);
}
