using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeltaCrafter.App.Controls;

/// <summary>
/// Win11 设置行样式卡片:图标 + 标题/说明 + 右侧操作区(Slot)。
/// 自研以避免引入第三方 UI 库;Slot 用显式属性元素语法赋值,
/// 不声明 ContentProperty——那会与 UserControl 自身内容机制冲突。
/// </summary>
public sealed partial class SettingCard : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingCard), new PropertyMetadata(""));

    public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(
        nameof(Slot), typeof(object), typeof(SettingCard), new PropertyMetadata(null));

    public SettingCard() => InitializeComponent();

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public object? Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    public Visibility GlyphVisibility(string? glyph) =>
        string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DescriptionVisibility(string? description) =>
        string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
}
