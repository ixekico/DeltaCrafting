namespace DeltaCrafter.Core.L0;

/// <summary>
/// 自动化步骤失败。抛出即中止当前一轮(除"材料不足"这类显式业务结果外,
/// 不存在跳过某个失败步骤继续跑的路径——那会把点错按钮的后果放大)。
/// ScreenshotPath/OcrDump 供人工定位:失败时的画面与识别原文。
/// </summary>
public sealed class StepFailedException : Exception
{
    public string StepName { get; }
    public string? ScreenshotPath { get; }
    public string? OcrDump { get; }

    public StepFailedException(string stepName, string message,
        string? screenshotPath = null, string? ocrDump = null, Exception? inner = null)
        : base($"步骤[{stepName}]失败:{message}", inner)
    {
        StepName = stepName;
        ScreenshotPath = screenshotPath;
        OcrDump = ocrDump;
    }
}
