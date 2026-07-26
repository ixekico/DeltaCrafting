using DeltaCrafter.App.Services;
using DeltaCrafter.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeltaCrafter.App.Pages;

public sealed partial class OverviewPage : Page
{
    public OverviewViewModel Vm { get; } = AppHost.Current.OverviewVm;

    public OverviewPage()
    {
        InitializeComponent();
        // 取消制造的二次确认由页面提供(需要 XamlRoot 弹 ContentDialog)。
        Vm.ConfirmCancelAsync = ConfirmCancelAsync;
    }

    private async Task<bool> ConfirmCancelAsync(FacilityCardModel model)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"取消{model.Name}的制造?",
            Content = $"将中止「{model.ItemName}」的制造。游戏通常不返还已消耗的材料,此操作不可撤销。",
            PrimaryButtonText = "中止制造",
            CloseButtonText = "返回",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
