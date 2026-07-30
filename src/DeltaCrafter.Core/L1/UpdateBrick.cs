using DeltaCrafter.Core.L0;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 更新能力(GitHub Releases):检查最新版本、下载安装包并校验 SHA-256、启动安装程序。
/// 约束:任何一步异常都原样抛出(网络不通、资产缺失、校验不符),由调用方决定呈现方式;
/// 本类不做静默降级——校验不过的安装包会被删除,绝不启动。
/// </summary>
public sealed class UpdateBrick
{
    // 发布仓库固定为本项目官方仓库;/releases/latest 只返回正式版(草稿与预发布不计)。
    private const string LatestReleaseApi =
        "https://api.github.com/repos/ixekico/DeltaCrafting/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>当前程序版本(取装配体版本前三段,与发布 tag 的 x.y.z 对齐)。</summary>
    public Version CurrentVersion { get; } =
        Normalize(typeof(UpdateBrick).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>查询最新 Release 并与当前版本比较。新版本缺 Setup 或校验资产时抛出——
    /// 发布不完整就明确失败,不引导用户装一个无法校验的包。</summary>
    public async Task<UpdateInfo> CheckLatestAsync(CancellationToken ct)
    {
        // 查询是小请求,30s 足够;客户端本身不限时(下载另设上限),避免大包被全局超时截断。
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        ct = cts.Token;
        using var response = await Http.GetAsync(LatestReleaseApi, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"查询 GitHub 最新版本失败:HTTP {(int)response.StatusCode}。请稍后重试或检查网络。");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseRelease(json.RootElement, CurrentVersion);
    }

    /// <summary>把 GitHub Release JSON 转为更新结论。资产名必须与 tag 精确对应，
    /// 防止同一 Release 混入旧安装包时把安装包和校验文件错误配对。</summary>
    internal static UpdateInfo ParseRelease(JsonElement release, Version current)
    {
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        var latest = TryParseVersionTag(tag)
            ?? throw new InvalidOperationException($"无法解析发布标签「{tag}」,请到项目主页手动更新。");

        bool newer = latest > Normalize(current);
        string releaseNotes = ReleaseNotesFormatter.ToPlainText(
            release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
        string versionToken = tag.Trim();
        if (versionToken.StartsWith('v') || versionToken.StartsWith('V'))
            versionToken = versionToken[1..];
        string expectedSetupName = $"DeltaCrafter-Setup-{versionToken}.exe";
        string expectedChecksumName = expectedSetupName + ".sha256";
        string? setupName = null, setupUrl = null, checksumUrl = null;
        long setupBytes = 0;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (name.Equals(expectedSetupName, StringComparison.OrdinalIgnoreCase))
            {
                (setupName, setupUrl) = (name, url);
                setupBytes = asset.GetProperty("size").GetInt64();
            }
            else if (name.Equals(expectedChecksumName, StringComparison.OrdinalIgnoreCase))
                checksumUrl = url;
        }
        if (newer && (setupUrl is null || checksumUrl is null))
            throw new InvalidOperationException(
                $"发布 {tag} 缺少与版本精确对应的安装包或校验文件,无法安全更新。请到项目 Releases 页人工确认。");
        if (newer)
        {
            EnsureHttpsDownloadUrl(setupUrl!);
            EnsureHttpsDownloadUrl(checksumUrl!);
        }
        return new UpdateInfo(Normalize(current), latest, tag, newer, releaseNotes,
            setupName, setupUrl, checksumUrl, setupBytes);
    }

    /// <summary>下载安装包到 targetDir 并按发布的 .sha256 校验。校验不符即删除文件并抛出。
    /// progress 回报 0..1(按已知资产大小);返回通过校验的安装包完整路径。</summary>
    public async Task<string> DownloadVerifiedSetupAsync(
        UpdateInfo info, string targetDir, IProgress<double>? progress, CancellationToken ct)
    {
        if (!info.IsNewer || info.SetupUrl is null || info.ChecksumUrl is null || info.SetupName is null)
            throw new InvalidOperationException("当前没有可下载的新版本安装包。");

        // 下载上限 15 分钟:防连接僵死无限挂起;超限按失败处理,用户可重试。
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(15));
        ct = cts.Token;

        // 旧的更新残留一律清掉:上次下到一半/校验失败的文件不允许被误用。
        Directory.CreateDirectory(targetDir);
        foreach (var stale in Directory.EnumerateFiles(targetDir))
            File.Delete(stale);

        string expected = ParseChecksumText(await Http.GetStringAsync(info.ChecksumUrl, ct))
            ?? throw new InvalidOperationException("校验文件内容不是有效的 SHA-256,拒绝继续下载。");

        string setupPath = Path.Combine(targetDir, info.SetupName);
        using (var response = await Http.GetAsync(info.SetupUrl,
                   HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(setupPath);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
                if (info.SetupBytes > 0)
                    progress?.Report(Math.Min(1.0, (double)total / info.SetupBytes));
            }
        }

        string actual;
        await using (var stream = File.OpenRead(setupPath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(setupPath);
            throw new InvalidOperationException(
                $"安装包 SHA-256 校验不符(期望 {expected[..12]}…,实际 {actual[..12]}…),已删除下载文件。请重试。");
        }
        return setupPath;
    }

    /// <summary>启动安装程序:静默覆盖安装,装完自动重启助手(/AutoLaunch=1,见 DeltaCrafter.iss)。
    /// 本进程已提权,安装程序沿用同一令牌,不再弹 UAC。调用方随后应立即退出应用。</summary>
    public void LaunchInstaller(string setupPath)
    {
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("已校验的更新安装包不存在,拒绝启动安装。", setupPath);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/SILENT /NORESTART /AutoLaunch=1",
            UseShellExecute = true,
        });
        if (process is null)
            throw new InvalidOperationException("系统未能启动更新安装程序。");
    }

    /// <summary>解析发布标签为严格三段版本:接受「v1.2.3」「1.2.3」及预发布后缀
    /// 「v1.2.3-beta」(后缀忽略,/releases/latest 本就不含预发布)。</summary>
    public static Version? TryParseVersionTag(string tag)
    {
        string core = tag.Trim();
        if (core.StartsWith('v') || core.StartsWith('V'))
            core = core[1..];
        int dash = core.IndexOf('-');
        if (dash >= 0) core = core[..dash];
        string[] parts = core.Split('.');
        return parts.Length == 3
               && parts.All(p => int.TryParse(p, out int value) && value >= 0)
            ? new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]))
            : null;
    }

    /// <summary>解析 .sha256 文件文本(格式「小写哈希␣␣文件名」,见 build-installer.ps1)。
    /// 取首个空白前的记号,须为 64 位十六进制;否则返回 null。</summary>
    public static string? ParseChecksumText(string text)
    {
        string token = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        return token.Length == 64 && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }

    /// <summary>版本统一为三段(装配体版本带第四段 0,发布 tag 只有三段,不归一会比错)。</summary>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub API 要求 User-Agent;版本随当前程序,便于服务端区分。
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DeltaCrafter", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static void EnsureHttpsDownloadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Release 返回了非 HTTPS 下载地址,拒绝更新。");
    }
}
