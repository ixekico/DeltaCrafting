using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeltaCrafter.App.Services;
using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;

namespace DeltaCrafter.App.ViewModels;

/// <summary>
/// 设置页。所有属性即改即存;开机自启走 schtasks,失败原因原样显示在
/// AutostartError(InfoBar),开关状态回弹到真实值——不假装成功。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly ThemeService _theme;
    private bool _autostartEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutostartError))]
    private string autostartError = "";

    [ObservableProperty] private string gamePath;
    [ObservableProperty] private string windowRuleText;

    public bool HasAutostartError => !string.IsNullOrEmpty(AutostartError);

    public SettingsViewModel(AppHost host, ThemeService theme)
    {
        _host = host;
        _theme = theme;
        gamePath = S.GamePath;
        windowRuleText = DescribeRule();
        try { _autostartEnabled = host.Autostart.IsEnabled(); }
        catch (Exception ex) { AutostartError = ex.Message; }
    }

    private AppSettings S => _host.Settings;
    private void Save() => _host.SaveSettings();

    partial void OnGamePathChanged(string value)
    {
        S.GamePath = value ?? "";
        Save();
    }

    public double LaunchTimeoutSeconds
    {
        get => S.LaunchTimeoutSeconds;
        set { if (!double.IsNaN(value)) { S.LaunchTimeoutSeconds = Math.Clamp((int)value, 30, 900); Save(); OnPropertyChanged(); } }
    }

    public double LobbyTimeoutSeconds
    {
        get => S.LobbyTimeoutSeconds;
        set { if (!double.IsNaN(value)) { S.LobbyTimeoutSeconds = Math.Clamp((int)value, 30, 900); Save(); OnPropertyChanged(); } }
    }

    public double FailureRetryMinutes
    {
        get => S.FailureRetryMinutes;
        set { if (!double.IsNaN(value)) { S.FailureRetryMinutes = Math.Clamp((int)value, 1, 720); Save(); OnPropertyChanged(); } }
    }

    public int AfterRunIndex
    {
        get => S.AfterRun switch
        {
            AfterRunAction.CloseGame => 0,
            AfterRunAction.KeepRunning => 1,
            _ => 2, // KeepAtLobby
        };
        set
        {
            S.AfterRun = value switch
            {
                0 => AfterRunAction.CloseGame,
                1 => AfterRunAction.KeepRunning,
                _ => AfterRunAction.KeepAtLobby,
            };
            Save();
            OnPropertyChanged();
        }
    }

    public bool PreventSleep
    {
        get => S.PreventSleepWhileWaiting;
        set { S.PreventSleepWhileWaiting = value; Save(); OnPropertyChanged(); }
    }

    public bool CloseToTray
    {
        get => S.CloseToTray;
        set { S.CloseToTray = value; Save(); OnPropertyChanged(); }
    }

    public int ThemeIndex
    {
        get => (int)S.Theme;
        set { S.Theme = (ThemeChoice)value; Save(); _theme.Apply(S.Theme); OnPropertyChanged(); }
    }

    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        set
        {
            if (_autostartEnabled == value) return;
            try
            {
                if (value) _host.Autostart.Enable(Environment.ProcessPath!);
                else _host.Autostart.Disable();
                _autostartEnabled = value;
                AutostartError = "";
            }
            catch (Exception ex)
            {
                AutostartError = ex.Message; // 开关回弹到真实状态
            }
            OnPropertyChanged();
        }
    }

    /// <summary>开发者模式:控制总览页「单步调试」显隐,改动即刻生效。</summary>
    public bool DeveloperMode
    {
        get => S.DeveloperMode;
        set
        {
            if (S.DeveloperMode == value) return;
            S.DeveloperMode = value;
            Save();
            OnPropertyChanged();
            _host.OverviewVm.NotifyDeveloperModeChanged();
        }
    }

    public string VersionText =>
        "v" + (typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    [RelayCommand]
    private void BrowseGamePath()
    {
        nint hwnd = App.MainWindowRef is { } w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : 0;
        var picked = Win32Dialogs.PickExeFile(hwnd);
        if (picked is not null) GamePath = picked; // 属性变更钩子负责落盘
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo { FileName = _host.Paths.Root, UseShellExecute = true });

    /// <summary>供窗口选择对话框使用:候选窗口(剔除本进程自己)。</summary>
    public IReadOnlyList<GameWindowInfo> ListCandidateWindows()
    {
        int selfPid = Environment.ProcessId;
        return _host.WindowBrick.ListCandidates().Where(w => w.ProcessId != selfPid).ToList();
    }

    public void ApplyWindowChoice(GameWindowInfo chosen)
    {
        S.WindowMatch.ExactTitle = chosen.Title;
        S.WindowMatch.ClassName = chosen.ClassName;
        Save();
        WindowRuleText = DescribeRule();
        _host.Log.Information("窗口匹配规则已绑定:{Title} / {Class}", chosen.Title, chosen.ClassName);
    }

    [RelayCommand]
    private void ResetWindowRule()
    {
        S.WindowMatch.ExactTitle = null;
        S.WindowMatch.ClassName = null;
        Save();
        WindowRuleText = DescribeRule();
    }

    private string DescribeRule() => S.WindowMatch.ExactTitle is { Length: > 0 } t
        ? $"精确标题:{t}"
        : $"标题包含:{S.WindowMatch.TitleContains}";
}
