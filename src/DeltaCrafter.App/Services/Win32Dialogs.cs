using System.Runtime.InteropServices;

namespace DeltaCrafter.App.Services;

/// <summary>
/// Win32 通用对话框。为何不用 WinRT FileOpenPicker:本程序以管理员运行,
/// FileOpenPicker 在提权进程中初始化失败(平台已知限制),comdlg32 不受影响。
/// FatalError 用于 XAML 尚未就绪时的启动期致命错误提示。
/// </summary>
public static class Win32Dialogs
{
    public static void FatalError(string title, string message) =>
        _ = MessageBoxW(0, message, title, MB_OK | MB_ICONERROR);

    /// <summary>选择 exe 文件;用户取消返回 null。</summary>
    public static string? PickExeFile(nint ownerHwnd)
    {
        const int bufferChars = 4096;
        nint buffer = Marshal.AllocHGlobal(bufferChars * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0, 0); // 无初始文件名
            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = "可执行文件 (*.exe)\0*.exe\0所有文件 (*.*)\0*.*\0\0",
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = bufferChars,
                lpstrTitle = "选择游戏可执行文件",
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            };
            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;
    private const int OFN_NOCHANGEDIR = 0x8;
    private const int OFN_PATHMUSTEXIST = 0x800;
    private const int OFN_FILEMUSTEXIST = 0x1000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;   // 手动分配的可写缓冲,返回所选路径
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrTitle;
        public int Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }
}
