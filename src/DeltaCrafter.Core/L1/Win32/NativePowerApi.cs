using System.Runtime.InteropServices;

namespace DeltaCrafter.Core.L1.Win32;

/// <summary>电源请求声明。ES_CONTINUOUS 状态按线程记账,调用线程退出即失效——
/// 因此 SleepGuardBrick 必须用常驻线程持有,不能在 async 线程池上随手调用。</summary>
internal static class NativePowerApi
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SetThreadExecutionState(uint esFlags);

    internal const uint ES_CONTINUOUS = 0x80000000;
    internal const uint ES_SYSTEM_REQUIRED = 0x00000001;
}
