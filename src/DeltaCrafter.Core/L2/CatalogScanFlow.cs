using System.Text.RegularExpressions;
using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>
/// 配方目录扫描:进入设施生产界面,滚动读完整个「已解锁」列表,收集配方名。
/// 名称以游戏内 OCR 为准(与玩家解锁完全一致),网上抄的清单反而会有出入。
/// 终止条件 = 滚动后列表内容不再变化(到底);带安全上限防意外死循环。
/// </summary>
public sealed partial class CatalogScanFlow
{
    private const int MaxScrollPages = 30;
    private static readonly string[] NoiseWords = ["已解锁", "未解锁", "价格", "品质", "全部类型", "返回"];

    private readonly ScreenProbe _probe;
    private readonly StepRunner _runner;
    private readonly InputBrick _input;
    private readonly ILogger _log;

    public CatalogScanFlow(ScreenProbe probe, StepRunner runner, InputBrick input, ILogger log)
    {
        _probe = probe;
        _runner = runner;
        _input = input;
        _log = log.ForContext<CatalogScanFlow>();
    }

    /// <summary>扫描单个设施的配方列表并返回名称集合。调用前设施槽位须为 空闲/制造中
    /// (可领取槽位点击会触发领取,由调用方先行排除)。结束后回到特勤处总览。</summary>
    public async Task<IReadOnlyList<string>> ScanFacilityAsync(nint hwnd, FacilityKey key, CancellationToken ct)
    {
        string facility = FacilityKeys.DisplayName(key);
        var prodSpec = _probe.Screen(AnchorKeys.Production);
        var listArea = prodSpec.Roi(AnchorKeys.RoiListArea);

        await _runner.RunAsync(hwnd, new Step(
            $"打开{facility}生产界面(扫描)",
            () => _probe.ClickPoint(hwnd, AnchorKeys.SpecOpsHome, AnchorKeys.FacilitySlot(key)),
            () => _probe.IsOnAsync(hwnd, AnchorKeys.Production),
            TimeSpan.FromSeconds(12)), ct);

        var names = new List<string>();
        var seen = new HashSet<string>();
        string previousView = "";
        for (int page = 0; page < MaxScrollPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var lines = await _probe.ReadAreaLinesAsync(hwnd, listArea);
            foreach (var line in lines)
            {
                string name = CleanName(line.Text);
                if (name.Length < 2) continue;
                if (seen.Add(TextMatch.Canonical(name))) names.Add(name);
            }

            string currentView = ScreenProbe.Normalize(string.Join("|", lines.Select(l => l.Text)));
            if (currentView.Length > 0 && currentView == previousView) break; // 到底
            previousView = currentView;
            _probe.ScrollRoi(hwnd, listArea, -5);
            await Task.Delay(700, ct);
        }

        await _runner.RunAsync(hwnd, new Step(
            "返回特勤处总览",
            () => _input.PressEscape(),
            () => _probe.IsOnAsync(hwnd, AnchorKeys.SpecOpsHome),
            TimeSpan.FromSeconds(8)), ct);

        _log.Information("{Facility} 扫描到 {Count} 个配方。", facility, names.Count);
        return names;
    }

    /// <summary>清洗 OCR 行:去掉数量角标、界面噪声词与倒计时,留下像配方名的行。</summary>
    private static string CleanName(string raw)
    {
        string text = raw.Trim();
        if (NoiseWords.Any(w => text.Contains(w, StringComparison.Ordinal))) return "";
        if (CountdownParser.TryParse(text, out _)) return "";
        if (PureNumberPattern().IsMatch(ScreenProbe.Normalize(text))) return ""; // 数量角标(如 120/×10)
        return Regex.Replace(text, @"\s+", " ");
    }

    [GeneratedRegex(@"^[0-9xX×*/]+$")]
    private static partial Regex PureNumberPattern();
}
