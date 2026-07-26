using DeltaCrafter.App.Services;
using DeltaCrafter.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeltaCrafter.App.Pages;

/// <summary>
/// 制造计划页。物品选择用 AutoSuggestBox 搜索式交互:键入即过滤、聚焦展开全部候选。
/// 写入(选中/回车/失焦)与回显(加载后回填)走事件显式处理,不依赖 Text 绑定
/// (沿袭可编辑 ComboBox 时代验证过的可靠做法);目录之外的自定义物品名仍可手填。
/// </summary>
public sealed partial class PlanPage : Page
{
    public PlanViewModel Vm { get; } = AppHost.Current.PlanVm;

    public PlanPage() => InitializeComponent();

    /// <summary>DataContext 就绪或变化时用模型值回填显示。挂 DataContextChanged 而非 Loaded:
    /// ItemsRepeater 会复用模板控件,复用瞬间若不回填,上一个设施的残留文本
    /// 会在失焦落盘时写进当前设施——必须在换绑当下同步。</summary>
    private void OnItemBoxDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is AutoSuggestBox box && args.NewValue is PlanFacilityModel model)
            box.Text = model.ItemName;
    }

    /// <summary>聚焦即展开候选列表(按当前文本过滤;空文本 = 全部),保留“浏览全目录”体验。</summary>
    private void OnItemBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not AutoSuggestBox box || box.DataContext is not PlanFacilityModel model) return;
        var matches = model.Filter(box.Text);
        box.ItemsSource = matches;
        box.IsSuggestionListOpen = matches.Count > 0;
    }

    /// <summary>用户键入 → 实时过滤候选。程序性回填(选中/回显)不重开列表。</summary>
    private void OnItemTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (sender.DataContext is not PlanFacilityModel model) return;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = model.Filter(sender.Text);
    }

    /// <summary>点选候选或按回车确认 → 立即写入模型并落盘(清空+回车 = 清除选择)。</summary>
    private void OnItemQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is not PlanFacilityModel model) return;
        model.ItemName = (args.ChosenSuggestion as string) ?? args.QueryText?.Trim() ?? "";
        sender.Text = model.ItemName;
    }

    /// <summary>
    /// 输入后未确认直接点别处:失焦时落盘当前文本(目录外自定义名同样有效)。
    /// 显示为空而模型有值时,视为控件清空显示而非用户清除意图 → 回填显示,不写空值
    /// (用户想清除应清空后按回车,走 QuerySubmitted)。
    /// </summary>
    private void OnItemBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not AutoSuggestBox box || box.DataContext is not PlanFacilityModel model) return;
        string text = box.Text?.Trim() ?? "";
        if (text.Length > 0)
            model.ItemName = text;
        else if (model.ItemName.Length > 0)
            box.Text = model.ItemName;
    }
}
