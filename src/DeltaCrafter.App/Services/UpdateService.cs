using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L3;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace DeltaCrafter.App.Services;

/// <summary>
/// 应用内更新编排:启动自动检查 + 设置页手动检查,发现新版弹窗确认后
/// 下载、校验、静默安装并重启。更新是辅助通道:启动期检查失败只记日志,
/// 手动检查的失败原文回给设置页显示;下载校验失败在弹窗内亮明,绝不带病安装。
/// 本机数据(制造计划、倒计时调度、设置)在 %LocalAppData%,安装升级不触碰。
/// </summary>
public sealed class UpdateService
{
    private readonly AppHost _host;
    private readonly UpdateCoordinator _updates;
    private readonly ILogger _log;
    private bool _dialogOpen;
    private bool _installing;

    public UpdateService(AppHost host, UpdateCoordinator updates, ILogger log)
    {
        _host = host;
        _updates = updates;
        _log = log.ForContext<UpdateService>();
    }

    /// <summary>启动自动检查(每次启动都查)。网络失败不打扰用户;窗口驻留托盘时
    /// 发现新版只发系统通知,避免弹窗弹在一个看不见的窗口里。</summary>
    public async Task CheckOnStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3)); // 让窗口/托盘先就绪
            var info = await _updates.CheckLatestAsync(CancellationToken.None);
            if (!info.IsNewer)
            {
                _log.Information("启动更新检查:当前 v{Current} 已是最新。", info.Current.ToString(3));
                return;
            }
            _log.Information("启动更新检查:发现新版本 {Tag}(当前 v{Current})。",
                info.TagName, info.Current.ToString(3));
            var window = App.MainWindowRef;
            if (window is null) return;
            window.DispatcherQueue.TryEnqueue(async () =>
            {
                // 这是 async void 上下文:异常若逸出会走 App.OnUnhandledException 终止进程,
                // 与「启动检查失败只记日志」矛盾。就地兜住并降级为通知。
                try
                {
                    if (window.AppWindow.IsVisible)
                        await PromptAndUpdateAsync(info);
                    else
                        _host.Notifier.Notify($"发现新版本 {info.TagName}",
                            "打开窗口后在「设置 → 关于 → 检查更新」一键更新;制造计划与倒计时不受影响。");
                }
                catch (Exception ex)
                {
                    _log.Warning("弹出更新提示失败(不影响使用):{Message}", ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            _log.Warning("启动更新检查失败(不影响使用):{Message}", ex.Message);
        }
    }

    /// <summary>设置页「检查更新」。返回给状态文本的结论;发现新版会直接弹更新窗。
    /// 检查失败把原因原样抛给调用方显示。</summary>
    public async Task<string> CheckFromSettingsAsync()
    {
        var info = await _updates.CheckLatestAsync(CancellationToken.None);
        if (!info.IsNewer)
            return $"已是最新版本(v{info.Current.ToString(3)})";
        if (_dialogOpen) return $"发现新版本 {info.TagName},更新窗口已打开";
        await PromptAndUpdateAsync(info);
        return $"发现新版本 {info.TagName}";
    }

    /// <summary>更新弹窗全流程。确认后:等正在执行的制造轮结束 → 下载+SHA-256 校验 →
    /// 阻断新的调度触发 → 启动静默安装(装完自动重启)→ 退出本程序。</summary>
    private async Task PromptAndUpdateAsync(UpdateInfo info)
    {
        var window = App.MainWindowRef;
        if (window is null || _dialogOpen) return;

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        var dialog = new ContentDialog
        {
            Title = $"发现新版本 {info.TagName}",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = $"当前版本 v{info.Current.ToString(3)},最新版本 v{info.Latest.ToString(3)}。\n" +
                               "将下载官方安装包并校验 SHA-256,校验通过后静默安装并自动重启助手。\n" +
                               "制造计划、正在计时的制造任务与设置都保存在本机数据目录,更新不受影响。",
                    },
                    progress, status,
                },
            },
            PrimaryButtonText = "立即更新",
            CloseButtonText = "暂不",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = window.Content.XamlRoot,
        };

        // 下载/安装启动后禁止用「暂不」中途关闭:否则弹窗关了但后台仍会装完并重启,与用户意图相悖。
        dialog.CloseButtonClick += (d, args) => { if (_installing) args.Cancel = true; };

        dialog.PrimaryButtonClick += async (d, args) =>
        {
            args.Cancel = true; // 更新过程留在本窗口内呈现,成功路径以退出程序收尾
            d.IsPrimaryButtonEnabled = false;
            _installing = true;
            // 先阻断一切新触发(自动+手动),再等在途轮次结束:否则下载/安装期间新一轮开工,
            // 安装的 taskkill 会把它拦腰截断。运行阻断可逆,更新失败时解除。
            try
            {
                status.Text = "正在等待当前制造任务结束…";
                await _host.Coordinator.BlockNewRunsAndWaitAsync(CancellationToken.None);
                status.Text = "正在下载安装包…";
                progress.Visibility = Visibility.Visible;
                var reporter = new Progress<double>(p => progress.Value = p * 100);
                string setup = await Task.Run(() => _updates.DownloadVerifiedSetupAsync(
                    info, _host.Paths.UpdatesDir, reporter, CancellationToken.None));
                status.Text = "校验通过,正在启动安装并退出…";
                _log.Information("更新安装包已就绪:{Setup},启动安装并退出。", setup);
                // 先启动安装再退出:LaunchInstaller 抛错时不退出、不解除封锁前落入 catch。
                _updates.LaunchInstaller(setup);
                window.RequestExit();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "更新失败。");
                _installing = false; // 失败后放行「暂不」并允许重试
                _host.Coordinator.UnblockRuns(); // 解除封锁,失败的更新不该停掉挂机
                progress.Visibility = Visibility.Collapsed;
                status.Text = $"更新失败:{ex.Message}";
                d.IsPrimaryButtonEnabled = true;
            }
        };

        _dialogOpen = true;
        try { await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
    }
}
