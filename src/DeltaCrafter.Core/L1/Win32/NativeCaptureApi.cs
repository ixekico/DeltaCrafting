using System.Runtime.InteropServices;

namespace DeltaCrafter.Core.L1.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    // 32bpp BI_RGB 无调色板;GetDIBits 对高位深不会写入 bmiColors。
}

/// <summary>GDI 屏幕拷贝相关声明。捕获走"屏幕 DC + 窗口客户区屏幕坐标",
/// 前提是目标窗口已前台且未被遮挡——流程层在捕获前必须先完成前台化。</summary>
internal static class NativeCaptureApi
{
    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint hdc, nint hObject);

    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int cx, int cy,
        nint hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(nint hdc, nint hbm, uint start, uint cLines,
        byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);

    internal const uint SRCCOPY = 0x00CC0020;
    internal const uint CAPTUREBLT = 0x40000000;
    internal const uint BI_RGB = 0;
    internal const uint DIB_RGB_COLORS = 0;
}
