using System.Runtime.InteropServices.WindowsRuntime;
using DeltaCrafter.Core.L0;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace DeltaCrafter.Core.L1;

/// <summary>一行 OCR 结果。中心坐标为原始帧内的物理像素(已换算回裁剪/缩放前)。</summary>
public sealed record OcrLine(string Text, double CenterX, double CenterY);

public sealed record OcrReadout(string FullText, IReadOnlyList<OcrLine> Lines);

/// <summary>
/// Windows 内置中文 OCR 封装。约束:
/// 1) 游戏小字号文本直接识别率差,默认放大 2 倍(最近邻)后再识别;
/// 2) OcrEngine 有最大边长限制,超限时自动降低倍率乃至降采样(全屏诊断转储场景);
/// 3) 缺中文语言包属环境错误,构造时立即抛出并给出安装指引,不降级到其他语言。
/// </summary>
public sealed class OcrBrick
{
    private readonly OcrEngine _engine;

    private OcrBrick(OcrEngine engine) => _engine = engine;

    public static OcrBrick CreateSimplifiedChinese()
    {
        var lang = new Language("zh-Hans");
        if (!OcrEngine.IsLanguageSupported(lang))
            throw new InvalidOperationException(
                "系统缺少简体中文 OCR 组件:请到 Windows 设置 → 时间和语言 → 语言和区域 → " +
                "中文(简体) → 语言选项,安装「光学字符识别」后重启本程序。");
        var engine = OcrEngine.TryCreateFromLanguage(lang)
            ?? throw new InvalidOperationException("中文 OCR 引擎初始化失败(语言包状态异常)。");
        return new OcrBrick(engine);
    }

    /// <summary>识别帧内区域文本。roi 为空表示整帧(诊断用)。</summary>
    public async Task<OcrReadout> ReadAsync(CapturedFrame frame, NRect? roi = null, double upscale = 2.0)
    {
        var (cx, cy, cw, ch) = roi is null
            ? (0, 0, frame.Width, frame.Height)
            : PixelMapper.ToPixelRect(roi, frame.Width, frame.Height);

        // 实际倍率 = 期望倍率与引擎尺寸上限的较小者;全帧转储时可能 <1(降采样)。
        double maxDim = OcrEngine.MaxImageDimension;
        double s = Math.Min(upscale, Math.Min(maxDim / cw, maxDim / ch));
        int dw = Math.Max(1, (int)(cw * s));
        int dh = Math.Max(1, (int)(ch * s));

        var resampled = NearestResample(frame, cx, cy, cw, ch, dw, dh);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            resampled.AsBuffer(), BitmapPixelFormat.Bgra8, dw, dh, BitmapAlphaMode.Ignore);
        var result = await _engine.RecognizeAsync(bitmap);

        double sx = (double)dw / cw, sy = (double)dh / ch;
        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                minX = Math.Min(minX, r.X);
                minY = Math.Min(minY, r.Y);
                maxX = Math.Max(maxX, r.X + r.Width);
                maxY = Math.Max(maxY, r.Y + r.Height);
            }
            if (minX > maxX) continue; // 无词的空行,丢弃
            lines.Add(new OcrLine(line.Text,
                cx + (minX + maxX) / 2 / sx,
                cy + (minY + maxY) / 2 / sy));
        }
        return new OcrReadout(string.Join("\n", lines.Select(l => l.Text)), lines);
    }

    /// <summary>最近邻重采样(裁剪+缩放一步完成)。识别用途下质量足够,且零额外依赖。</summary>
    private static byte[] NearestResample(CapturedFrame f, int cx, int cy, int cw, int ch, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
        {
            int sy = cy + (int)((long)y * ch / dh);
            int srcRow = sy * f.Width * 4;
            int dstRow = y * dw * 4;
            for (int x = 0; x < dw; x++)
            {
                int sx2 = cx + (int)((long)x * cw / dw);
                int si = srcRow + sx2 * 4;
                int di = dstRow + x * 4;
                dst[di] = f.Bgra[si];
                dst[di + 1] = f.Bgra[si + 1];
                dst[di + 2] = f.Bgra[si + 2];
                dst[di + 3] = f.Bgra[si + 3];
            }
        }
        return dst;
    }
}
