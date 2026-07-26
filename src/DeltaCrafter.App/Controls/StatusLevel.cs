namespace DeltaCrafter.App.Controls;

/// <summary>
/// 语义状态级别。UI 只用它选择颜色,颜色本身由 XAML 的 {ThemeResource} 决定,
/// 从而随深浅色主题实时切换(不在 ViewModel 里持有 Brush,避免主题切换不刷新)。
/// </summary>
public enum StatusLevel
{
    Neutral,   // 空闲/未知 —— 中性灰
    Info,      // 进行中 —— 强调蓝
    Success,   // 等待计划/可领取 —— 绿
    Caution,   // 需人工 —— 黄
    Critical,  // 失败 —— 红
}
