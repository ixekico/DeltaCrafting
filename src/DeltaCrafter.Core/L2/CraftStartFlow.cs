using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>开工结果。Blocked(材料不足/需兑换)是显式业务结果,与步骤失败(异常)分开。</summary>
public sealed record CraftStartResult(bool Started, TimeSpan? Remaining, string? BlockReason);

/// <summary>
/// 「选择配方并开始生产」流程。生产界面右下角为同位置三态按钮,其文字即状态:
/// 「一键补齐」=缺料 →(可选)自动购买;「生产」=可开工;「中止」=已在生产(异常)。
/// 开工成功的判据 = 按钮变为「中止」;随后读「剩余时间」作为唯一调度依据。
/// 配方列表因玩家置顶而顺序各异,按名称 OCR 定位(两级匹配:精确包含 → 子串编辑距离,
/// 见 PickLine);滚到列表不再变化为止,并设安全页数上限。
/// </summary>
public sealed class CraftStartFlow
{
    private const int MaxScrollPages = 20; // 安全上限;正常在此之前就会因「见底」停止
    private readonly ScreenProbe _probe;
    private readonly StepRunner _runner;
    private readonly InputBrick _input;
    private readonly ILogger _log;

    public CraftStartFlow(ScreenProbe probe, StepRunner runner, InputBrick input, ILogger log)
    {
        _probe = probe;
        _runner = runner;
        _input = input;
        _log = log.ForContext<CraftStartFlow>();
    }

    /// <param name="searchName">运行期匹配名(通常为 OCR 原文),用于列表定位与选中校验。</param>
    /// <param name="displayName">显示名,用于日志/报告/失败信息。</param>
    public async Task<CraftStartResult> StartAsync(nint hwnd, FacilityKey key, string searchName,
        string displayName, bool autoReplenish, CancellationToken ct)
    {
        string facility = FacilityKeys.DisplayName(key);
        var prodSpec = _probe.Screen(AnchorKeys.Production);
        var kw = _probe.Anchors.Keywords;

        await _runner.RunAsync(hwnd, new Step(
            $"打开{facility}生产界面",
            () => _probe.ClickPoint(hwnd, AnchorKeys.SpecOpsHome, AnchorKeys.FacilitySlot(key)),
            () => _probe.IsOnAsync(hwnd, AnchorKeys.Production),
            TimeSpan.FromSeconds(12)), ct);

        await FindAndSelectItemAsync(hwnd, facility, searchName, displayName, prodSpec, ct);

        string label = await ReadActionLabelAsync(hwnd, prodSpec, ct);
        if (LabelHits(label, kw.ButtonAbort))
            throw new StepFailedException($"{facility}开工前检查",
                "生产界面显示「中止」,槽位并非空闲——与总览观察不一致,中止本轮待人工确认。");

        if (LabelHits(label, kw.ButtonReplenish))
        {
            if (!autoReplenish)
            {
                await EscBackToHomeAsync(hwnd, ct);
                return new CraftStartResult(false, null, "材料不足(自动补齐已关闭)");
            }
            await _runner.RunAsync(hwnd, new Step(
                "打开一键补齐清单",
                () => _probe.ClickPoint(hwnd, AnchorKeys.Production, AnchorKeys.PointActionButton),
                () => _probe.IsOnAsync(hwnd, AnchorKeys.ReplenishPopup),
                TimeSpan.FromSeconds(10)), ct);
            _log.Information("{Facility}「{Item}」缺料,自动购买(金额随交易行波动,见游戏账单)。", facility, displayName);
            await _runner.RunAsync(hwnd, new Step(
                "确认购买缺料",
                () => _probe.ClickPoint(hwnd, AnchorKeys.ReplenishPopup, AnchorKeys.PointBuy),
                () => _probe.IsOnAsync(hwnd, AnchorKeys.Production),
                TimeSpan.FromSeconds(15)), ct);
            await Task.Delay(800, ct);

            label = await ReadActionLabelAsync(hwnd, prodSpec, ct);
            if (!LabelHits(label, kw.ButtonProduce))
            {
                // 兑换类材料交易行买不到:显式受阻,绝不循环烧钱重试。
                await EscBackToHomeAsync(hwnd, ct);
                return new CraftStartResult(false, null, "一键补齐后材料仍不足(可能含需兑换材料)");
            }
        }

        if (!LabelHits(label, kw.ButtonProduce))
        {
            var (png, dumpText) = await _probe.DumpAsync(hwnd, "fail-识别操作按钮");
            throw new StepFailedException($"{facility}识别操作按钮",
                $"按钮文字「{label}」无法归类为 生产/一键补齐/中止。诊断截图:{png}", png, dumpText);
        }

        await _runner.RunAsync(hwnd, new Step(
            $"开始生产「{displayName}」",
            () => _probe.ClickPoint(hwnd, AnchorKeys.Production, AnchorKeys.PointActionButton),
            async () => LabelHits(await ReadLabelOnceAsync(hwnd, prodSpec), kw.ButtonAbort),
            TimeSpan.FromSeconds(15)), ct);

        var remaining = await ReadRemainingTimeAsync(hwnd, facility, prodSpec, ct);
        await EscBackToHomeAsync(hwnd, ct);
        _log.Information("{Facility}「{Item}」已开工,剩余 {Remaining}。", facility, displayName, remaining);
        return new CraftStartResult(true, remaining, null);
    }

