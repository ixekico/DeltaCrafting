using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeltaCrafter.App.Services;
using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L3;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog.Events;

namespace DeltaCrafter.App.ViewModels;

/// <summary>
/// 总览页:运行状态、四张设施卡、最近日志与手动/单步操作。
/// 长任务一律 Task.Run 丢后台——流程内部有同步点击延时,不能占 UI 线程。
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly AutomationCoordinator _coordinator;
    private readonly AppHost _host;
    private readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _ticker;

    public ObservableCollection<FacilityCardModel> Facilities { get; } = [];
    public ObservableCollection<string> RecentLogs { get; } = [];

    /// <summary>取消前的二次确认(销毁性操作)。由页面提供:显示 ContentDialog 返回是否确认。
    /// 未设置(理论上不会)时保守返回 false,即不执行中止。</summary>
    public Func<FacilityCardModel, Task<bool>>? ConfirmCancelAsync { get; set; }

    [ObservableProperty] private string runStateTitle = "空闲";
    [ObservableProperty] private string runStateDetail = "";
    [ObservableProperty] private string nextRunText = "—";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool lastRunFailed;
    [ObservableProperty] private string lastRunSummary = "";

    public OverviewViewModel(AutomationCoordinator coordinator, UiLogSink sink, AppHost host)
    {
        _coordinator = coordinator;
        _host = host;
        foreach (var key in FacilityKeys.All)
            Facilities.Add(new FacilityCardModel(key, FacilityKeys.DisplayName(key), HandleCancelAsync));

        coordinator.StatusChanged += s => _dq.TryEnqueue(RefreshAll);
        sink.Emitted += line => _dq.TryEnqueue(() => PushLog(line));
        foreach (var line in sink.Snapshot().TakeLast(6)) PushLog(line);
        RefreshAll();

        _ticker = _dq.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(1);
        _ticker.Tick += (_, _) =>
        {
            var now = DateTimeOffset.Now;
            foreach (var f in Facilities) f.Tick(now);
        };
        _ticker.Start();
    }

    private void RefreshAll()
    {
        var s = _coordinator.Status;
        IsRunning = s.Mode == EngineMode.Running;
        RunStateTitle = s.Mode switch
        {
            EngineMode.Running => "执行中",
            EngineMode.WaitingSchedule => "等待计划",
            EngineMode.Faulted => "上轮失败",
            _ => "空闲",
        };
        RunStateDetail = s.Detail;
        NextRunText = s.NextRunAt is { } n ? n.ToString("MM-dd HH:mm:ss") : "—";

        var snapshot = _coordinator.FacilitySnapshot();
        foreach (var model in Facilities)
        {
            var rt = snapshot.FirstOrDefault(f => f.Key == model.Key);
            if (rt is not null) model.Update(rt);
        }

        var (lastAt, summary, failed) = _coordinator.LastRunInfo();
        LastRunFailed = failed;
        LastRunSummary = lastAt is { } t ? $"{t:MM-dd HH:mm} — {summary}" : "尚未执行过";
    }

    private void PushLog(UiLogLine line)
    {
        if (line.Level < LogEventLevel.Information) return;
        RecentLogs.Add($"{line.At:HH:mm:ss}  {line.Message}");
        while (RecentLogs.Count > 6) RecentLogs.RemoveAt(0);
    }

    [RelayCommand]
    private Task RunNowAsync() =>
        Task.Run(() => _coordinator.RunOnceAsync("手动触发", CancellationToken.None));

    /// <summary>「识别当前任务」:只观察并同步四设施进度,不领取不开工。
    /// 面向首次使用时特勤处里已经在制造的场景。</summary>
    [RelayCommand]
    private Task SyncNowAsync() =>
        Task.Run(() => _coordinator.SyncFacilitiesAsync("识别当前任务", CancellationToken.None));

    [RelayCommand]
    private void Stop() => _coordinator.RequestStop();

    /// <summary>取消某设施的制造:先二次确认,再后台执行中止(执行中不重复触发)。</summary>
    private async Task HandleCancelAsync(FacilityCardModel model)
    {
        if (IsRunning) return;
        var confirm = ConfirmCancelAsync;
        if (confirm is null || !await confirm(model)) return;
        await Task.Run(() => _coordinator.AbortFacilityAsync(model.Key));
    }

    [RelayCommand]
    private Task DebugLobbyAsync() => Task.Run(() => _coordinator.DebugEnsureLobbyAsync());

    [RelayCommand]
    private Task DebugSpecOpsAsync() => Task.Run(() => _coordinator.DebugEnterSpecOpsAsync());

    [RelayCommand]
    private Task DebugDumpAsync() => Task.Run(() => _coordinator.DebugDumpAsync());

    [RelayCommand]
    private Task DebugScanCatalogAsync() => Task.Run(() => _coordinator.DebugScanCatalogAsync());

    /// <summary>「单步调试」仅开发者模式显示(设置页最底部开关)。</summary>
    public Visibility DebugVisibility =>
        _host.Settings.DeveloperMode ? Visibility.Visible : Visibility.Collapsed;

    public void NotifyDeveloperModeChanged() => OnPropertyChanged(nameof(DebugVisibility));
}
