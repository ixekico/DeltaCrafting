using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>单个设施槽位的一次观察。ItemName 为槽位上显示的物品名(空闲时为空)。</summary>
public sealed record FacilityObservation(FacilityPhase Phase, TimeSpan? Remaining, string ItemName, string RawText);

/// <summary>
/// 特勤处总览页的观察与领取。单槽位判定规则:
/// 含「空闲中」→ 空闲;能解析出倒计时 → 制造中;否则有物品名 → 可领取。
/// 整体观察采用多遍共识(见 ObserveAllAsync):同槽位两票相位一致才采信。
/// 领取路径实测可能有两种(点槽位直达结算 / 先进生产页再点「领取」),两分支都显式校验。
/// </summary>
public sealed class CollectFlow
{
    private readonly ScreenProbe _probe;
    private readonly StepRunner _runner;
    private readonly InputBrick _input;
    private readonly IAppWindowGuard _windowGuard;
    private readonly ILogger _log;

    public CollectFlow(ScreenProbe probe, StepRunner runner, InputBrick input,
        IAppWindowGuard windowGuard, ILogger log)
    {
        _probe = probe;
        _runner = runner;
        _input = input;
        _windowGuard = windowGuard;
        _log = log.ForContext<CollectFlow>();
    }

    /// <summary>连续多遍观察全部设施,同一槽位在两遍读取中判定一致才采信。
    /// 为什么不是"一帧定案":① 进入特勤处的滑入动画期间,槽位区域会读到邻卡串染文本,
    /// 单帧曾把制造中误判为空闲(2026-07-26 实机日志);② 「制造完成」态的物品标题对比度低,
    /// 2x 最近邻放大后 OCR 稳定读空,而 1x 原始尺寸可读(同日失败转储证实)——因此各遍交替
    /// 2x/1x 倍率,读空(Unknown)不投票也不清掉已有一票。制造中槽位还要求两票的倒计时按
    /// 实际流逝单调递减(±容差),单次误读的数字进不了调度。上限遍数内仍无一致判定 → 报错。</summary>
    public async Task<Dictionary<FacilityKey, FacilityObservation>> ObserveAllAsync(nint hwnd, CancellationToken ct)
    {
        var spec = _probe.Screen(AnchorKeys.SpecOpsHome);
        var rois = FacilityKeys.All.Select(k => spec.Roi(AnchorKeys.FacilitySlot(k))).ToArray();
        const int MaxPasses = 5;
        const int PassDelayMs = 700;

        var agreed = new Dictionary<FacilityKey, (FacilityObservation Obs, long CapMs)>();
        var claims = new Dictionary<FacilityKey, (FacilityObservation Obs, long CapMs)>();

        for (int pass = 1; pass <= MaxPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();
            // 执行中若助手窗口被手动唤起,会盖住游戏污染屏幕拷贝(实测发生过):
            // 每遍观察前重新压下去;窗口已最小化时此调用为无操作。
            _windowGuard.MinimizeForRun();
            double upscale = pass % 2 == 1 ? 2.0 : 1.0;
            long capMs = Environment.TickCount64; // 截帧时刻:剩余时间读数对应的基准点
            string[] texts = await _probe.ReadRoisAsync(hwnd, rois, upscale);
            for (int i = 0; i < FacilityKeys.All.Length; i++)
            {
                var key = FacilityKeys.All[i];
                if (agreed.ContainsKey(key)) continue;
                var obs = Classify(texts[i]);
                _log.Debug("{Facility} 槽位(第{Pass}遍 {Scale:0.#}x):{Phase} {Raw}",
                    FacilityKeys.DisplayName(key), pass, upscale, obs.Phase, ScreenProbe.Normalize(texts[i]));
                if (obs.Phase == FacilityPhase.Unknown) continue; // 读空/读数被误读:不投票,已有一票保留

                if (claims.TryGetValue(key, out var prev))
                {
                    if (prev.Obs.Phase == obs.Phase && CountdownConsistent(prev.Obs, prev.CapMs, obs, capMs))
                    {
                        agreed[key] = (Merge(prev.Obs, obs), capMs);
                    }
                    else if (prev.Obs.Phase == FacilityPhase.Crafting && obs.Phase == FacilityPhase.ReadyToCollect)
                    {
                        // 证据强弱不对称:解析成功的倒计时是正向证据;「可领取」只是
                        // "这一遍没读到倒计时"的负向推断(实测 1x 会把冒号读丢,伪造可领取)。
                        // 负向证据不得推翻正向证据——丢弃此票,保留制造中票。
                        _log.Debug("{Facility} 丢弃可领取票(已持有制造中票)。", FacilityKeys.DisplayName(key));
                    }
                    else
                    {
                        claims[key] = (obs, capMs); // 与前票冲突:以最新一票为基准继续观察
                    }
                }
                else
                {
                    claims[key] = (obs, capMs); // 首票
                }
            }
            if (agreed.Count == FacilityKeys.All.Length)
            {
                // 读数补偿:剩余时间对应各自截帧那一刻,返回前减去识别已耗掉的时间,
                // 消除「游戏里少一两秒」的系统性滞后。
                long now = Environment.TickCount64;
                return FacilityKeys.All.ToDictionary(k => k, k =>
                {
                    var (obs, capAt) = agreed[k];
                    if (obs.Phase != FacilityPhase.Crafting || obs.Remaining is not { } r) return obs;
                    var adjusted = r - TimeSpan.FromMilliseconds(now - capAt);
                    return obs with { Remaining = adjusted > TimeSpan.Zero ? adjusted : TimeSpan.Zero };
                });
            }
            if (pass < MaxPasses) await Task.Delay(PassDelayMs, ct);
        }

        var unresolved = FacilityKeys.All.Where(k => !agreed.ContainsKey(k)).Select(k =>
            claims.TryGetValue(k, out var c)
                ? $"{FacilityKeys.DisplayName(k)}(最后判定 {c.Obs.Phase},未获第二票)"
                : $"{FacilityKeys.DisplayName(k)}(始终读空)");
        var (png, dumpText) = await _probe.DumpAsync(hwnd, "fail-观察设施槽位");
        throw new StepFailedException("观察设施状态",
            $"连续 {MaxPasses} 遍读取仍有槽位无法得到一致判定:{string.Join("、", unresolved)}。" +
            $"请核对 slot 区域标定与 keywords。诊断截图:{png}", png, dumpText);
    }

