using DeltaCrafter.Core.L3;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class ProfitPlanCoordinatorTests
{
    [Theory]
    [InlineData(13, 0, 14, 0)]
    [InlineData(13, 17, 14, 0)]
    [InlineData(13, 59, 14, 0)]
    public void Successful_fetch_schedules_the_next_whole_hour(
        int hour,
        int minute,
        int expectedHour,
        int expectedMinute)
    {
        var now = new DateTimeOffset(
            2026, 7, 30, hour, minute, 25, TimeSpan.FromHours(8));

        var next = ProfitPlanCoordinator.NextRefreshAttemptAt(
            now, lastFetchSucceeded: true);

        Assert.Equal(expectedHour, next.Hour);
        Assert.Equal(expectedMinute, next.Minute);
        Assert.Equal(0, next.Second);
        Assert.Equal(now.Offset, next.Offset);
    }

    [Theory]
    [InlineData(13, 17, 13, 27)]
    [InlineData(13, 55, 14, 0)]
    public void Failed_fetch_retries_without_skipping_an_earlier_whole_hour(
        int hour,
        int minute,
        int expectedHour,
        int expectedMinute)
    {
        var now = new DateTimeOffset(
            2026, 7, 30, hour, minute, 0, TimeSpan.FromHours(8));

        var next = ProfitPlanCoordinator.NextRefreshAttemptAt(
            now, lastFetchSucceeded: false);

        Assert.Equal(expectedHour, next.Hour);
        Assert.Equal(expectedMinute, next.Minute);
        Assert.Equal(0, next.Second);
    }
}
