using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using DeltaCrafter.Core.L2;
using Serilog;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class ScheduleEngineTests : IDisposable
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(8));
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deltacrafter-tests-" + Guid.NewGuid());
    private readonly FakeClock _clock = new();
    private readonly ScheduleEngine _engine;
    private readonly AppSettings _settings = new() { RunBufferSeconds = 60, FailureRetryMinutes = 30 };
    private readonly CraftPlanConfig _plan = CraftPlanConfig.CreateDefault();

    public ScheduleEngineTests()
    {
        Directory.CreateDirectory(_dir);
        // 无 sink 的配置即静默 logger,不依赖具体 Serilog 版本是否提供 Logger.None。
        ILogger silent = new LoggerConfiguration().CreateLogger();
        _engine = new ScheduleEngine(new JsonStoreBrick(),
            Path.Combine(_dir, "state.json"), _clock, silent);
    }

    [Fact]
    public void Crafting_facility_schedules_ready_plus_buffer()
    {
        _plan.For(FacilityKey.Workbench).Enabled = true;
        var ready = _clock.Now.AddHours(2);
        _engine.RecordObservation(FacilityKey.Workbench, FacilityPhase.Crafting, "物品A", ready, null);

        Assert.Equal(ready.AddSeconds(60), _engine.ComputeNextRunAt(_plan, _settings));
    }

    [Fact]
    public void Ready_or_unknown_facility_triggers_now()
    {
        _plan.For(FacilityKey.Workbench).Enabled = true; // 从未观察 → Unknown
        Assert.Equal(_clock.Now, _engine.ComputeNextRunAt(_plan, _settings));

        _engine.RecordObservation(FacilityKey.Workbench, FacilityPhase.ReadyToCollect, "物品A", null, null);
        Assert.Equal(_clock.Now, _engine.ComputeNextRunAt(_plan, _settings));
    }

    [Fact]
    public void Earliest_enabled_facility_wins()
    {
        _plan.For(FacilityKey.Workbench).Enabled = true;
        _plan.For(FacilityKey.TechCenter).Enabled = true;
        _engine.RecordObservation(FacilityKey.Workbench, FacilityPhase.Crafting, "A", _clock.Now.AddHours(5), null);
        _engine.RecordObservation(FacilityKey.TechCenter, FacilityPhase.Crafting, "B", _clock.Now.AddHours(1), null);

        Assert.Equal(_clock.Now.AddHours(1).AddSeconds(60), _engine.ComputeNextRunAt(_plan, _settings));
    }

    [Fact]
    public void Disabled_and_needs_manual_do_not_trigger()
    {
        // 全部未启用 → 无计划
        Assert.Null(_engine.ComputeNextRunAt(_plan, _settings));

        // 仅一个启用且 NeedsManual → 不因它反复空跑
        _plan.For(FacilityKey.PharmacyLab).Enabled = true;
        _engine.RecordObservation(FacilityKey.PharmacyLab, FacilityPhase.NeedsManual, "C", null, "材料不足");
        Assert.Null(_engine.ComputeNextRunAt(_plan, _settings));
    }

    [Fact]
    public void Failure_backoff_delays_next_run_until_gate()
    {
        _plan.For(FacilityKey.Workbench).Enabled = true;
        _engine.RecordObservation(FacilityKey.Workbench, FacilityPhase.ReadyToCollect, "A", null, null);
        _engine.MarkRunFinished("失败", failed: true, failureRetryMinutes: 30);

        Assert.Equal(_clock.Now.AddMinutes(30), _engine.ComputeNextRunAt(_plan, _settings));

        _engine.MarkRunFinished("成功", failed: false, failureRetryMinutes: 30);
        Assert.Equal(_clock.Now, _engine.ComputeNextRunAt(_plan, _settings));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
    }
}
