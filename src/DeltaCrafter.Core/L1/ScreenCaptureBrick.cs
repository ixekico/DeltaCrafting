using System.ComponentModel;
using System.Runtime.InteropServices;
using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1.Win32;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 屏幕拷贝捕获(GDI BitBlt)。选择屏幕 DC 而非 PrintWindow:游戏为 DX 渲染,
/// PrintWindow 常得黑屏;流程保证捕获前窗口已前台,屏幕上的像素即游戏画面。
/// </summary>
public sealed class ScreenCaptureBrick
{
    public CapturedFrame CaptureClient(PixelRect rect)
    {
        int w = rect.Width, h = rect.Height;
        nint screenDc = NativeCaptureApi.GetDC(0);
        if (screenDc == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC 失败");
        nint memDc = 0, hbmp = 0, oldSel = 0;
        try
        {
            memDc = NativeCaptureApi.CreateCompatibleDC(screenDc);
            hbmp = NativeCaptureApi.CreateCompatibleBitmap(screenDc, w, h);
            if (memDc == 0 || hbmp == 0)
                throw new InvalidOperationException("创建内存位图失败(GDI 资源不足?)。");

            oldSel = NativeCaptureApi.SelectObject(memDc, hbmp);
            if (!NativeCaptureApi.BitBlt(memDc, 0, 0, w, h, screenDc, rect.Left, rect.Top,
                    NativeCaptureApi.SRCCOPY | NativeCaptureApi.CAPTUREBLT))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt 屏幕拷贝失败");
            // GetDIBits 要求位图未被选入 DC,先换回旧位图。
            NativeCaptureApi.SelectObject(memDc, oldSel);
            oldSel = 0;

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h, // 负高:自顶向下行序,与 CapturedFrame 约定一致
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeCaptureApi.BI_RGB,
                },
            };
            var pixels = new byte[w * h * 4];
            int lines = NativeCaptureApi.GetDIBits(memDc, hbmp, 0, (uint)h, pixels, ref bmi,
                NativeCaptureApi.DIB_RGB_COLORS);
            if (lines != h)
                throw new InvalidOperationException($"GetDIBits 仅返回 {lines}/{h} 行,捕获不完整。");
            return new CapturedFrame(w, h, pixels);
        }
        finally
        {
            if (oldSel != 0) NativeCaptureApi.SelectObject(memDc, oldSel);
            if (hbmp != 0) NativeCaptureApi.DeleteObject(hbmp);
            if (memDc != 0) NativeCaptureApi.DeleteDC(memDc);
            NativeCaptureApi.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>保存 PNG(失败诊断截图用)。IO 失败向上抛,由调用方决定是否致命。</summary>
    public async Task SavePngAsync(CapturedFrame frame, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var mem = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, mem);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)frame.Width, (uint)frame.Height, 96, 96, frame.Bgra);
        await encoder.FlushAsync();

        mem.Seek(0);
        using var src = mem.AsStreamForRead();
        using var dst = File.Create(path);
        await src.CopyToAsync(dst);
    }
}
