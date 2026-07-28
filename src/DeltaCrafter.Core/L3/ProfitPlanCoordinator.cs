using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;

namespace DeltaCrafter.Core.L3;

/// <summary>
/// 面向应用层的利润推荐入口。L1 负责网络与解析,
/// 本类保持 UI 只依赖 L3/L0,不直接穿透到积木层(与 UpdateCoordinator 同型)。
/// </summary>
public sealed class ProfitPlanCoordinator
{
    /// <summary>利润模式下的推荐刷新周期(需求约定:每 2 小时更新一次)。</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(2);

    private readonly ProfitAdvisorBrick _brick = new();

    public Task<ProfitRecommendationSet> FetchRecommendationsAsync(CancellationToken ct) =>
        _brick.FetchRecommendationsAsync(ct);
}
