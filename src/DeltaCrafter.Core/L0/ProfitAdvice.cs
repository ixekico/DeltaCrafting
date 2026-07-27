namespace DeltaCrafter.Core.L0;

/// <summary>
/// 单个设施的制造利润推荐(来自 kkrb.net「特勤处制作产物推荐」)。
/// HourlyProfit 取该物品各设施等级中最高的小时利润(与网站展示口径一致);
/// TotalProfit 为单次制造的总利润。两个字段同源同物品,由 CraftMode 决定采信哪个口径。
/// </summary>
public sealed record ProfitRecommendation(
    FacilityKey Facility,
    string ItemName,
    double HourlyProfit,
    double TotalProfit);
