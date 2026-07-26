using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeltaCrafter.App.Controls;

/// <summary>
/// 圆角状态胶囊。Level 决定背景色,但颜色由 XAML VisualState 的 {ThemeResource} 提供,
/// 主题切换时自动重解析——因此这里绝不在代码里持有或查找 Brush(那样主题切换不刷新)。
/// </summary>
public sealed partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(""));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(StatusLevel), typeof(StatusBadge),
        new PropertyMetadata(StatusLevel.Neutral, OnLevelChanged));

    public StatusBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => GoToLevelState(false); // 首帧同步到初始 Level
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusLevel Level
    {
        get => (StatusLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusBadge)d).GoToLevelState(true);

    private void GoToLevelState(bool useTransitions) =>
        VisualStateManager.GoToState(this, Level.ToString(), useTransitions);
}
