using System.Diagnostics;
using DeltaCrafter.Core.L1.Win32;
using Serilog;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 游戏进程启停。只负责"拉起可执行文件"与"按窗口句柄关闭所属进程",
/// 是否到达大厅由流程层用画面判定,这里不猜测启动器的中间过程。
/// </summary>
public sealed class GameProcessBrick
{
    private readonly ILogger _log;

    public GameProcessBrick(ILogger log) => _log = log.ForContext<GameProcessBrick>();

    /// <summary>启动游戏/启动器。路径未配置或不存在直接抛错——这类配置错误必须暴露给用户。</summary>
    public void Launch(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            throw new InvalidOperationException("尚未设置游戏路径,请先到「设置」页选择游戏可执行文件。");
        if (!File.Exists(gamePath))
            throw new InvalidOperationException($"游戏路径不存在:{gamePath}");

        var psi = new ProcessStartInfo
        {
            FileName = gamePath,
            WorkingDirectory = Path.GetDirectoryName(gamePath)!,
            UseShellExecute = true,
        };
        // UseShellExecute 下 Start 可能返回 null(复用已有进程),不代表失败;
        // 后续一律以"游戏窗口是否出现"为准,不跟踪这里的进程句柄。
        Process.Start(psi);
        _log.Information("已启动游戏:{Path}", gamePath);
    }

    /// <summary>
    /// 关闭窗口所属进程:先礼后兵(CloseMainWindow → 宽限 → Kill 进程树)。
    /// 窗口/进程已不存在视为目标达成而非错误——本方法的契约是"调用后游戏不再运行"。
    /// </summary>
    public void CloseByWindow(nint hwnd, TimeSpan grace)
    {
        NativeWindowApi.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            _log.Information("关闭游戏:窗口已不存在,视为已关闭。");
            return;
        }

        Process proc;
        try { proc = Process.GetProcessById((int)pid); }
        catch (ArgumentException)
        {
            _log.Information("关闭游戏:进程 {Pid} 已退出。", pid);
            return;
        }

        using (proc)
        {
            proc.CloseMainWindow();
            if (!proc.WaitForExit((int)grace.TotalMilliseconds))
            {
                _log.Warning("游戏未在 {Grace}s 内响应关闭请求,强制结束进程树。", grace.TotalSeconds);
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(5000);
            }
            _log.Information("游戏已关闭。");
        }
    }
}
