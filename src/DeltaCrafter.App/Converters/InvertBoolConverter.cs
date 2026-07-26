using Microsoft.UI.Xaml.Data;

namespace DeltaCrafter.App.Converters;

/// <summary>布尔取反(执行中禁用「执行」按钮等)。非布尔输入按 false 处理并取反为 true。</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is not bool b || !b;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not bool b || !b;
}
