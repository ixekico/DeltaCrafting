using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

public sealed record LaunchOutcome(nint Hwnd, bool LaunchedByUs);

/// <summary>
/// 「确保游戏就绪并到达大厅」流程。游戏必须经启动器启动,完整链:
/// 启动器(OCR 找「开始游戏」点击)→ 游戏客户端(16:9 大窗,与启动器同名以比例区分)→
/// 模式选择(点烽火地带)→ 3D 基地(Tab)→ 大厅。未知画面时有界 ESC(≤3 次)关弹窗。
/// </summary>
public sealed class LaunchFlow
{
    private static readonly string[] KnownScreens =
        [AnchorKeys.Lobby, AnchorKeys.SpecOpsHome, AnchorKeys.ModeSelect, AnchorKeys.Safehouse];
    private static readonly NRect FullFrame = new() { X = 0, Y = 0, W = 1, H = 1 };

    private readonly GameProcessBrick _process;
    private readonly GameWindowBrick _window;
    private readonly ScreenProbe _probe;
    private readonly InputBrick _input;
    private readonly Func<AppSettings> _settings;
    private readonly ILogger _log;

    public LaunchFlow(GameProcessBrick process, GameWindowBrick window, ScreenProbe probe,
        InputBrick input, Func<AppSettings> settings, ILogger log)
    {
        _process = process;
        _window = window;
        _probe = probe;
        _input = input;
        _settings = settings;
        _log = log.ForContext<LaunchFlow>();
    }

    public async Task<LaunchOutcome> EnsureLobbyAsync(CancellationToken ct)
    {
        var s = _settings();
        var game = _window.FindGameClient(s.WindowMatch);
        bool launched = false;

        // 「保持运行(最小化)」收尾后客户端处于最小化:客户区几何取不到,16:9 判定
        // 必然失败,若直接走启动器会对着已在运行的游戏白点 240 秒。先还原再找一次。
        if (game is null && _window.TryRestoreMinimizedCandidate(s.WindowMatch))
        {
            await Task.Delay(1200, ct);
            game = _window.FindGameClient(s.WindowMatch);
            if (game is not null) _log.Information("游戏窗口处于最小化,已还原。");
        }

        if (game is null)
        {
            game = await StartViaLauncherAsync(s, ct);
            launched = true;
        }

        if (!_window.TryEnsureForeground(game.Hwnd, TimeSpan.FromSeconds(3)))
            throw new StepFailedException("窗口前台化",
                "无法将游戏窗口置于前台。请检查是否有其他置顶/管理员窗口阻挡。");

        await NavigateToLobbyAsync(game.Hwnd, s, ct);
        return new LaunchOutcome(game.Hwnd, launched);
    }

    /// <summary>经启动器把游戏客户端拉起来。「开始游戏」允许一次显式重试点击(30s 后仍无客户端)。</summary>
    private async Task<GameWindowInfo> StartViaLauncherAsync(AppSettings s, CancellationToken ct)
    {
        var launcher = _window.FindLauncher(s.WindowMatch.TitleContains);
        if (launcher is null)
        {
            _process.Launch(s.GamePath);
            long waitLauncher = Environment.TickCount64 + s.LaunchTimeoutSeconds * 1000L;
            while (launcher is null && Environment.TickCount64 < waitLauncher)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(2000, ct);
                launcher = _window.FindLauncher(s.WindowMatch.TitleContains);
            }
            if (launcher is null)
                throw new StepFailedException("等待启动器窗口",
                    $"启动后 {s.LaunchTimeoutSeconds}s 内未发现标题含「{s.WindowMatch.TitleContains}」的启动器窗口。" +
                    "请确认游戏路径指向启动器/游戏可执行文件。");
            _log.Information("启动器已打开:{Title}", launcher.Title);
        }
        else
        {
            _log.Information("发现已打开的启动器:{Title}", launcher.Title);
        }

