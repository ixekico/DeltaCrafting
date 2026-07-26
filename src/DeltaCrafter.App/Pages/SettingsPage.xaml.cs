using DeltaCrafter.App.Services;
using DeltaCrafter.App.ViewModels;
using DeltaCrafter.Core.L1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeltaCrafter.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel Vm { get; } = AppHost.Current.SettingsVm;

    public SettingsPage() => InitializeComponent();

    /// <summary>
    /// 定位游戏窗口:列出候选窗口让用户点选,把标题+类名固化为匹配规则。
    /// 一次性人工确认胜过猜测窗口类名——不同渠道/版本类名可能不同。
    /// </summary>
    private async void OnPickWindow(object sender, RoutedEventArgs e)
    {
        var candidates = Vm.ListCandidateWindows();
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = candidates.Select(w => $"{w.Title}   ·   {w.ClassName}").ToList(),
            MaxHeight = 320,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "选择游戏窗口",
            Content = candidates.Count == 0
                ? new TextBlock { Text = "未发现可见窗口。请先启动游戏后重试。", TextWrapping = TextWrapping.Wrap }
                : list,
            PrimaryButtonText = "绑定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && list.SelectedIndex >= 0)
        {
            Vm.ApplyWindowChoice(candidates[list.SelectedIndex]);
        }
    }
}
