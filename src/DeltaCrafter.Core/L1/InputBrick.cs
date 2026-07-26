using System.ComponentModel;
using System.Runtime.InteropServices;
using DeltaCrafter.Core.L1.Win32;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 模拟鼠标/按键(SendInput)。前提:本进程与游戏同权限(应用清单已要求管理员),
/// 否则系统会静默丢弃输入。SendInput 返回 0 视为硬错误立即抛出,绝不静默继续。
/// </summary>
public sealed class InputBrick
{
    /// <summary>点击屏幕物理坐标:先移动悬停再按放,间隔模拟人手,游戏 UI 需要悬停帧。</summary>
    public void ClickAt(int x, int y)
    {
        MoveTo(x, y);
        Thread.Sleep(70);
        Send(Mouse(NativeInputApi.MOUSEEVENTF_LEFTDOWN));
        Thread.Sleep(60);
        Send(Mouse(NativeInputApi.MOUSEEVENTF_LEFTUP));
        Thread.Sleep(80);
    }

    public void MoveTo(int x, int y)
    {
        var (nx, ny) = NormalizeToVirtualDesktop(x, y);
        var input = Mouse(NativeInputApi.MOUSEEVENTF_MOVE
            | NativeInputApi.MOUSEEVENTF_ABSOLUTE | NativeInputApi.MOUSEEVENTF_VIRTUALDESK);
        input.U.mi.dx = nx;
        input.U.mi.dy = ny;
        Send(input);
    }

    /// <summary>在指定位置滚动列表。notches 负值向下翻。每档间隔,避免游戏丢滚动事件。</summary>
    public void ScrollAt(int x, int y, int notches)
    {
        MoveTo(x, y);
        Thread.Sleep(60);
        int step = notches > 0 ? 1 : -1;
        for (int i = 0; i != notches; i += step)
        {
            var input = Mouse(NativeInputApi.MOUSEEVENTF_WHEEL);
            input.U.mi.mouseData = unchecked((uint)(NativeInputApi.WHEEL_DELTA * step));
            Send(input);
            Thread.Sleep(50);
        }
    }

    public void PressEscape() => PressKey(NativeInputApi.VK_ESCAPE);

    /// <summary>Tab 在游戏内为「进入下一界面」快捷键(基地→大厅)。</summary>
    public void PressTab() => PressKey(NativeInputApi.VK_TAB);

    private void PressKey(ushort vk)
    {
        Send(Key(vk, 0));
        Thread.Sleep(50);
        Send(Key(vk, NativeInputApi.KEYEVENTF_KEYUP));
    }

    private static INPUT Mouse(uint flags)
    {
        var i = new INPUT { type = NativeInputApi.INPUT_MOUSE };
        i.U.mi.dwFlags = flags;
        return i;
    }

    private static INPUT Key(ushort vk, uint flags)
    {
        var i = new INPUT { type = NativeInputApi.INPUT_KEYBOARD };
        i.U.ki.wVk = vk;
        i.U.ki.dwFlags = flags;
        return i;
    }

    private static void Send(INPUT input)
    {
        uint sent = NativeInputApi.SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SendInput 被系统拒绝:请确认本程序以管理员身份运行,且未被安全软件拦截。");
    }

    /// <summary>绝对坐标归一化到虚拟桌面(0..65535)。虚拟桌面原点可能为负(多显示器)。</summary>
    private static (int nx, int ny) NormalizeToVirtualDesktop(int x, int y)
    {
        int vx = NativeWindowApi.GetSystemMetrics(NativeWindowApi.SM_XVIRTUALSCREEN);
        int vy = NativeWindowApi.GetSystemMetrics(NativeWindowApi.SM_YVIRTUALSCREEN);
        int vw = NativeWindowApi.GetSystemMetrics(NativeWindowApi.SM_CXVIRTUALSCREEN);
        int vh = NativeWindowApi.GetSystemMetrics(NativeWindowApi.SM_CYVIRTUALSCREEN);
        if (vw <= 1 || vh <= 1)
            throw new InvalidOperationException("虚拟桌面尺寸异常,无法换算鼠标坐标。");
        int nx = Math.Clamp((int)Math.Round((x - vx) * 65535.0 / (vw - 1)), 0, 65535);
        int ny = Math.Clamp((int)Math.Round((y - vy) * 65535.0 / (vh - 1)), 0, 65535);
        return (nx, ny);
    }
}
