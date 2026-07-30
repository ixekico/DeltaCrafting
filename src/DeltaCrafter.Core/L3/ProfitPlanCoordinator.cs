using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;

namespace DeltaCrafter.Core.L3;

/// <summary>
/// 面向应用层的利润推荐入口。L1 负责网络与解析,
/// 本类保持 UI 只依赖 L3/L0,不直接穿透到积木层(与 UpdateCoordinator 同型)。
/// </summary>
public sealed class ProfitPlanCoordinator
{
    public static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(10);

    private readonly ProfitAdvisorBrick _brick = new();

    public Task<ProfitRecommendationSet> FetchRecommendationsAsync(CancellationToken ct) =>
        _brick.FetchRecommendationsAsync(ct);

    /// <summary>
    /// 成功后固定等到下一个本地整点;失败时 10 分钟重试,但不会跨过更早到来的整点。
    /// 启动预热由调用方立即执行,不经过此计算。
    /// </summary>
    public static DateTimeOffset NextRefreshAttemptAt(
        DateTimeOffset now,
        bool lastFetchSucceeded)
    {
        var nextWholeHour = new DateTimeOffset(
            now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset).AddHours(1);
        if (lastFetchSucceeded) return nextWholeHour;

        var retryAt = now.Add(FailureRetryDelay);
        return retryAt < nextWholeHour ? retryAt : nextWholeHour;
    }
}
