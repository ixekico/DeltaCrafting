using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>
/// 一轮成功后的游戏处置(仅成功路径调用;失败保留现场):
/// 关闭进程 / 最小化后台 / 返回大厅留在前台(挂机)。
/// 返回大厅走顶栏「开始游戏」页签点击 + 大厅探针校验,不用 ESC(ESC 在大厅层级行为不确定)。
/// </summary>
public sealed class ShutdownFlow
{
    private readonly GameProcessBrick _process;
    private readonly GameWindowBrick _window;
    private readonly ScreenProbe _probe;
    private readonly StepRunner _runner;
    private readonly ILogger _log;

    public ShutdownFlow(GameProcessBrick process, GameWindowBrick window,
        ScreenProbe probe, StepRunner runner, ILogger log)
    {
        _process = process;
        _window = window;
        _probe = probe;
        _runner = runner;
        _log = log.ForContext<ShutdownFlow>();
    }

    public async Task ApplyAsync(AfterRunAction action, nint hwnd, CancellationToken ct)
    {
        switch (action)
        {
            case AfterRunAction.CloseGame:
                _process.CloseByWindow(hwnd, TimeSpan.FromSeconds(20));
                break;

            case AfterRunAction.KeepRunning:
                _window.Minimize(hwnd);
                _log.Information("按设置保留游戏运行,窗口已最小化。");
                break;

            case AfterRunAction.KeepAtLobby:
                if (!await _probe.IsOnAsync(hwnd, AnchorKeys.Lobby))
                {
                    await _runner.RunAsync(hwnd, new Step(
                        "返回大厅",
                        () => _probe.ClickPoint(hwnd, AnchorKeys.SpecOpsHome, AnchorKeys.PointBackToLobby),
                        () => _probe.IsOnAsync(hwnd, AnchorKeys.Lobby),
                        TimeSpan.FromSeconds(10)), ct);
                }
                _log.Information("按设置游戏留在大厅,窗口保持前台挂机。");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "未知的收取后行为");
        }
    }
}
