using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Serilog;

namespace DeltaCrafter.Core.L2;

/// <summary>
/// 画面探针:把「窗口几何 + 截图 + OCR + 点击」组合成流程层可用的原语。
/// 所有读取都基于"捕获当下一帧",不缓存旧帧——过期画面比没有画面更危险。
/// </summary>
public sealed class ScreenProbe
{
    private readonly GameWindowBrick _window;
    private readonly ScreenCaptureBrick _capture;
    private readonly OcrBrick _ocr;
    private readonly InputBrick _input;
    private readonly Func<AnchorTable> _anchors;
    private readonly string _shotsDir;
    private readonly ILogger _log;

    public ScreenProbe(GameWindowBrick window, ScreenCaptureBrick capture, OcrBrick ocr,
        InputBrick input, Func<AnchorTable> anchors, string shotsDir, ILogger log)
    {
        _window = window;
        _capture = capture;
        _ocr = ocr;
        _input = input;
        _anchors = anchors;
        _shotsDir = shotsDir;
        _log = log.ForContext<ScreenProbe>();
    }

    public AnchorTable Anchors => _anchors();

    public ScreenSpec Screen(string name) => Anchors.Screen(name);

    public CapturedFrame Capture(nint hwnd) => _capture.CaptureClient(_window.ClientRectOnScreen(hwnd));

    public async Task<string> ReadRoiAsync(nint hwnd, NRect roi) =>
        (await _ocr.ReadAsync(Capture(hwnd), roi)).FullText;

    /// <summary>一次捕获、多区域识别,保证多个读数来自同一帧。
    /// upscale 可指定识别倍率:2x 适合小字号;1x 适合低对比大字(见 CollectFlow 交替倍率观察)。</summary>
    public async Task<string[]> ReadRoisAsync(nint hwnd, IReadOnlyList<NRect> rois, double upscale = 2.0)
    {
        var frame = Capture(hwnd);
        var result = new string[rois.Count];
        for (int i = 0; i < rois.Count; i++)
            result[i] = (await _ocr.ReadAsync(frame, rois[i], upscale)).FullText;
        return result;
    }

    /// <summary>单帧多界面判定:返回第一个探针命中的界面名,均未命中返回 null。
    /// 一次截帧多次判定,避免逐界面截图造成的时间错位。</summary>
    public async Task<string?> WhichScreenAsync(nint hwnd, IReadOnlyList<string> screenNames)
    {
        var frame = Capture(hwnd);
        foreach (var name in screenNames)
        {
            var spec = Screen(name);
            string text = (await _ocr.ReadAsync(frame, spec.Probe.Roi)).FullText;
            if (Normalize(text).Contains(Normalize(spec.Probe.MustContain), StringComparison.Ordinal))
                return name;
        }
        return null;
    }

    /// <summary>是否处于指定界面:探针区域 OCR 文本包含约定关键字。</summary>
    public async Task<bool> IsOnAsync(nint hwnd, string screenName)
    {
        var spec = Screen(screenName);
        string text = await ReadRoiAsync(hwnd, spec.Probe.Roi);
        bool on = Normalize(text).Contains(Normalize(spec.Probe.MustContain), StringComparison.Ordinal);
        _log.Debug("界面判定 {Screen}:{Result}(读到:{Text})", screenName, on, Compact(text));
        return on;
    }

    public void ClickPoint(nint hwnd, string screenName, string pointName)
    {
        var rect = _window.ClientRectOnScreen(hwnd);
        var p = Screen(screenName).Point(pointName);
        var (x, y) = PixelMapper.ToPixel(p, rect.Left, rect.Top, rect.Width, rect.Height);
        _log.Debug("点击 {Screen}.{Point} → 屏幕({X},{Y})", screenName, pointName, x, y);
        _input.ClickAt(x, y);
    }

    /// <summary>点击帧内像素坐标(OCR 定位到的行中心)。</summary>
    public void ClickFramePoint(nint hwnd, double frameX, double frameY)
    {
        var rect = _window.ClientRectOnScreen(hwnd);
        _input.ClickAt(rect.Left + (int)Math.Round(frameX), rect.Top + (int)Math.Round(frameY));
    }

    /// <summary>在区域中心滚动(负档向下翻列表)。</summary>
    public void ScrollRoi(nint hwnd, NRect roi, int notches)
    {
        var rect = _window.ClientRectOnScreen(hwnd);
        var center = new NPoint { X = roi.X + roi.W / 2, Y = roi.Y + roi.H / 2 };
        var (x, y) = PixelMapper.ToPixel(center, rect.Left, rect.Top, rect.Width, rect.Height);
        _input.ScrollAt(x, y, notches);
    }

    /// <summary>在区域内按文本找行(TextMatch 规范形包含匹配,抗 0/O、1/I 同形误读)。
    /// 找不到返回 null,由调用方决定翻页或失败。</summary>
    public async Task<OcrLine?> FindLineAsync(nint hwnd, NRect area, string target)
    {
        var readout = await _ocr.ReadAsync(Capture(hwnd), area);
        return readout.Lines.FirstOrDefault(l => TextMatch.LineContains(l.Text, target));
    }

    /// <summary>读取区域内全部 OCR 行(配方目录扫描用)。</summary>
    public async Task<IReadOnlyList<OcrLine>> ReadAreaLinesAsync(nint hwnd, NRect area) =>
        (await _ocr.ReadAsync(Capture(hwnd), area)).Lines;

    /// <summary>保存整帧截图与全文 OCR 转储(失败现场/校准诊断)。返回(截图路径, OCR 文本)。</summary>
    public async Task<(string PngPath, string OcrText)> DumpAsync(nint hwnd, string tag)
    {
        var frame = Capture(hwnd);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string png = Path.Combine(_shotsDir, $"{stamp}-{tag}.png");
        await _capture.SavePngAsync(frame, png);
        var readout = await _ocr.ReadAsync(frame, roi: null, upscale: 1.0);
        await File.WriteAllTextAsync(Path.ChangeExtension(png, ".txt"), readout.FullText);
        _log.Information("已保存诊断截图:{Png}", png);
        return (png, readout.FullText);
    }

    /// <summary>OCR 文本归一化:去除空白(中文 OCR 常在词间插入空格)。</summary>
    public static string Normalize(string s) =>
        string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    private static string Compact(string s)
    {
        var one = Normalize(s);
        return one.Length <= 40 ? one : one[..40] + "…";
    }
}
