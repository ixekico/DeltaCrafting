namespace DeltaCrafter.Core.L0;

/// <summary>客户区归一化坐标点(0..1)。与分辨率无关,任意 16:9 窗口按比例还原。</summary>
public sealed class NPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>客户区归一化矩形(0..1)。用于 OCR 感兴趣区域(ROI)。</summary>
public sealed class NRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>界面判定探针:在 Roi 内 OCR 出的文本必须包含 MustContain,才认定处于该界面。</summary>
public sealed class TextProbe
{
    public NRect Roi { get; set; } = new();
    public string MustContain { get; set; } = "";
}

/// <summary>一个游戏界面的锚点集合:判定探针 + 可点击点位 + 可读取区域。</summary>
public sealed class ScreenSpec
{
    public TextProbe Probe { get; set; } = new();
    public Dictionary<string, NPoint> Points { get; set; } = [];
    public Dictionary<string, NRect> Rois { get; set; } = [];

    public NPoint Point(string name) => Points.TryGetValue(name, out var p)
        ? p : throw new InvalidOperationException($"锚点缺少点位 '{name}',请检查 anchors.json。");

    public NRect Roi(string name) => Rois.TryGetValue(name, out var r)
        ? r : throw new InvalidOperationException($"锚点缺少区域 '{name}',请检查 anchors.json。");
}

/// <summary>状态与按钮文字关键词。以游戏实际措辞为准;右下角按钮的文字即生产界面的状态。</summary>
public sealed class StateKeywords
{
    public List<string> Idle { get; set; } = ["空闲中", "空闲"];
    public List<string> ButtonProduce { get; set; } = ["生产"];
    public List<string> ButtonReplenish { get; set; } = ["一键补齐", "补齐"];
    public List<string> ButtonAbort { get; set; } = ["中止"];
    public List<string> ButtonCollect { get; set; } = ["领取"];
    /// <summary>启动器上的进入游戏按钮文字(整窗 OCR 按文字定位点击)。</summary>
    public List<string> LauncherStart { get; set; } = ["开始游戏"];
}

/// <summary>
/// 锚点总表(anchors.json)。Calibrated=false 时拒绝执行自动化——宁可明确拒绝,
/// 也不用占位坐标乱点。当前出厂值按 2560×1440 实机截图标定(归一化后与分辨率无关)。
/// </summary>
public sealed class AnchorTable
{
    public bool Calibrated { get; set; }
    /// <summary>默认表修订号。程序启动时若默认表比本地副本新,自动备份并替换本地副本。</summary>
    public int Revision { get; set; }
    /// <summary>标定来源说明,仅作人读参考;运行时一律按比例换算。</summary>
    public string CalibratedOn { get; set; } = "";
    public StateKeywords Keywords { get; set; } = new();
    public Dictionary<string, ScreenSpec> Screens { get; set; } = [];

    public ScreenSpec Screen(string name) => Screens.TryGetValue(name, out var s)
        ? s : throw new InvalidOperationException($"anchors.json 缺少界面 '{name}' 的定义。");
}

/// <summary>
/// anchors.json 中的界面/点位/区域名称常量。改名必须同步数据文件与本类。
/// 界面流:mode-select →(点烽火地带)→ safehouse →(Tab)→ lobby →(点顶栏特勤处)→
/// specops-home(四设施槽位一屏可见)→(点槽位)→ production / collect-result。
/// </summary>
public static class AnchorKeys
{
    public const string ModeSelect = "mode-select";
    public const string Safehouse = "safehouse";
    public const string Lobby = "lobby";
    public const string SpecOpsHome = "specops-home";
    public const string Production = "production";
    public const string ReplenishPopup = "replenish-popup";
    public const string CollectResult = "collect-result";
    public const string AbortConfirm = "abort-confirm";

    public const string PointModeEntry = "mode-entry";
    public const string PointSpecOpsEntry = "specops-entry";
    public const string PointBackToLobby = "back-to-lobby";
    public const string PointDismiss = "dismiss";
    public const string PointActionButton = "action-button";
    public const string PointBuy = "buy";
    public const string PointConfirm = "confirm";
    public static string FacilitySlot(FacilityKey key) => "slot-" + FacilityKeys.JsonKey(key);

    public const string RoiListArea = "list-area";
    public const string RoiDetailTitle = "detail-title";
    public const string RoiActionButton = "action-button";
    public const string RoiRemainingTime = "remaining-time";
}

/// <summary>归一化几何到物理像素的纯换算,便于单元测试。</summary>
public static class PixelMapper
{
    public static (int X, int Y) ToPixel(NPoint p, int left, int top, int width, int height) =>
        (left + (int)Math.Round(p.X * width), top + (int)Math.Round(p.Y * height));

    /// <summary>ROI 换算并夹取到帧内,避免 OCR 裁剪越界。宽高至少 1 像素。</summary>
    public static (int X, int Y, int W, int H) ToPixelRect(NRect r, int width, int height)
    {
        int x = Math.Clamp((int)Math.Round(r.X * width), 0, width - 1);
        int y = Math.Clamp((int)Math.Round(r.Y * height), 0, height - 1);
        int w = Math.Clamp((int)Math.Round(r.W * width), 1, width - x);
        int h = Math.Clamp((int)Math.Round(r.H * height), 1, height - y);
        return (x, y, w, h);
    }
}
