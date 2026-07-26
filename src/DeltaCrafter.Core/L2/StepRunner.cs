using DeltaCrafter.Core.L0;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>一个自动化步骤 = 动作 + 后置校验 + 超时。校验函数必须基于新截帧判断。</summary>
public sealed record Step(
    string Name,
    Action Act,
    Func<Task<bool>> Verify,
    TimeSpan Timeout,
    bool RetryOnce = true);

/// <summary>
/// 步骤执行器,唯一的失败出口。策略:
/// 动作后轮询校验直至超时;允许一次显式重试(记 Warning);再失败即保存现场
/// (截图 + OCR 转储)并抛 StepFailedException 中止本轮。没有静默续跑路径。
/// </summary>
public sealed class StepRunner
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);
    private readonly ScreenProbe _probe;
    private readonly ILogger _log;

    public StepRunner(ScreenProbe probe, ILogger log)
    {
        _probe = probe;
        _log = log.ForContext<StepRunner>();
    }

    public async Task RunAsync(nint hwnd, Step step, CancellationToken ct)
    {
        int attempts = step.RetryOnce ? 2 : 1;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            _log.Debug("执行步骤[{Name}](第 {Attempt} 次)", step.Name, attempt);
            step.Act();
            if (await PollVerifyAsync(step, ct))
            {
                _log.Debug("步骤[{Name}]完成。", step.Name);
                return;
            }
            if (attempt < attempts)
                _log.Warning("步骤[{Name}]在 {Timeout}s 内未通过校验,重试一次。",
                    step.Name, step.Timeout.TotalSeconds);
        }

        string png = "", dump = "";
        try
        {
            (png, dump) = await _probe.DumpAsync(hwnd, "fail-" + Sanitize(step.Name));
        }
        catch (Exception ex)
        {
            // 现场保存失败只削弱诊断,不改变"步骤已失败"这一事实。
            _log.Error(ex, "保存步骤[{Name}]失败现场时出错。", step.Name);
        }
        throw new StepFailedException(step.Name,
            $"{step.Timeout.TotalSeconds:F0}s 内未到达预期状态。诊断截图:{(png.Length > 0 ? png : "保存失败")}",
            png.Length > 0 ? png : null, dump.Length > 0 ? dump : null);
    }

    private async Task<bool> PollVerifyAsync(Step step, CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + (long)step.Timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await step.Verify()) return true;
            await Task.Delay(PollInterval, ct);
        }
        return await step.Verify();
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Where(c => !invalid.Contains(c)));
    }
}
