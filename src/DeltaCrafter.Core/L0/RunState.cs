namespace DeltaCrafter.Core.L0;

/// <summary>设施的运行期状态。Unknown 表示尚未观察过,视为"需要尽快巡视一次"。</summary>
public enum FacilityPhase { Unknown, Idle, Crafting, ReadyToCollect, NeedsManual }

/// <summary>协调器对外的宏观状态,驱动 UI 状态胶囊与总览页。</summary>
public enum EngineMode { Idle, WaitingSchedule, Running, Faulted }

/// <summary>协调器状态快照。Detail 为面向人的短语(如"处理 工作台")。</summary>
public sealed record CoordinatorStatus(EngineMode Mode, string Detail, DateTimeOffset? NextRunAt);

/// <summary>
/// 单设施运行期记录。ReadyAt 来自游戏内倒计时 OCR 读数,是调度的唯一时间依据
/// (不用静态时长表:制造时长受特勤处等级影响,静态表必然漂移)。
/// </summary>
public sealed class FacilityRuntime
{
    public FacilityKey Key { get; set; }
    public FacilityPhase Phase { get; set; } = FacilityPhase.Unknown;
    public string ItemName { get; set; } = "";
    public DateTimeOffset? ReadyAt { get; set; }
    /// <summary>Phase 为 NeedsManual 时的原因(如"材料不足"),用于 UI 与通知。</summary>
    public string? ManualReason { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
}

/// <summary>
/// 调度持久状态(state.json)。应用重启后据此恢复计划;若已过期则尽快补跑。
/// FailureBackoffUntil:上轮失败后的重试闸门,成功一轮即清零。
/// </summary>
public sealed class ScheduleState
{
    public List<FacilityRuntime> Facilities { get; set; } = [];
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastRunSummary { get; set; }
    public bool LastRunFailed { get; set; }
    public DateTimeOffset? FailureBackoffUntil { get; set; }

    public FacilityRuntime For(FacilityKey key)
    {
        var found = Facilities.FirstOrDefault(f => f.Key == key);
        if (found is null)
        {
            found = new FacilityRuntime { Key = key };
            Facilities.Add(found);
        }
        return found;
    }

    public static ScheduleState CreateDefault() => new()
    {
        Facilities = FacilityKeys.All.Select(k => new FacilityRuntime { Key = k }).ToList(),
    };
}

/// <summary>一轮执行的过程记录,汇总为通知与"上次执行"摘要。</summary>
public sealed class RunReport
{
    private readonly List<string> _lines = [];
    public string Trigger { get; }
    public bool HasFailure { get; private set; }

    public RunReport(string trigger) => Trigger = trigger;

    public void Add(string line) => _lines.Add(line);

    public void AddFailure(string line)
    {
        HasFailure = true;
        _lines.Add(line);
    }

    public string Summary() => _lines.Count == 0 ? "无操作" : string.Join(";", _lines);
}
