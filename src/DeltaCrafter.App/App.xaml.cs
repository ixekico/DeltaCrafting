using DeltaCrafter.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;

namespace DeltaCrafter.App;

public partial class App : Application
{
    public static MainWindow? MainWindowRef { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppHost.Initialize();
        }
        catch (Exception ex)
        {
            // 启动失败(缺中文 OCR 包/配置损坏/构建产物不完整)必须把原因亮给用户,不能无声退出。
            Win32Dialogs.FatalError("三角洲特勤助手无法启动", ex.Message);
            Environment.Exit(1);
            return;
        }

        MainWindowRef = new MainWindow();
        AppHost.Current.AttachMainWindow(MainWindowRef);

        // 二次启动被 Program 里的单实例守卫重定向到本进程 → 把窗口调出来。
        AppInstance.GetCurrent().Activated += (_, _) =>
            MainWindowRef?.DispatcherQueue.TryEnqueue(() => MainWindowRef.RestoreFromTray());

        if (Environment.GetCommandLineArgs().Contains("--minimized"))
            AppHost.Current.Log.Information("以 --minimized 启动,驻留托盘,不显示窗口。");
        else
            MainWindowRef.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 只记录、不拦截:吞掉未处理异常会让程序带着未知状态继续点游戏,比崩溃更危险。
        Log.Fatal(e.Exception, "未处理异常,进程即将退出。");
        Log.CloseAndFlush();
    }
}
