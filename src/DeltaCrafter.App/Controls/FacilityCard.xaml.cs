using DeltaCrafter.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeltaCrafter.App.Controls;

/// <summary>总览页设施卡视图。数据全部来自 FacilityCardModel,本类只做绑定壳。</summary>
public sealed partial class FacilityCard : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(FacilityCardModel), typeof(FacilityCard), new PropertyMetadata(null));

    public FacilityCard() => InitializeComponent();

    public FacilityCardModel? Model
    {
        get => (FacilityCardModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
