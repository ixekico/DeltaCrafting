using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Serilog.Events;

namespace DeltaCrafter.App.Converters;

/// <summary>
/// 日志等级 → 前景色。在绑定时(UI 线程、元素已入树)按当前主题解析 {ThemeResource},
/// 列表项虚拟化重建时会重跑,因而随主题切换自然更新;不在 ViewModel 里缓存 Brush。
/// 键缺失属编码错误,直接让其抛出以便开发期发现,不静默兜底成透明色。
/// </summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string key = value is LogEventLevel level && level >= LogEventLevel.Error ? "SystemFillColorCriticalBrush"
            : value is LogEventLevel w && w == LogEventLevel.Warning ? "SystemFillColorCautionBrush"
            : value is LogEventLevel i && i == LogEventLevel.Information ? "SystemFillColorSuccessBrush"
            : "TextFillColorTertiaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
