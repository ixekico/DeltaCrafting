namespace DeltaCrafter.Core.L0;

/// <summary>
/// 用户可见通知端口。实现位于 UI 层(系统 Toast);核心层只声明契约,
/// 保证 L0-L3 不反向依赖 WindowsAppSDK。实现失败应记日志,不得让通知问题中断主流程。
/// </summary>
public interface INotifier
{
    void Notify(string title, string message);
}

/// <summary>
/// 配方目录接收端。扫描流程把游戏里读到的配方名交给 UI 层合并入目录
/// (存盘 + 刷新计划页下拉),核心层不关心目录的存储位置。
/// </summary>
public interface ICatalogSink
{
    void MergeScanned(FacilityKey key, IReadOnlyList<string> names);
}

/// <summary>
/// 目录查询端口:把槽位 OCR 读到的物品名解析为目录里的规范显示名,
/// 解析不出(目录外物品/读数太烂)返回 null,调用方保留原文——不硬猜。
/// 目录的存取在 UI 层,故以端口形式供核心层使用。
/// </summary>
public interface ICatalogLookup
{
    string? ResolveDisplayName(FacilityKey key, string ocrName);
}

/// <summary>
/// 助手窗口守卫。截图走屏幕拷贝,助手自己的窗口若盖在游戏上会污染识别——
/// 执行期间把窗口最小化,结束后恢复到执行前的可见状态。UI 层实现。
/// </summary>
public interface IAppWindowGuard
{
    void MinimizeForRun();
    void RestoreAfterRun();
}

/// <summary>时间源。调度逻辑一律经此取"现在",使单元测试可控。</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>BGRA32 位图帧(顶行在前)。捕获与 OCR 之间的通用载体。</summary>
public sealed record CapturedFrame(int Width, int Height, byte[] Bgra);