    private async Task FindAndSelectItemAsync(nint hwnd, string facility, string searchName,
        string displayName, ScreenSpec prodSpec, CancellationToken ct)
    {
        var listArea = prodSpec.Roi(AnchorKeys.RoiListArea);
        OcrLine? line = null;
        string previousView = "";
        bool reachedBottom = false;
        // 滚动直到找到 / 列表内容不再变化(到底或滚动无效) / 安全上限。每页只读一次。
        for (int page = 0; page < MaxScrollPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var lines = await _probe.ReadAreaLinesAsync(hwnd, listArea);
            line = PickLine(lines, searchName, page);
            if (line is not null) break;

            string currentView = ScreenProbe.Normalize(string.Concat(lines.Select(l => l.Text)));
            if (currentView.Length > 0 && currentView == previousView)
            {
                reachedBottom = true;
                break;
            }
            previousView = currentView;
            _probe.ScrollRoi(hwnd, listArea, -5);
            await Task.Delay(700, ct);
        }
        if (line is null)
        {
            var (png, dumpText) = await _probe.DumpAsync(hwnd, "fail-查找物品");
            throw new StepFailedException($"查找物品「{displayName}」",
                (reachedBottom
                    ? $"{facility}列表已滚到底仍未找到(匹配键「{searchName}」)。可用「扫描配方目录」重建候选。"
                    : $"{facility}列表滚动 {MaxScrollPages} 页未找到(或滚轮未生效)。") +
                $"诊断截图:{png}", png, dumpText);
        }

        _probe.ClickFramePoint(hwnd, line.CenterX, line.CenterY);
        long deadline = Environment.TickCount64 + 8000;
        // 注意:下方详情标题校验保持「规范形包含」强匹配——列表模糊选错行时,
        // 校验必然不过并大声失败,绝不会静默生产错误物品。
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(800, ct);
            string title = await _probe.ReadRoiAsync(hwnd, prodSpec.Roi(AnchorKeys.RoiDetailTitle));
            if (TextMatch.LineContains(title, searchName)) return;
        }
        var (png2, dump2) = await _probe.DumpAsync(hwnd, "fail-选中物品");
        throw new StepFailedException($"选中物品「{displayName}」",
            $"点击后详情标题未出现该物品。诊断截图:{png2}", png2, dump2);
    }

    /// <summary>页内选行,两级匹配:① 规范形包含(精确);② 子串编辑距离 ≤1 且严格惟一最优
    /// (容忍单点误读——实测「7.62」被读成「7毛2」或吞掉行首「7.」,包含匹配直接漏过,
    /// 导致目标明明在第一屏却一路滚到底)。阈值取 1 保证不会错认同族名(AP/PS 差 2);
    /// 即便极端情况下选错行,后续详情标题强校验也会大声失败,不会静默错造。</summary>
    private OcrLine? PickLine(IReadOnlyList<OcrLine> lines, string searchName, int page)
    {
        var exact = lines.FirstOrDefault(l => TextMatch.LineContains(l.Text, searchName));
        if (exact is not null) return exact;

        OcrLine? best = null;
        int bestD = int.MaxValue, secondD = int.MaxValue;
        foreach (var l in lines)
        {
            int d = TextMatch.SubstringDistance(l.Text, searchName);
            if (d < bestD) { secondD = bestD; bestD = d; best = l; }
            else if (d < secondD) { secondD = d; }
        }
        _log.Debug("查找第{Page}页:{Count} 行,最优距离 {Best}(次优 {Second}):{Text}",
            page + 1, lines.Count, bestD, secondD,
            best is null ? "" : ScreenProbe.Normalize(best.Text));
        return bestD <= 1 && bestD < secondD ? best : null;
    }

    private async Task<string> ReadActionLabelAsync(nint hwnd, ScreenSpec prodSpec, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            string label = await ReadLabelOnceAsync(hwnd, prodSpec);
            if (label.Length > 0) return label;
            await Task.Delay(1200, ct);
        }
        return "";
    }

    private async Task<string> ReadLabelOnceAsync(nint hwnd, ScreenSpec prodSpec) =>
        ScreenProbe.Normalize(await _probe.ReadRoiAsync(hwnd, prodSpec.Roi(AnchorKeys.RoiActionButton)));

    private static bool LabelHits(string normalizedLabel, IEnumerable<string> keywords) =>
        keywords.Any(k => k.Length > 0 &&
            normalizedLabel.Contains(ScreenProbe.Normalize(k), StringComparison.Ordinal));

    private async Task<TimeSpan> ReadRemainingTimeAsync(nint hwnd, string facility,
        ScreenSpec prodSpec, CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + 8000;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string text = await _probe.ReadRoiAsync(hwnd, prodSpec.Roi(AnchorKeys.RoiRemainingTime));
            if (CountdownParser.TryParse(text, out var remaining)) return remaining;
            await Task.Delay(1000, ct);
        }
        var (png, dumpText) = await _probe.DumpAsync(hwnd, "fail-读取剩余时间");
        throw new StepFailedException($"读取{facility}剩余时间",
            $"开工后读不到剩余时间,无法调度下一轮。诊断截图:{png}", png, dumpText);
    }

    private Task EscBackToHomeAsync(nint hwnd, CancellationToken ct) =>
        _runner.RunAsync(hwnd, new Step(
            "返回特勤处总览",
            () => _input.PressEscape(),
            () => _probe.IsOnAsync(hwnd, AnchorKeys.SpecOpsHome),
            TimeSpan.FromSeconds(8)), ct);
}
