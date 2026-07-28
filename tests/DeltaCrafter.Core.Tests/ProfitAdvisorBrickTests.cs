using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using System.Net;
using System.Text.Json;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class ProfitAdvisorBrickTests
{
    /// <summary>
    /// 按 getOVData 实测结构裁剪。spData 与 sphData 故意给技术中心不同物品,
    /// 防止再次把总利润物品的 itemForge 误当成每小时利润榜首。
    /// </summary>
    private static string SampleJson(
        string totalTech = """{"itemName":"灵眼3/7测距狙击瞄准镜","profit":20682.03}""",
        string hourlyTech = """{"itemName":"幻影垂直握把","profit":4618.4175}""",
        string? totalArmory = """{"itemName":"H09 防暴头盔","profit":206003.19}""",
        string? hourlyArmory = """{"itemName":"H09 防暴头盔","profit":25750.39875}""")
    {
        string totalArmoryPart = totalArmory is null ? "" : @",""armory"":" + totalArmory;
        string hourlyArmoryPart = hourlyArmory is null ? "" : @",""armory"":" + hourlyArmory;
        return @"{""code"":1,""data"":{""spData"":{""tech"":" + totalTech
            + @",""workbench"":{""itemName"":""5.8x42mm DVC12 +P"",""profit"":247344.15}"
            + @",""pharmacy"":{""itemName"":""战地医疗箱"",""profit"":36163.19}"
            + totalArmoryPart
            + @"},""sphData"":{""tech"":" + hourlyTech
            + @",""workbench"":{""itemName"":""5.8x42mm DVC12 +P"",""profit"":30918.01875}"
            + @",""pharmacy"":{""itemName"":""战地医疗箱"",""profit"":4520.39875}"
            + hourlyArmoryPart + "}}}";
    }

    private static ProfitRecommendationSet Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ProfitAdvisorBrick.ParseOverviewData(doc.RootElement);
    }

    [Fact]
    public void Parses_distinct_total_and_hourly_recommendations_for_all_facilities()
    {
        var set = Parse(SampleJson());
        Assert.Equal(4, set.TotalProfitRecommendations.Count);
        Assert.Equal(4, set.HourlyProfitRecommendations.Count);

        var totalTech = Assert.Single(set.TotalProfitRecommendations,
            r => r.Facility == FacilityKey.TechCenter);
        Assert.Equal("灵眼3/7测距狙击瞄准镜", totalTech.ItemName);
        Assert.Equal(20682.03, totalTech.Profit, precision: 2);

        var hourlyTech = Assert.Single(set.HourlyProfitRecommendations,
            r => r.Facility == FacilityKey.TechCenter);
        Assert.Equal("幻影垂直握把", hourlyTech.ItemName);
        Assert.Equal(4618.4175, hourlyTech.Profit, precision: 4);

        Assert.Equal("5.8x42mm DVC12 +P",
            Assert.Single(set.TotalProfitRecommendations,
                r => r.Facility == FacilityKey.Workbench).ItemName);
        Assert.Equal("战地医疗箱",
            Assert.Single(set.HourlyProfitRecommendations,
                r => r.Facility == FacilityKey.PharmacyLab).ItemName);
        Assert.Equal("H09 防暴头盔",
            Assert.Single(set.TotalProfitRecommendations,
                r => r.Facility == FacilityKey.ArmorStation).ItemName);
    }

    [Fact]
    public void Craft_mode_selects_its_matching_recommendation_group()
    {
        var set = Parse(SampleJson());

        Assert.Equal("灵眼3/7测距狙击瞄准镜",
            Assert.Single(set.ForMode(CraftMode.TotalProfit),
                r => r.Facility == FacilityKey.TechCenter).ItemName);
        Assert.Equal("幻影垂直握把",
            Assert.Single(set.ForMode(CraftMode.HourlyProfit),
                r => r.Facility == FacilityKey.TechCenter).ItemName);
        Assert.Throws<InvalidOperationException>(() => set.ForMode(CraftMode.Custom));
    }

    [Fact]
    public void Missing_facility_throws_with_display_name()
    {
        // 四设施缺一即拒绝:残缺推荐不允许改用户计划。
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Parse(SampleJson(totalArmory: null)));
        Assert.Contains("防具台", ex.Message);
        Assert.Contains("总利润", ex.Message);
    }

    [Fact]
    public void Missing_hourly_facility_rejects_the_whole_snapshot()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Parse(SampleJson(hourlyArmory: null)));
        Assert.Contains("防具台", ex.Message);
        Assert.Contains("每小时利润", ex.Message);
    }

    [Fact]
    public void Missing_spData_throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            Parse("""{"code":1,"data":{"sphData":{}}}"""));

    [Fact]
    public void Missing_sphData_throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            Parse("""{"code":1,"data":{"spData":{}}}"""));

    [Fact]
    public void Empty_item_name_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(SampleJson(
            hourlyTech: """{"itemName":"  ","profit":1}""")));
        Assert.Contains("技术中心", ex.Message);
        Assert.Contains("每小时利润", ex.Message);
    }

    [Theory]
    [InlineData("""{"itemName":"物品A"}""")]
    [InlineData("""{"itemName":"物品A","profit":"暂无"}""")]
    public void Missing_or_non_numeric_profit_field_throws(string totalTech)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Parse(SampleJson(totalTech: totalTech)));
        Assert.Contains("总利润", ex.Message);
    }

    [Fact]
    public void Zero_or_negative_profit_is_accepted_as_real_market_data()
    {
        // 行情低谷利润可为 0 或负值,是真实数据;只在字段缺失/非数值时才拒绝。
        var set = Parse(SampleJson(
            totalTech: """{"itemName":"零利润品","profit":0}""",
            hourlyTech: """{"itemName":"每小时亏本品","profit":-120.5}"""));
        Assert.Equal(0, Assert.Single(set.TotalProfitRecommendations,
            r => r.Facility == FacilityKey.TechCenter).Profit);
        Assert.Equal(-120.5, Assert.Single(set.HourlyProfitRecommendations,
            r => r.Facility == FacilityKey.TechCenter).Profit);
    }

    /// <summary>可编程传输桩:验证抓取管线的超时/取消/握手行为,不出网络。</summary>
    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }

    private static ProfitAdvisorBrick Brick(
        TimeSpan timeout, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(timeout, () => new StubHandler(respond));

    private static async Task<HttpResponseMessage> HangForever(HttpRequestMessage _, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    }

    [Fact]
    public async Task Internal_deadline_surfaces_as_timeout_not_cancellation()
    {
        // 内部限时到点若原样抛取消异常,调用方会误判为应用退出而静默吞掉,
        // 巡检循环被永久杀死——必须翻译成 TimeoutException 走正常告警重试路径。
        var brick = Brick(TimeSpan.FromMilliseconds(100), HangForever);
        await Assert.ThrowsAsync<TimeoutException>(
            () => brick.FetchRecommendationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_stays_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var brick = Brick(TimeSpan.FromSeconds(30), HangForever);
        var task = brick.FetchRecommendationsAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Full_handshake_success_returns_recommendations()
    {
        var paths = new List<string>();
        var brick = Brick(TimeSpan.FromSeconds(30), (req, _) =>
        {
            paths.Add(req.RequestUri!.AbsolutePath);
            if (req.Method == HttpMethod.Post)
                Assert.Contains("XMLHttpRequest", req.Headers.GetValues("X-Requested-With"));
            string body = req.RequestUri!.AbsolutePath switch
            {
                "/" => "<html>overview</html>",
                "/getMenu" => """{"code":1,"menu":[]}""",
                "/checkUAStatus" => """{"code":1,"msg":"success"}""",
                "/getOVData" => SampleJson(),
                _ => throw new InvalidOperationException("unexpected " + req.RequestUri),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        });
        var set = await brick.FetchRecommendationsAsync(CancellationToken.None);
        Assert.Equal(4, set.TotalProfitRecommendations.Count);
        Assert.Equal(4, set.HourlyProfitRecommendations.Count);
        Assert.Equal(new[] { "/", "/getMenu", "/checkUAStatus", "/getOVData" }, paths);
    }

    [Fact]
    public async Task Non_json_api_response_reports_endpoint_context()
    {
        // 风控挑战页等非 JSON 响应必须带接口名报错,不能抛裸 JsonException。
        var brick = Brick(TimeSpan.FromSeconds(30), (req, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>challenge</html>"),
            }));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => brick.FetchRecommendationsAsync(CancellationToken.None));
        Assert.Contains("getMenu", ex.Message);
    }
}
