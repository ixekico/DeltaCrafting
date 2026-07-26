using DeltaCrafter.Core.L0;

namespace DeltaCrafter.Core.L2;

/// <summary>进入特勤处(点大厅顶栏「特勤处」页签)。四个设施槽位在特勤处总览一屏可见,
/// 无需再逐设施导航——观察与操作都从总览页出发。</summary>
public sealed class SpecOpsNavFlow
{
    private readonly ScreenProbe _probe;
    private readonly StepRunner _runner;

    public SpecOpsNavFlow(ScreenProbe probe, StepRunner runner)
    {
        _probe = probe;
        _runner = runner;
    }

    public async Task EnterSpecOpsAsync(nint hwnd, CancellationToken ct)
    {
        if (await _probe.IsOnAsync(hwnd, AnchorKeys.SpecOpsHome)) return; // 游戏恢复在特勤处页

        await _runner.RunAsync(hwnd, new Step(
            "进入特勤处",
            () => _probe.ClickPoint(hwnd, AnchorKeys.Lobby, AnchorKeys.PointSpecOpsEntry),
            () => _probe.IsOnAsync(hwnd, AnchorKeys.SpecOpsHome),
            TimeSpan.FromSeconds(15)), ct);
    }
}