    /// <summary>两票间倒计时一致性:剩余时间必须随实际流逝单调递减(允许秒级抖动)。
    /// 非制造中或缺读数时不设此约束。</summary>
    private static bool CountdownConsistent(FacilityObservation prev, long prevMs,
        FacilityObservation cur, long curMs)
    {
        if (prev.Phase != FacilityPhase.Crafting ||
            prev.Remaining is not { } r1 || cur.Remaining is not { } r2) return true;
        double elapsed = (curMs - prevMs) / 1000.0;
        double drop = (r1 - r2).TotalSeconds;
        return drop >= -2 && drop <= elapsed + 8;
    }

    /// <summary>两票一致后的合并:取最新一票(倒计时更准),物品名取非空者。</summary>
    private static FacilityObservation Merge(FacilityObservation prev, FacilityObservation cur) =>
        cur with { ItemName = cur.ItemName.Length > 0 ? cur.ItemName : prev.ItemName };

    private FacilityObservation Classify(string raw)
    {
        string norm = ScreenProbe.Normalize(raw);
        var kw = _probe.Anchors.Keywords;
        string itemName = ExtractItemName(raw, kw);

        if (kw.Idle.Any(k => norm.Contains(ScreenProbe.Normalize(k), StringComparison.Ordinal)))
            return new FacilityObservation(FacilityPhase.Idle, null, "", raw);
        if (CountdownParser.TryParse(raw, out var remaining))
            return new FacilityObservation(FacilityPhase.Crafting, remaining, itemName, raw);
        // 存在「时间形」行(数字+冒号)却解析不出 → 槽位在制造中,只是读数被误读
        // (实测 0 被读成 ℃/口)。这是一次失败的测量,判 Unknown 等下一遍重读;
        // 绝不能因为解析不出时间就滑落成「可领取」。
        if (raw.Split('\n').Any(IsTimeLike))
            return new FacilityObservation(FacilityPhase.Unknown, null, "", raw);
        if (itemName.Length >= 2)
            return new FacilityObservation(FacilityPhase.ReadyToCollect, null, itemName, raw);
        return new FacilityObservation(FacilityPhase.Unknown, null, "", raw);
    }

