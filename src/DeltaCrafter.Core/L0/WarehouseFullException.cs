namespace DeltaCrafter.Core.L0;

/// <summary>
/// 游戏明确提示仓库容量不足。它会使补齐或领取保持在原状态，必须中止本轮并提醒用户；
/// 继续重试只会重复同一失败动作，不能把它归为普通材料不足。
/// </summary>
public sealed class WarehouseFullException : StepFailedException
{
    public WarehouseFullException(string operation)
        : base(operation, "检测到游戏提示“仓库空间不足”。请及时清理游戏仓库后再运行。")
    {
    }
}
