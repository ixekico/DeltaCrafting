using DeltaCrafter.Core.L0;
using Microsoft.UI.Xaml;

namespace DeltaCrafter.App.Services;

/// <summary>主题应用:改根元素 RequestedTheme(Mica 与系统资源随之切换),并同步标题栏按钮色。</summary>
public sealed class ThemeService
{
    private readonly Window _window;

    public ThemeService(Window window) => _window = window;

    public void Apply(ThemeChoice choice)
    {
        if (_window.Content is FrameworkElement root)
            root.RequestedTheme = choice switch
            {
                ThemeChoice.Light => ElementTheme.Light,
                ThemeChoice.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default, // 跟随系统
            };
        TitleBarHelper.ApplyButtonColors(_window);
    }
}
