using System.Diagnostics;
using System.Text;
using Serilog;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 开机自启,基于计划任务(schtasks /RL HIGHEST):本程序需管理员权限,
/// 注册表 Run 键会在开机时弹 UAC,计划任务方案可静默以最高权限启动。
/// 所有 schtasks 失败都带退出码与原始输出抛出,不吞。
/// </summary>
public sealed class AutostartBrick
{
    private const string TaskName = "DeltaCrafter-AutoStart";
    private readonly ILogger _log;

    static AutostartBrick() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public AutostartBrick(ILogger log) => _log = log.ForContext<AutostartBrick>();

    public bool IsEnabled() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    public void Enable(string exePath)
    {
        var r = Run("/Create", "/F", "/RL", "HIGHEST", "/SC", "ONLOGON",
            "/TN", TaskName, "/TR", $"\"{exePath}\" --minimized");
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"创建开机自启任务失败(退出码 {r.ExitCode}):{r.Output}");
        _log.Information("已创建开机自启计划任务。");
    }

    public void Disable()
    {
        var r = Run("/Delete", "/F", "/TN", TaskName);
        if (r.ExitCode != 0 && IsEnabled())
            throw new InvalidOperationException($"删除开机自启任务失败(退出码 {r.ExitCode}):{r.Output}");
        _log.Information("已移除开机自启计划任务。");
    }

    private (int ExitCode, string Output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // schtasks 输出走 OEM 简体中文代码页;编码不对只影响报错文本可读性,不影响判定。
            StandardOutputEncoding = Encoding.GetEncoding(936),
            StandardErrorEncoding = Encoding.GetEncoding(936),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 schtasks.exe。");
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output.Trim());
    }
}
