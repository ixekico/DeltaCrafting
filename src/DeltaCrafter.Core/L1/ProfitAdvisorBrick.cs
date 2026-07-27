using System.Net;
using System.Text.Json;
using DeltaCrafter.Core.L0;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 制造利润推荐能力:从 kkrb.net「热点内容一图流」页的数据接口读取
/// 「特勤处制作产物推荐」,得到四个设施各自的推荐物品与两种利润口径。
/// 约束:任何一步失败(网络、反爬拦截、字段缺失)都带上下文抛出,由调用方
/// 决定呈现方式;本类不做静默降级,也不返回不完整的推荐列表。
/// 内部限时到点抛 TimeoutException 而非取消异常——调用方以取消语义判断
/// 「是否应用退出」,超时必须能走正常的告警与重试路径。
/// </summary>
public sealed class ProfitAdvisorBrick
{
    private const string BaseUrl = "https://www.kkrb.net";
    private const string OverviewPage = BaseUrl + "/?viewpage=view%2Foverview";

    // 站点服务端校验 UA 与 XHR 头(checkUAStatus),非浏览器形态的请求一律拒绝
    // (返回 code=-101)。此 UA 与实测放行的桌面 Chrome 一致,不含个人信息。
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/126.0.0.0 Safari/537.36";

    /// <summary>spData 键(网站命名)→ 本项目设施键。四个键必须全部出现。</summary>
    private static readonly (string Key, FacilityKey Facility)[] FacilityMap =
    [
        ("tech", FacilityKey.TechCenter),
        ("workbench", FacilityKey.Workbench),
        ("pharmacy", FacilityKey.PharmacyLab),
        ("armory", FacilityKey.ArmorStation),
    ];

    private readonly TimeSpan _timeout;
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    public ProfitAdvisorBrick() : this(TimeSpan.FromSeconds(60), handlerFactory: null) { }

    /// <summary>测试注入口:缩短限时、替换传输层。生产代码只用默认构造。</summary>
    internal ProfitAdvisorBrick(TimeSpan timeout, Func<HttpMessageHandler>? handlerFactory)
    {
        _timeout = timeout;
        _handlerFactory = handlerFactory;
    }

    /// <summary>
    /// 抓取当前推荐。会话按实测放行序列建立:首页(取 PHPSESSID)→ getMenu
    /// (下发 csrf_token)→ checkUAStatus(标记会话已验证)→ getOVData(取数据)。
    /// 每次抓取用全新会话,避免服务端会话过期造成难排查的间歇失败。
    /// </summary>
    public async Task<IReadOnlyList<ProfitRecommendation>> FetchRecommendationsAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            return await FetchCoreAsync(cts.Token);
        }
        // 内部限时与外部取消抛的都是取消异常,类型上不可区分;仅当调用方令牌
        // 未取消(即超时是本类自己触发的)才翻译成超时异常。
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"获取利润推荐超时({(int)_timeout.TotalSeconds} 秒),请检查网络后重试。");
        }
    }

    private async Task<IReadOnlyList<ProfitRecommendation>> FetchCoreAsync(CancellationToken ct)
    {
        var cookies = new CookieContainer();
        using var handler = _handlerFactory?.Invoke()
            ?? new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

        using (var page = await http.GetAsync(OverviewPage, ct))
        {
            if (!page.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"访问推荐数据站点失败:HTTP {(int)page.StatusCode}。请检查网络后重试。");
        }

        (await PostApiAsync(http, cookies, "getMenu", "globalData=false", ct)).Dispose();
        (await PostApiAsync(http, cookies, "checkUAStatus", body: null, ct)).Dispose();
        using var data = await PostApiAsync(http, cookies, "getOVData", "globalData=false", ct);
        return ParseOverviewData(data.RootElement);
    }

    /// <summary>POST 站点接口并校验业务码。非 JSON 响应(风控挑战页)与 code≠1
    /// 都带接口名抛出;返回的 JsonDocument 由调用方负责释放。</summary>
    private static async Task<JsonDocument> PostApiAsync(
        HttpClient http, CookieContainer cookies, string endpoint, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{endpoint}");
        // 服务端以 XHR 头区分页面脚本请求;csrf_token 由 getMenu 下发,后续请求须回传同值头。
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("Referer", OverviewPage);
        request.Headers.TryAddWithoutValidation("Origin", BaseUrl);
        string? csrf = cookies.GetCookies(new Uri(BaseUrl))["csrf_token"]?.Value;
        if (csrf is not null)
            request.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8,
                "application/x-www-form-urlencoded");

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"推荐数据接口 {endpoint} 失败:HTTP {(int)response.StatusCode}。");

        string text = await response.Content.ReadAsStringAsync(ct);
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"推荐数据接口 {endpoint} 返回了无法解析的内容,可能被站点风控拦截,请稍后重试。");
        }
        try
        {
            if (!json.RootElement.TryGetProperty("code", out var codeProp)
                || codeProp.ValueKind != JsonValueKind.Number)
                throw new InvalidOperationException(
                    $"推荐数据接口 {endpoint} 响应缺少业务码,站点接口可能已变化。");
            int code = codeProp.GetInt32();
            if (code != 1)
            {
                string msg = json.RootElement.TryGetProperty("msg", out var m)
                    ? m.GetString() ?? "" : "";
                throw new InvalidOperationException(
                    $"推荐数据接口 {endpoint} 返回 code={code}({msg}),可能被站点风控拦截,请稍后重试。");
            }
            return json;
        }
        catch
        {
            json.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 解析 getOVData 返回:data.spData 下每设施一个对象,itemName 为推荐物品,
    /// itemForge[].hourlyProfit 为各设施等级的小时利润(取最高,与网站展示一致),
    /// profit 为单次制造总利润。四个设施缺一即抛——不返回残缺推荐去改用户计划。
    /// 利润只验「字段存在且为数值」,0 或负值是真实行情,照常采信。
    /// </summary>
    internal static IReadOnlyList<ProfitRecommendation> ParseOverviewData(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("spData", out var sp)
            || sp.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("推荐数据缺少 spData 字段,站点数据结构可能已变化。");

        var result = new List<ProfitRecommendation>(FacilityMap.Length);
        foreach (var (key, facility) in FacilityMap)
        {
            if (!sp.TryGetProperty(key, out var entry))
                throw new InvalidOperationException(
                    $"推荐数据缺少设施「{FacilityKeys.DisplayName(facility)}」({key}),拒绝按残缺数据改计划。");
            string itemName = entry.TryGetProperty("itemName", out var n)
                ? n.GetString()?.Trim() ?? "" : "";
            if (itemName.Length == 0)
                throw new InvalidOperationException(
                    $"推荐数据中「{FacilityKeys.DisplayName(facility)}」缺少物品名,站点数据结构可能已变化。");

            double? hourly = null;
            if (entry.TryGetProperty("itemForge", out var forge)
                && forge.ValueKind == JsonValueKind.Array)
                foreach (var level in forge.EnumerateArray())
                    if (level.TryGetProperty("hourlyProfit", out var hp)
                        && hp.ValueKind == JsonValueKind.Number)
                        hourly = hourly is null ? hp.GetDouble() : Math.Max(hourly.Value, hp.GetDouble());
            double? total = entry.TryGetProperty("profit", out var p)
                && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
            if (hourly is null || total is null)
                throw new InvalidOperationException(
                    $"推荐数据中「{FacilityKeys.DisplayName(facility)}」缺少数值利润字段,站点数据结构可能已变化。");

            result.Add(new ProfitRecommendation(facility, itemName, hourly.Value, total.Value));
        }
        return result;
    }
}
