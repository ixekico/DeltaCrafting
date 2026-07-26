using CommunityToolkit.Mvvm.ComponentModel;
using DeltaCrafter.App.Controls;
using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L3;
using Microsoft.UI.Dispatching;

namespace DeltaCrafter.App.ViewModels;

/// <summary>标题栏状态胶囊。协调器事件来自后台线程,统一经 DispatcherQueue 回 UI。</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();

    [ObservableProperty] private string statusText = "空闲";
    [ObservableProperty] private StatusLevel statusLevel = StatusLevel.Neutral;

    public ShellViewModel(AutomationCoordinator coordinator)
    {
        coordinator.StatusChanged += s => _dq.TryEnqueue(() => Apply(s));
        Apply(coordinator.Status);
    }

    private void Apply(CoordinatorStatus s)
    {
        (StatusText, StatusLevel) = s.Mode switch
        {
            EngineMode.Running => ("执行中", StatusLevel.Info),
            EngineMode.WaitingSchedule =>
                (s.NextRunAt is { } n ? $"等待 · {n:HH:mm}" : "等待计划", StatusLevel.Success),
            EngineMode.Faulted => ("上轮失败", StatusLevel.Critical),
            _ => ("空闲", StatusLevel.Neutral),
        };
    }
}
