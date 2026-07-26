using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace DeltaCrafter.App.Services;

/// <summary>
/// 沉浸式标题栏的系统按钮配色。窗口内容延伸到标题栏后,最小化/关闭按钮的
/// 背景与前景不会随主题自动适配,必须在主题变化时手动刷新。
/// </summary>
public static class TitleBarHelper
{
    public static void ApplyButtonColors(Window window)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return; // Win11 恒为 true,保护极端环境

        var titleBar = window.AppWindow.TitleBar;
        bool dark = window.Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Dark;

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonInactiveForegroundColor = dark
            ? Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E)
            : Color.FromArgb(0xFF, 0x61, 0x61, 0x61);
        titleBar.ButtonHoverBackgroundColor = dark
            ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
        titleBar.ButtonHoverForegroundColor = titleBar.ButtonForegroundColor;
        titleBar.ButtonPressedBackgroundColor = dark
            ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x18, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedForegroundColor = titleBar.ButtonForegroundColor;
    }
}
