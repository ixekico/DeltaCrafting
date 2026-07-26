using DeltaCrafter.Core.L0;

namespace DeltaCrafter.Core.L3;

/// <summary>
/// 单步调试入口(总览页「单步调试」菜单)。与正式一轮共用同一执行闸门与失败语义,
/// 但 affectsSchedule=false:调试动作不写调度状态、不触发失败退避。
/// </summary>
public sealed partial class AutomationCoordinator
{
    public Task<RunReport> DebugEnsureLobbyAsync() =>
        ExecuteGuardedAsync("单步:启动到大厅", affectsSchedule: false, requiresCalibration: true,
            async (r, ct) =>
            {
                await _launch.EnsureLobbyAsync(ct);
                r.Add("已到达大厅(游戏保持打开,供人工检查)");
            });

    public Task<RunReport> DebugEnterSpecOpsAsync() =>
        ExecuteGuardedAsync("单步:进入特勤处", affectsSchedule: false, requiresCalibration: true,
            async (r, ct) =>
            {
                var launch = await _launch.EnsureLobbyAsync(ct);
                await _nav.EnterSpecOpsAsync(launch.Hwnd, ct);
                r.Add("已进入特勤处(游戏保持打开)");
            });

    /// <summary>
    /// 扫描配方目录:进每个设施生产界面滚动读全列表,把配方名合并进候选目录。
    /// 有待领取产物的设施跳过(点它的槽位会触发领取),先执行一轮领完再扫。
    /// </summary>
    public Task<RunReport> DebugScanCatalogAsync() =>
        ExecuteGuardedAsync("单步:扫描配方目录", affectsSchedule: false, requiresCalibration: true,
            async (r, ct) =>
            {
                var launch = await _launch.EnsureLobbyAsync(ct);
                await _nav.EnterSpecOpsAsync(launch.Hwnd, ct);
                var observations = await _collect.ObserveAllAsync(launch.Hwnd, ct);
                foreach (var key in FacilityKeys.All)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = FacilityKeys.DisplayName(key);
                    if (observations[key].Phase == FacilityPhase.ReadyToCollect)
                    {
                        r.Add($"{name}有待领取产物,本次跳过(领取后再扫)");
                        continue;
                    }
                    var names = await _scan.ScanFacilityAsync(launch.Hwnd, key, ct);
                    _catalogSink.MergeScanned(key, names);
                    r.Add($"{name}扫描到 {names.Count} 个配方");
                }
                r.Add("目录已合并保存,制造计划页下拉即刻可用(游戏保持打开)");
            });

    /// <summary>画面诊断:保存整帧截图+全文 OCR。不要求已校准——它正是校准的工具。</summary>
    public Task<RunReport> DebugDumpAsync() =>
        ExecuteGuardedAsync("单步:画面诊断", affectsSchedule: false, requiresCalibration: false,
            async (r, ct) =>
            {
                var s = _settings();
                var win = _windowBrick.FindGameClient(s.WindowMatch)
                    ?? throw new StepFailedException("画面诊断",
                        "未找到 16:9 的游戏客户端窗口。请先手动把游戏开进客户端,或在设置页检查窗口匹配规则。");
                _windowBrick.TryEnsureForeground(win.Hwnd, TimeSpan.FromSeconds(2));
                await Task.Delay(600, ct);
                var (png, _) = await _probe.DumpAsync(win.Hwnd, "debug");
                r.Add($"已保存截图与 OCR 文本:{png}");
            });
}
