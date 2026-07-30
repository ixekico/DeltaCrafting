using DeltaCrafter.Core.L0;
using Serilog;

namespace DeltaCrafter.App.Services;

/// <summary>
/// 把一份完整行情快照写入当前利润模式设施。调用方保证在 UI 线程且位于两轮制造之间;
/// 本类只负责计划变更、落盘和卡片定点刷新,不负责网络、缓存或调度。
/// </summary>
public sealed class ProfitPlanApplier
{
    private readonly AppHost _host;
    private readonly ILogger _log;

    public ProfitPlanApplier(AppHost host, ILogger log)
    {
        _host = host;
        _log = log;
    }

    public string Apply(
        ProfitRecommendationSet recommendations,
        DateTimeOffset fetchedAt,
        IReadOnlyCollection<FacilityKey>? targetKeys,
        string? refreshError)
    {
        var profitPlans = _host.Plan.Facilities
            .Where(f => f.Mode != CraftMode.Custom
                && (targetKeys is null || targetKeys.Contains(f.Key)))
            .ToList();
        if (profitPlans.Count == 0) return "";

        bool planDirty = false;
        var replacedItems = new List<FacilityKey>();
        var statusItems = new List<string>();
        foreach (var plan in profitPlans)
        {
            var rec = recommendations.ForFacility(plan.Key, plan.Mode);
            string metric = DescribeMetric(plan.Mode);
            string resolved = _host.ResolveCatalogMatchKey(plan.Key, rec.ItemName) ?? "";
            statusItems.Add(
                $"{FacilityKeys.DisplayName(plan.Key)}={rec.ItemName}({metric})");
            if (plan.ItemName == rec.ItemName)
            {
                // 目录后来收录该物品时补写 OCR 匹配键;解析不出则保留原值,
                // 不能用空值覆盖一份仍可执行的已知匹配。
                if (resolved.Length > 0 && plan.MatchName != resolved)
                {
                    plan.MatchName = resolved;
                    planDirty = true;
                }
                continue;
            }

            _log.Information("{Facility} 计划物品:{Old} → {New}({Metric} {Profit:N0})。",
                FacilityKeys.DisplayName(plan.Key),
                plan.ItemName.Length > 0 ? plan.ItemName : "未选物品",
                rec.ItemName, metric, rec.Profit);
            plan.ItemName = rec.ItemName;
            plan.MatchName = resolved;
            planDirty = true;
            replacedItems.Add(plan.Key);
        }

        if (planDirty) _host.SavePlan();
        if (replacedItems.Count > 0) _host.PlanVm.RefreshFacilities(replacedItems);
        string status = $"已应用行情缓存({fetchedAt:HH:mm}):"
            + string.Join(";", statusItems);
        return refreshError is null ? status : status + "\n" + refreshError;
    }

    private static string DescribeMetric(CraftMode mode) => mode switch
    {
        CraftMode.HourlyProfit => "每小时利润最高",
        CraftMode.TotalProfit => "总利润最高",
        CraftMode.Custom => throw new InvalidOperationException("自定义制造模式没有利润口径。"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知制造模式。"),
    };
}
