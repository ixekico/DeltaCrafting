namespace DeltaCrafter.Core.L0;

/// <summary>一轮收取+续造完成后,对游戏客户端的处置方式。</summary>
public enum AfterRunAction
{
    /// <summary>关闭游戏进程,到点再自动启动(默认,省资源)。</summary>
    CloseGame,
    /// <summary>游戏保持运行,把窗口最小化到后台。(枚举名保持不变以兼容既有配置)</summary>
    KeepRunning,
    /// <summary>游戏保持运行,返回大厅并把窗口留在前台——适合想在大厅挂机的玩家。</summary>
    KeepAtLobby,
}

/// <summary>UI 主题选择。System = 跟随系统。</summary>
public enum ThemeChoice { System, Light, Dark }

/// <summary>
/// 制造物品的选择方式。Custom = 用户在计划页自选;两种利润优先模式下,
/// 四个设施的物品由 kkrb.net「特勤处制作产物推荐」自动填充(每 2 小时刷新),
/// 计划页锁定物品编辑。数据源对同一设施给出同一推荐物品,两种模式的差别是
/// 采信并记录的利润口径(每小时利润 / 单次制造总利润)。
/// </summary>
public enum CraftMode
{
    /// <summary>自定义:计划页手选物品(默认,与历史行为一致)。</summary>
    Custom,
    /// <summary>每小时利润优先:按推荐数据的小时利润口径自动填充。</summary>
    HourlyProfit,
    /// <summary>总利润优先:按推荐数据的单次制造总利润口径自动填充。</summary>
    TotalProfit,
}

/// <summary>
/// 游戏窗口匹配规则。ExactTitle 优先于 TitleContains;ClassName 为可选附加条件。
/// 由设置页"定位游戏窗口"工具一次性写入,避免猜测窗口类名。
/// </summary>
public sealed class WindowMatchRule
{
    public string TitleContains { get; set; } = "三角洲行动";
    public string? ExactTitle { get; set; }
    public string? ClassName { get; set; }
}

/// <summary>
/// 应用设置(持久化于 %LocalAppData%\DeltaCrafter\settings.json)。
/// 约束:仅 UI 线程写入;自动化线程只读,允许读到毫秒级旧值。
/// </summary>
public sealed class AppSettings
{
    /// <summary>游戏或启动器可执行文件完整路径。为空视为未配置,拒绝执行并明确报错。</summary>
    public string GamePath { get; set; } = "";

    /// <summary>启动后等待游戏窗口出现的上限(秒)。超时即判失败,不做无限等待。</summary>
    public int LaunchTimeoutSeconds { get; set; } = 240;

    /// <summary>窗口出现后等待大厅界面就绪的上限(秒),覆盖登录/加载/公告弹窗时间。</summary>
    public int LobbyTimeoutSeconds { get; set; } = 240;

    public AfterRunAction AfterRun { get; set; } = AfterRunAction.CloseGame;

    /// <summary>制造物品选择方式。非 Custom 时计划页物品锁定,由利润推荐自动填充。</summary>
    public CraftMode CraftMode { get; set; } = CraftMode.Custom;

    /// <summary>定时循环总开关。关闭时仅手动"立即执行"生效。</summary>
    public bool AutoLoopEnabled { get; set; }

    /// <summary>制造完成时刻之后追加的缓冲(秒),吸收 OCR 读数与游戏结算的小误差。</summary>
    public int RunBufferSeconds { get; set; } = 60;

    /// <summary>一轮失败后的重试间隔(分钟)。失败会通知,不静默;间隔防止连环失败刷屏。</summary>
    public int FailureRetryMinutes { get; set; } = 30;

    /// <summary>
    /// 材料不足时自动点「一键补齐」购买缺料(消耗游戏内货币,金额随交易行波动,日志留痕)。
    /// 关闭时材料不足的设施标记「需人工」。兑换类材料买不到时同样标记需人工,不会反复烧钱。
    /// </summary>
    public bool AutoReplenishMaterials { get; set; } = true;

    /// <summary>等待期间阻止系统睡眠(不阻止熄屏)。关闭则由用户自行保证到点时电脑醒着。</summary>
    public bool PreventSleepWhileWaiting { get; set; } = true;

    /// <summary>点窗口关闭按钮时最小化到托盘而非退出。</summary>
    public bool CloseToTray { get; set; } = true;

    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>开发者模式:显示总览页「单步调试」等排障工具。普通用户默认隐藏。</summary>
    public bool DeveloperMode { get; set; }

    public WindowMatchRule WindowMatch { get; set; } = new();
}
