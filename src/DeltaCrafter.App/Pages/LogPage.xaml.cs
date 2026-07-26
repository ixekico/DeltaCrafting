using DeltaCrafter.App.Services;
using DeltaCrafter.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DeltaCrafter.App.Pages;

public sealed partial class LogPage : Page
{
    public LogViewModel Vm { get; } = AppHost.Current.LogVm;

    public LogPage() => InitializeComponent();
}
