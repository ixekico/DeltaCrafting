using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeltaCrafter.App.Services;

/// <summary>
/// 托盘图标与菜单。应用的常驻形态:窗口关闭只是隐藏,调度循环继续;
/// 真正退出仅经托盘「退出」。SecondWindow 菜单模式是 unpackaged WinUI 下的可靠选择。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayService(MainWindow window, Action runNow, Action exit)
    {
        var flyout = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "打开面板" };
        open.Click += (_, _) => window.RestoreFromTray();
        var run = new MenuFlyoutItem { Text = "立即开始制造" };
        run.Click += (_, _) => runNow();
        var quit = new MenuFlyoutItem { Text = "退出" };
        quit.Click += (_, _) => exit();
        flyout.Items.Add(open);
        flyout.Items.Add(run);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(quit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "三角洲特勤助手",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")),
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = flyout,
            LeftClickCommand = new RelayCommand(window.RestoreFromTray),
            NoLeftClickDelay = true,
        };
        _icon.ForceCreate();
    }

    public void Dispose() => _icon.Dispose();
}
