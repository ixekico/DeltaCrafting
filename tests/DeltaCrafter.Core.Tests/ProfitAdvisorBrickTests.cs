using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using System.Net;
using System.Text.Json;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class ProfitAdvisorBrickTests
{
    /// <summary>按 kkrb.net getOVData 实测结构裁剪的样例(保留解析所需字段)。</summary>
    private static string SampleJson(
        string tech = """{"itemName":"OLIGHT WARRIOR 3S战术手电","itemForge":[{"requiredLevel":1,"hourlyProfit":3522.57},{"requiredLevel":2,"hourlyProfit":5283.86}],"profit":23777.36}""",
        string workbench = """{"itemName":"4.6x30mm AP SX","itemForge":[{"requiredLevel":3,"hourlyProfit":33217.63}],"profit":265741}""",
        string pharmacy = """{"itemName":"精密护甲维修包","itemForge":[{"requiredLevel":2,"hourlyProfit":11104}],"profit":88831}""",
        string? armory = """{"itemName":"重型突击背心","itemForge":[{"requiredLevel":3,"hourlyProfit":30283}],"profit":242262}""")
    {
        string armoryPart = armory is null ? "" : @",""armory"":" + armory;
        return @"{""code"":1,""data"":{""spData"":{""tech"":" + tech
            + @",""workbench"":" + workbench
            + @",""pharmacy"":" + pharmacy
            + armoryPart + "}}}";
    }

    private static IReadOnlyList<ProfitRecommendation> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ProfitAdvisorBrick.ParseOverviewData(doc.RootElement);
    }

    [Fact]
    public void Parses_all_four_facilities_with_both_profit_metrics()
    {
        var recs = Parse(SampleJson());
        Assert.Equal(4, recs.Count);

        var tech = Assert.Single(recs, r => r.Facility == FacilityKey.TechCenter);
        Assert.Equal("OLIGHT WARRIOR 3S战术手电", tech.ItemName);
        // 小时利润取各设施等级中的最高档(与网站展示口径一致),不是首个档位。
        Assert.Equal(5283.86, tech.HourlyProfit, precision: 2);
        Assert.Equal(23777.36, tech.TotalProfit, precision: 2);

        Assert.Equal("4.6x30mm AP SX",
            Assert.Single(recs, r => r.Facility == FacilityKey.Workbench).ItemName);
        Assert.Equal("精密护甲维修包",
            Assert.Single(recs, r => r.Facility == FacilityKey.PharmacyLab).ItemName);
        Assert.Equal("重型突击背心",
            Assert.Single(recs, r => r.Facility == FacilityKey.ArmorStation).ItemName);
    }

    [Fact]
    public void Missing_facility_throws_with_display_name()
    {
        // 四设施缺一即拒绝:残缺推荐不允许改用户计划。
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(SampleJson(armory: null)));
        Assert.Contains("防具台", ex.Message);
    }

    [Fact]
    public void Missing_spData_throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            Parse("""{"code":1,"data":{"bdData":{}}}"""));

    [Fact]
    public void Empty_item_name_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(SampleJson(
            tech: """{"itemName":"  ","itemForge":[{"hourlyProfit":1}],"profit":1}""")));
        Assert.Contains("技术中心", ex.Message);
    }

    [Theory]
    [InlineData("""{"itemName":"物品A","profit":100}""")]                        // 缺 itemForge → 无小时利润
    [InlineData("""{"itemName":"物品A","itemForge":[{"hourlyProfit":100}]}""")]  // 缺 profit → 无总利润
    [InlineData("""{"itemName":"物品A","itemForge":[{"hourlyProfit":"N/A"}],"profit":"暂无"}""")] // 非数值同缺失
    public void Missing_profit_field_throws(string tech)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(SampleJson(tech: tech)));
        Assert.Contains("利润", ex.Message);
    }

    [Fact]
    public void Zero_or_negative_profit_is_accepted_as_real_market_data()
    {
        // 行情低谷利润可为 0 或负值,是真实数据;只在字段缺失/非数值时才拒绝。
        var recs = Parse(SampleJson(
            tech: """{"itemName":"亏本品","itemForge":[{"hourlyProfit":-120.5}],"profit":0}"""));
        var tech = Assert.Single(recs, r => r.Facility == FacilityKey.TechCenter);
        Assert.Equal(-120.5, tech.HourlyProfit);
        Assert.Equal(0, tech.TotalProfit);
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
        var recs = await brick.FetchRecommendationsAsync(CancellationToken.None);
        Assert.Equal(4, recs.Count);
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
