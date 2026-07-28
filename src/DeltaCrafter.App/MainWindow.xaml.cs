using DeltaCrafter.App.Pages;
using DeltaCrafter.App.Services;
using DeltaCrafter.App.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeltaCrafter.App;

/// <summary>
/// 应用壳:Mica 背景 + 自定义标题栏 + 左侧导航。关闭按钮默认最小化到托盘
/// (CloseToTray 设置),真正退出只经 RequestExit(托盘菜单)。
/// </summary>
public sealed partial class MainWindow : Window
{
    public ShellViewModel Shell { get; }
    private bool _exitRequested;

    public MainWindow()
    {
        Shell = AppHost.Current.ShellVm;
        InitializeComponent();

        Title = "三角洲特勤助手";
        // Mica 云母:Win11 生效;更旧系统框架自动回退纯色背景(仅视觉降级,已记日志)。
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new SizeInt32(1160, 720));
        AppWindow.Closing += OnClosing;

        if (Content is FrameworkElement root)
            root.ActualThemeChanged += (_, _) => TitleBarHelper.ApplyButtonColors(this);
        TitleBarHelper.ApplyButtonColors(this);

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public void RestoreFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    public void RequestExit()
    {
        _exitRequested = true;
        AppHost.Current.Shutdown();
        Application.Current.Exit();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (!_exitRequested && AppHost.Current.Settings.CloseToTray)
        {
            e.Cancel = true;
            sender.Hide();
            return;
        }
        AppHost.Current.Shutdown();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        // 设置页走自定义底部项(内置 Settings 项已禁用,其文案会跟随系统语言变英文)。
        Type? page = ((e.SelectedItem as NavigationViewItem)?.Tag as string) switch
        {
            "overview" => typeof(OverviewPage),
            "plan" => typeof(PlanPage),
            "logs" => typeof(LogPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };
        if (page is not null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }
}