        var startWords = _probe.Anchors.Keywords.LauncherStart;
        long deadline = Environment.TickCount64 + s.LaunchTimeoutSeconds * 1000L;
        int clicks = 0;
        long lastClickAt = 0;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var game = _window.FindGameClient(s.WindowMatch);
            if (game is not null && game.Hwnd != launcher.Hwnd)
            {
                _log.Information("游戏客户端窗口已出现:{Title}", game.Title);
                await Task.Delay(2000, ct); // 等渲染器就绪,后续交给画面判定
                return game;
            }

            bool launcherAlive = _window.IsAlive(launcher.Hwnd);
            if (!launcherAlive && clicks == 0)
                throw new StepFailedException("启动游戏客户端", "启动器窗口已消失且游戏客户端未出现。");

            // 点击「开始游戏」:最多 2 次(首点 + 30s 后一次显式重试),不盲目连点。
            if (launcherAlive && clicks < 2 && Environment.TickCount64 - lastClickAt > 30_000)
            {
                _window.TryEnsureForeground(launcher.Hwnd, TimeSpan.FromSeconds(1));
                foreach (var word in startWords)
                {
                    var line = await _probe.FindLineAsync(launcher.Hwnd, FullFrame, word);
                    if (line is null) continue;
                    clicks++;
                    lastClickAt = Environment.TickCount64;
                    if (clicks > 1) _log.Warning("客户端仍未出现,重试点击启动器「{Word}」。", word);
                    else _log.Information("点击启动器「{Word}」。", word);
                    _probe.ClickFramePoint(launcher.Hwnd, line.CenterX, line.CenterY);
                    break;
                }
            }
            await Task.Delay(3000, ct);
        }

        string shot = "";
        if (_window.IsAlive(launcher.Hwnd))
            try { (shot, _) = await _probe.DumpAsync(launcher.Hwnd, "fail-启动器"); }
            catch (Exception ex) { _log.Error(ex, "保存启动器现场失败。"); }
        throw new StepFailedException("启动游戏客户端",
            $"{s.LaunchTimeoutSeconds}s 内游戏客户端窗口未出现(启动器可能在更新或等待登录)。" +
            (shot.Length > 0 ? $"诊断截图:{shot}" : ""), shot.Length > 0 ? shot : null);
    }

    private async Task NavigateToLobbyAsync(nint hwnd, AppSettings s, CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + s.LobbyTimeoutSeconds * 1000L;
        int escUsed = 0, unknownStreak = 0;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!_window.IsAlive(hwnd))
                throw new StepFailedException("等待大厅", "游戏窗口中途消失(游戏崩溃或被手动关闭)。");

            _window.TryEnsureForeground(hwnd, TimeSpan.FromSeconds(1));
            string? screen = await _probe.WhichScreenAsync(hwnd, KnownScreens);
            switch (screen)
            {
                case AnchorKeys.Lobby:
                case AnchorKeys.SpecOpsHome:
                    _log.Information("已到达{Screen}。", screen == AnchorKeys.Lobby ? "大厅" : "特勤处");
                    return;
                case AnchorKeys.ModeSelect:
                    _log.Information("模式选择界面,点击「烽火地带」。");
                    _probe.ClickPoint(hwnd, AnchorKeys.ModeSelect, AnchorKeys.PointModeEntry);
                    unknownStreak = 0;
                    await Task.Delay(2500, ct);
                    break;
                case AnchorKeys.Safehouse:
                    _log.Information("特勤基地界面,按 Tab 进入大厅。");
                    _input.PressTab();
                    unknownStreak = 0;
                    await Task.Delay(2500, ct);
                    break;
                default:
                    unknownStreak++;
                    if (unknownStreak % 4 == 0 && escUsed < 3)
                    {
                        escUsed++;
                        _log.Information("画面未识别,按 ESC 尝试关闭弹窗({N}/3)。", escUsed);
                        _input.PressEscape();
                    }
                    await Task.Delay(3000, ct);
                    break;
            }
        }

        var (png, dump) = await _probe.DumpAsync(hwnd, "fail-等待大厅");
        throw new StepFailedException("等待大厅",
            $"{s.LobbyTimeoutSeconds}s 内未到达大厅(登录卡住或锚点需微调)。诊断截图:{png}", png, dump);
    }
}