    /// <summary>时间形判定:含冒号记号且有 ≥2 个数字的行,结构上就是倒计时,
    /// 无论个别数字被误读成什么字符。℃ 计为冒号记号(OCR 把「:0」合并读成 ℃)。
    /// 物品名不含冒号与 ℃,不会误伤。</summary>
    private static bool IsTimeLike(string line) =>
        line.Count(c => c is ':' or '：' or '℃') >= 1 && line.Count(char.IsDigit) >= 2;

    /// <summary>槽位文本里最长的非倒计时行即物品名(制造中与完成态槽位都显示物品名)。
    /// 时间形行(见 IsTimeLike)一律排除——哪怕数字被误读成解析不出的字符
    /// (实测「11:05:04」读作「11：05：口4」「10:50:47」读作「10：54℃4」),
    /// 也绝不能当成物品名。</summary>
    private static string ExtractItemName(string raw, StateKeywords kw)
    {
        return raw.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length >= 2)
            .Where(l => !IsTimeLike(l))
            .Where(l => !CountdownParser.TryParse(l, out _))
            .Where(l => !kw.Idle.Any(k => l.Contains(k, StringComparison.Ordinal)))
            .OrderByDescending(l => l.Length)
            .FirstOrDefault() ?? "";
    }

    /// <summary>领取产物并回到特勤处总览。</summary>
    public async Task CollectAsync(nint hwnd, FacilityKey key, CancellationToken ct)
    {
        string name = FacilityKeys.DisplayName(key);
        _probe.ClickPoint(hwnd, AnchorKeys.SpecOpsHome, AnchorKeys.FacilitySlot(key));

        // 分支一:直达「获得奖励」;分支二:进入生产页,需再点「领取」按钮。
        string? screen = await WaitForAsync(hwnd, [AnchorKeys.CollectResult, AnchorKeys.Production], 12_000, ct);
        if (screen is null)
        {
            var (png, dumpText) = await _probe.DumpAsync(hwnd, "fail-领取入口");
            throw new StepFailedException($"领取{name}产物",
                $"点击槽位后未出现结算或生产界面。诊断截图:{png}", png, dumpText);
        }
        if (screen == AnchorKeys.Production)
        {
            // 进了生产页说明领取按钮在右下三态按钮位;点它并以结算界面出现为准。
            await _runner.RunAsync(hwnd, new Step(
                $"点击{name}领取按钮",
                () => _probe.ClickPoint(hwnd, AnchorKeys.Production, AnchorKeys.PointActionButton),
                () => _probe.IsOnAsync(hwnd, AnchorKeys.CollectResult),
                TimeSpan.FromSeconds(12)), ct);
        }

        await _runner.RunAsync(hwnd, new Step(
            $"关闭{name}领取结算",
            () => _probe.ClickPoint(hwnd, AnchorKeys.CollectResult, AnchorKeys.PointDismiss),
            async () => await _probe.WhichScreenAsync(hwnd,
                [AnchorKeys.SpecOpsHome, AnchorKeys.Production]) is not null,
            TimeSpan.FromSeconds(12)), ct);

        // 若结算后回到的是生产页,再 ESC 返回总览。
        if (await _probe.IsOnAsync(hwnd, AnchorKeys.Production))
        {
            await _runner.RunAsync(hwnd, new Step(
                "返回特勤处总览",
                () => _input.PressEscape(),
                () => _probe.IsOnAsync(hwnd, AnchorKeys.SpecOpsHome),
                TimeSpan.FromSeconds(8)), ct);
        }
        _log.Information("{Facility} 已领取。", name);
    }

    private async Task<string?> WaitForAsync(nint hwnd, string[] screens, int timeoutMs, CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var hit = await _probe.WhichScreenAsync(hwnd, screens);
            if (hit is not null) return hit;
            await Task.Delay(800, ct);
        }
        return null;
    }
}
