using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeltaCrafter.App.Controls;
using DeltaCrafter.Core.L0;
using Microsoft.UI.Xaml;

namespace DeltaCrafter.App.ViewModels;

/// <summary>总览页单个设施卡的展示模型。倒计时文本由 1s 定时器驱动 Tick 刷新。</summary>
public sealed partial class FacilityCardModel : ObservableObject
{
    public FacilityKey Key { get; }
    public string Name { get; }

    private readonly Func<FacilityCardModel, Task> _cancel;
    private FacilityPhase _phase = FacilityPhase.Unknown;
    private DateTimeOffset? _readyAt;

    [ObservableProperty] private string itemName = "—";
    [ObservableProperty] private string phaseText = "未观察";
    [ObservableProperty] private StatusLevel badgeLevel = StatusLevel.Neutral;
    [ObservableProperty] private string countdownText = "—";
    [ObservableProperty] private string readyAtText = " ";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelVisibility))]
    private bool canCancel;

    public FacilityCardModel(FacilityKey key, string name, Func<FacilityCardModel, Task> cancel)
    {
        Key = key;
        Name = name;
        _cancel = cancel;
    }

    /// <summary>仅"制造中"才允许取消(取消=游戏内点「中止」,会销毁材料)。</summary>
    public Visibility CancelVisibility => CanCancel ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    private Task CancelAsync() => _cancel(this);

    public void Update(FacilityRuntime rt)
    {
        _phase = rt.Phase;
        _readyAt = rt.ReadyAt;
        ItemName = string.IsNullOrEmpty(rt.ItemName) ? "—" : rt.ItemName;
        CanCancel = rt.Phase == FacilityPhase.Crafting;

        (PhaseText, BadgeLevel) = rt.Phase switch
        {
            FacilityPhase.Crafting => ("制造中", StatusLevel.Info),
            FacilityPhase.ReadyToCollect => ("可领取", StatusLevel.Success),
            FacilityPhase.NeedsManual => ("需人工", StatusLevel.Caution),
            FacilityPhase.Idle => ("空闲", StatusLevel.Neutral),
            _ => ("未观察", StatusLevel.Neutral),
        };

        ReadyAtText = rt.Phase switch
        {
            FacilityPhase.Crafting when rt.ReadyAt is { } r => $"预计完成 {r:MM-dd HH:mm}",
            FacilityPhase.NeedsManual when rt.ManualReason is { Length: > 0 } reason => reason,
            _ => " ",
        };
        Tick(DateTimeOffset.Now);
    }

    public void Tick(DateTimeOffset now)
    {
        if (_phase == FacilityPhase.Crafting && _readyAt is { } r)
        {
            var left = r - now;
            CountdownText = left <= TimeSpan.Zero
                ? "已完成,待收取"
                : $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
        }
        else
        {
            CountdownText = _phase == FacilityPhase.ReadyToCollect ? "可领取" : "—";
        }
    }
}
