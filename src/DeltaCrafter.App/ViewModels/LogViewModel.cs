using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeltaCrafter.App.Services;
using Microsoft.UI.Dispatching;
using Serilog.Events;

namespace DeltaCrafter.App.ViewModels;

/// <summary>日志行。Level 交由 XAML 转换器在绑定时解析颜色,VM 不持有 Brush。</summary>
public sealed record LogRow(string TimeText, string LevelText, string Message, LogEventLevel Level);

/// <summary>运行日志页:内存环形缓冲的过滤视图。完整历史在日志文件里,这里只管快速浏览。</summary>
public sealed partial class LogViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();
    private readonly List<UiLogLine> _all = [];

    public ObservableCollection<LogRow> Rows { get; } = [];

    /// <summary>0=全部 1=信息+ 2=警告+ 3=错误+</summary>
    [ObservableProperty] private int levelFilterIndex = 1;

    partial void OnLevelFilterIndexChanged(int value) => Rebuild();

    public LogViewModel(AppHost host)
    {
        _host = host;
        _all.AddRange(host.UiSink.Snapshot());
        host.UiSink.Emitted += line => _dq.TryEnqueue(() =>
        {
            _all.Add(line);
            if (_all.Count > 2000) _all.RemoveAt(0);
            if (Passes(line))
            {
                Rows.Add(ToRow(line));
                if (Rows.Count > 2000) Rows.RemoveAt(0);
            }
        });
        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var line in _all.Where(Passes)) Rows.Add(ToRow(line));
    }

    private bool Passes(UiLogLine line)
    {
        var threshold = LevelFilterIndex switch
        {
            1 => LogEventLevel.Information,
            2 => LogEventLevel.Warning,
            3 => LogEventLevel.Error,
            _ => LogEventLevel.Verbose,
        };
        return line.Level >= threshold;
    }

    private static LogRow ToRow(UiLogLine line)
    {
        string text = line.Level switch
        {
            LogEventLevel.Fatal or LogEventLevel.Error => "错误",
            LogEventLevel.Warning => "警告",
            LogEventLevel.Information => "信息",
            _ => "调试",
        };
        return new LogRow(line.At.ToString("HH:mm:ss"), text, line.Message, line.Level);
    }

    [RelayCommand]
    private void OpenLogsFolder() =>
        Process.Start(new ProcessStartInfo { FileName = _host.Paths.LogsDir, UseShellExecute = true });

    [RelayCommand]
    private void OpenShotsFolder() =>
        Process.Start(new ProcessStartInfo { FileName = _host.Paths.ShotsDir, UseShellExecute = true });

    [RelayCommand]
    private void ClearView()
    {
        _all.Clear();
        Rows.Clear();
        // 仅清屏;日志文件原样保留在 logs 目录。
    }
}
