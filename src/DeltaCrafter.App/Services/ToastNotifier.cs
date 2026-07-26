using DeltaCrafter.Core.L0;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Serilog;

namespace DeltaCrafter.App.Services;

/// <summary>
/// INotifier 的系统 Toast 实现。通知是辅助通道:注册或发送失败记 Error 日志后继续,
/// 不允许通知问题中断自动化主流程(契约见 L0.INotifier)。
/// </summary>
public sealed class ToastNotifier : INotifier
{
    private readonly ILogger _log;
    private readonly bool _registered;

    public ToastNotifier(ILogger log)
    {
        _log = log.ForContext<ToastNotifier>();
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "系统通知注册失败,后续通知只写日志。");
        }
    }

    public void Notify(string title, string message)
    {
        _log.Information("[通知] {Title}:{Message}", title, message);
        if (!_registered) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "发送系统通知失败。");
        }
    }

    public void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception ex) { _log.Debug(ex, "通知反注册失败(进程即将退出,忽略)。"); }
    }
}
