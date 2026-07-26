using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>中止结果。Aborted=false 且无异常表示"设施本就不在制造中"(无需中止,未改动)。</summary>
public sealed record AbortResult(bool Aborted, string Message);

/// <summary>
/// 「中止制造」流程:点设施槽位进生产界面 → 确认右下按钮确实是「中止」(即真在制造)→
/// 点中止 → 处理可能的确认弹窗 → 校验设施回到空闲。销毁性操作,从严:
/// 按钮不是「中止」绝不点;确认弹窗未校准时绝不乱点未知坐标,而是留现场报错待标定。
/// </summary>
public sealed class AbortFlow
{
    private readonly ScreenProbe _probe;
    private readonly StepRunner _runner;
    private readonly InputBrick _input;
    private readonly ILogger _log;

    public AbortFlow(ScreenProbe probe, StepRunner runner, InputBrick input, ILogger log)
    {
        _probe = probe;
        _runner = runner;
        _input = input;
        _log = log.ForContext<AbortFlow>();
    }

    public async Task<AbortResult> AbortAsync(nint hwnd, FacilityKey key, CancellationToken ct)
    {
        string facility = FacilityKeys.DisplayName(key);
        var prodSpec = _probe.Screen(AnchorKeys.Production);
        var kw = _probe.Anchors.Keywords;

        await _runner.RunAsync(hwnd, new Step(
            $"打开{facility}生产界面(中止)",
            () => _probe.ClickPoint(hwnd, AnchorKeys.SpecOpsHome, AnchorKeys.FacilitySlot(key)),
            () => _probe.IsOnAsync(hwnd, AnchorKeys.Production),
            TimeSpan.FromSeconds(12)), ct);

        string label = await ReadLabelAsync(hwnd, prodSpec, ct);
        if (!LabelHits(label, kw.ButtonAbort))
        {
            // 非"中止"态说明未在制造(可能刚完成或已空闲):不点,原样退回,交调用方如实汇报。
            await EscBackHomeAsync(hwnd, ct);
            return new AbortResult(false, $"{facility}当前不在制造中(按钮为「{label}」),未做改动");
        }

        _log.Information("{Facility} 点击中止。", facility);
        _probe.ClickPoint(hwnd, AnchorKeys.Production, AnchorKeys.PointActionButton);

        await ResolveAbortAsync(hwnd, facility, prodSpec, kw, ct);
        await EnsureBackHomeAsync(hwnd, ct);
        _log.Information("{Facility} 制造已中止。", facility);
        return new AbortResult(true, $"{facility}已中止制造");
    }

    /// <summary>点中止后的结果收敛:回到总览 / 按钮变为可生产 / 出现确认弹窗(需已校准才点)/ 超时失败。</summary>
    private async Task ResolveAbortAsync(nint hwnd, string facility, ScreenSpec prodSpec,
        StateKeywords kw, CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + 12_000;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(800, ct);

            // 确认弹窗优先判定(它可能浮在生产界面之上,生产界面探针仍会命中)。
            string? screen = await _probe.WhichScreenAsync(hwnd,
                [AnchorKeys.AbortConfirm, AnchorKeys.SpecOpsHome, AnchorKeys.Production]);

            if (screen == AnchorKeys.SpecOpsHome) return; // 中止后直接回到总览

            if (screen == AnchorKeys.AbortConfirm)
            {
                var confirm = _probe.Screen(AnchorKeys.AbortConfirm).Point(AnchorKeys.PointConfirm);
                if (confirm.X == 0 && confirm.Y == 0)
                {
                    var (png, dump) = await _probe.DumpAsync(hwnd, "fail-中止确认弹窗待校准");
                    throw new StepFailedException($"中止{facility}",
                        "检测到中止确认弹窗,但其确认按钮尚未校准(坐标为 0)。" +
                        $"请把该弹窗截图发来以便标定 abort-confirm。诊断截图:{png}", png, dump);
                }
                _probe.ClickPoint(hwnd, AnchorKeys.AbortConfirm, AnchorKeys.PointConfirm);
                continue;
            }

            if (screen == AnchorKeys.Production)
            {
                // 仍在生产界面:若按钮不再是「中止」(变为 生产/一键补齐),即已中止成功。
                string label = ScreenProbe.Normalize(
                    await _probe.ReadRoiAsync(hwnd, prodSpec.Roi(AnchorKeys.RoiActionButton)));
                if (label.Length > 0 && !LabelHits(label, kw.ButtonAbort)) return;
            }
        }

        var (png2, dump2) = await _probe.DumpAsync(hwnd, "fail-中止未生效");
        throw new StepFailedException($"中止{facility}",
            $"点击中止后 12s 内设施未回到可生产状态(可能有未识别的确认弹窗)。诊断截图:{png2}", png2, dump2);
    }

    private async Task<string> ReadLabelAsync(nint hwnd, ScreenSpec prodSpec, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            string label = ScreenProbe.Normalize(
                await _probe.ReadRoiAsync(hwnd, prodSpec.Roi(AnchorKeys.RoiActionButton)));
            if (label.Length > 0) return label;
            await Task.Delay(1000, ct);
        }
        return "";
    }

    private static bool LabelHits(string normalizedLabel, IEnumerable<string> keywords) =>
        keywords.Any(k => k.Length > 0 &&
            normalizedLabel.Contains(ScreenProbe.Normalize(k), StringComparison.Ordinal));

    private async Task EnsureBackHomeAsync(nint hwnd, CancellationToken ct)
    {
        if (await _probe.IsOnAsync(hwnd, AnchorKeys.SpecOpsHome)) return;
        await EscBackHomeAsync(hwnd, ct);
    }

    private Task EscBackHomeAsync(nint hwnd, CancellationToken ct) =>
        _runner.RunAsync(hwnd, new Step(
            "返回特勤处总览",
            () => _input.PressEscape(),
            () => _probe.IsOnAsync(hwnd, AnchorKeys.SpecOpsHome),
            TimeSpan.FromSeconds(8)), ct);
}
