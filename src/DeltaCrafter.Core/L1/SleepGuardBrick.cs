using DeltaCrafter.Core.L1.Win32;
using Serilog;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 阻止系统睡眠(不阻止熄屏)。ES_CONTINUOUS 状态与调用线程绑定、线程退出即失效,
/// 因此由本类的常驻后台线程统一持有;外部只调 SetActive 表达意图。
/// 申请失败(返回 0)记错误日志并保持原状态标记,让"没防住睡眠"可被诊断而非无声失效。
/// </summary>
public sealed class SleepGuardBrick : IDisposable
{
    private readonly ILogger _log;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _thread;
    private volatile bool _wantActive;
    private volatile bool _disposed;

    public SleepGuardBrick(ILogger log)
    {
        _log = log.ForContext<SleepGuardBrick>();
        _thread = new Thread(Loop) { IsBackground = true, Name = "SleepGuard" };
        _thread.Start();
    }

    public void SetActive(bool active)
    {
        if (_wantActive == active) return;
        _wantActive = active;
        _signal.Set();
    }

    private void Loop()
    {
        bool applied = false;
        while (true)
        {
            _signal.WaitOne();
            if (_disposed) break;
            bool want = _wantActive;
            if (want == applied) continue;

            uint flags = want
                ? NativePowerApi.ES_CONTINUOUS | NativePowerApi.ES_SYSTEM_REQUIRED
                : NativePowerApi.ES_CONTINUOUS;
            if (NativePowerApi.SetThreadExecutionState(flags) == 0)
            {
                _log.Error("SetThreadExecutionState 失败,防睡眠状态未生效(want={Want})。", want);
                continue;
            }
            applied = want;
            _log.Information(want ? "已开启防睡眠。" : "已解除防睡眠。");
        }
        // 线程退出前恢复默认电源策略。
        NativePowerApi.SetThreadExecutionState(NativePowerApi.ES_CONTINUOUS);
    }

    public void Dispose()
    {
        _disposed = true;
        _signal.Set();
    }
}
