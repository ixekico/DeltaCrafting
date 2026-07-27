using DeltaCrafter.App.ViewModels;
using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using DeltaCrafter.Core.L2;
using DeltaCrafter.Core.L3;
using Serilog;

namespace DeltaCrafter.App.Services;

/// <summary>
/// 组合根:装配 Core(L0-L3)与 UI 服务,持有全局单例。不用 DI 框架——
/// 对象图一眼可读,构造顺序即依赖顺序。必须在 UI 线程调用 Initialize()。
/// 兼任 ICatalogSink(配方扫描结果合并存盘并刷新计划页)、ICatalogLookup
/// (槽位 OCR 名解析回目录规范名,供总览与通知显示)与
/// IAppWindowGuard(执行期间最小化助手窗口,防止盖住游戏污染屏幕拷贝识别)。
/// </summary>
public sealed class AppHost : ICatalogSink, ICatalogLookup, IAppWindowGuard
{
    public static AppHost Current { get; private set; } = null!;

    public AppDataBrick Paths { get; }
    public JsonStoreBrick Store { get; }
    public AppSettings Settings { get; }
    public CraftPlanConfig Plan { get; }
    public ItemCatalog Catalog { get; }
    public UiLogSink UiSink { get; }
    public ILogger Log { get; }
    public GameWindowBrick WindowBrick { get; }
    public AutostartBrick Autostart { get; }
    public SleepGuardBrick SleepGuard { get; }
    public ToastNotifier Notifier { get; }
    public AutomationCoordinator Coordinator { get; }
    public UpdateService Updater { get; }
    public ProfitPlanService ProfitPlan { get; }

    public ShellViewModel ShellVm { get; }
    public OverviewViewModel OverviewVm { get; }
    public PlanViewModel PlanVm { get; }
    public LogViewModel LogVm { get; }
    public SettingsViewModel SettingsVm { get; private set; } = null!;

    private readonly CancellationTokenSource _appStop = new();
    private TrayService? _tray;
    private ThemeService? _theme;
    private MainWindow? _mainWindow;
    private bool _isShutdown;

    public static void Initialize() => Current = new AppHost();

    private AppHost()
    {
        Paths = new AppDataBrick();
        Paths.EnsureInitialized(Path.Combine(AppContext.BaseDirectory, "Data"));

        UiSink = new UiLogSink();
        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(Paths.LogsDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(UiSink)
            .CreateLogger();
        Log = Serilog.Log.Logger;
        Log.Information("===== DeltaCrafter 启动 v{Version} =====",
            typeof(AppHost).Assembly.GetName().Version);

        Store = new JsonStoreBrick();
        // 默认数据表带修订号:构建里的默认表比本地副本新时,备份并替换本地副本,
        // 免去“每次更新锚点都要手动删本地文件”的操作;用户手改内容保留在 .bak 里。
        UpgradeDataFileIfNewer<AnchorTable>("anchors.json", Paths.AnchorsPath, t => t.Revision);
        UpgradeDataFileIfNewer<ItemCatalog>("items.json", Paths.ItemsPath, t => t.Revision);
        Settings = Store.LoadOrCreate(Paths.SettingsPath, () => new AppSettings());
        Plan = Store.LoadOrCreate(Paths.PlanPath, CraftPlanConfig.CreateDefault);
        Catalog = Store.Load<ItemCatalog>(Paths.ItemsPath);

        var clock = new SystemClock();
        var ocr = OcrBrick.CreateSimplifiedChinese(); // 缺中文包在此抛出,由 App 弹窗给指引
        WindowBrick = new GameWindowBrick();
        var capture = new ScreenCaptureBrick();
        var input = new InputBrick();
        var process = new GameProcessBrick(Log);
        SleepGuard = new SleepGuardBrick(Log);
        Autostart = new AutostartBrick(Log);
        Notifier = new ToastNotifier(Log);

        var probe = new ScreenProbe(WindowBrick, capture, ocr, input, LoadAnchors, Paths.ShotsDir, Log);
        var runner = new StepRunner(probe, Log);
        var launch = new LaunchFlow(process, WindowBrick, probe, input, () => Settings, Log);
        var nav = new SpecOpsNavFlow(probe, runner);
        var collect = new CollectFlow(probe, runner, input, this, Log);
        var craft = new CraftStartFlow(probe, runner, input, Log);
        var abort = new AbortFlow(probe, runner, input, Log);
        var scan = new CatalogScanFlow(probe, runner, input, Log);
        var shutdown = new ShutdownFlow(process, WindowBrick, probe, runner, Log);
        var engine = new ScheduleEngine(Store, Paths.StatePath, clock, Log);

        Coordinator = new AutomationCoordinator(launch, nav, collect, craft, abort, scan, this,
            this, this, shutdown, engine, probe, WindowBrick, SleepGuard, () => Settings, () => Plan,
            Notifier, clock, Log);

        Updater = new UpdateService(this, new UpdateCoordinator(), Log);

        ShellVm = new ShellViewModel(Coordinator);
        OverviewVm = new OverviewViewModel(Coordinator, UiSink, this);
        PlanVm = new PlanViewModel(this);
        LogVm = new LogViewModel(this);
        // 服务在 VM 之后构造(应用推荐时要刷新 PlanVm),循环随调度循环一起启动。
        ProfitPlan = new ProfitPlanService(this, new ProfitPlanCoordinator(), Log);

        _ = Task.Run(() => Coordinator.RunSchedulerLoopAsync(_appStop.Token));
        _ = Task.Run(() => ProfitPlan.RunLoopAsync(_appStop.Token));
    }

    private void UpgradeDataFileIfNewer<T>(string fileName, string localPath, Func<T, int> revision)
    {
        string defaultPath = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
        int defaultRev = revision(Store.Load<T>(defaultPath));
        int localRev = revision(Store.Load<T>(localPath));
        if (defaultRev <= localRev) return;
        string backup = localPath + ".bak";
        File.Copy(localPath, backup, overwrite: true);
        File.Copy(defaultPath, localPath, overwrite: true);
        Log.Information("{File} 默认表更新(rev {Old} → {New}),已替换本地副本;原文件备份为 {Backup}。",
            fileName, localRev, defaultRev, backup);
    }

    private AnchorTable? _anchorsCache;
    private DateTime _anchorsMtime;
    private readonly object _anchorsGate = new();

    /// <summary>锚点表按文件修改时间热重载:校准时改完 anchors.json 即生效,无需重启。</summary>
    public AnchorTable LoadAnchors()
    {
        lock (_anchorsGate)
        {
            var mtime = File.GetLastWriteTimeUtc(Paths.AnchorsPath);
            if (_anchorsCache is null || mtime != _anchorsMtime)
            {
                _anchorsCache = Store.Load<AnchorTable>(Paths.AnchorsPath);
                _anchorsMtime = mtime;
                Log.Information("锚点表已加载(calibrated={Calibrated})。", _anchorsCache.Calibrated);
            }
            return _anchorsCache;
        }
    }

    private bool _restoreWindowAfterRun;

    /// <summary>IAppWindowGuard:执行期间最小化助手窗口(仅当此刻可见),结束后恢复。
    /// 可在一轮执行中被反复调用(观察每遍都会再压一次,防用户中途唤起窗口盖住游戏):
    /// 恢复意愿只置位不清零(闩锁),窗口已最小化时为无操作,结束时恢复到「曾可见」状态。</summary>
    public void MinimizeForRun()
    {
        var window = _mainWindow;
        window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (!window.AppWindow.IsVisible) return;
            _restoreWindowAfterRun = true;
            if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                p.Minimize();
        });
    }

    public void RestoreAfterRun()
    {
        var window = _mainWindow;
        window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_restoreWindowAfterRun) return;
            _restoreWindowAfterRun = false;
            if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                p.Restore();
        });
    }

    /// <summary>目录读写共用一把锁:合并写入在自动化线程,查询在 UI 线程与自动化线程
    /// 都有(计划页解析、利润推荐应用、槽位名归一化),防止枚举中变更导致崩溃。</summary>
    private readonly object _catalogGate = new();

    /// <summary>
    /// ICatalogSink:扫描结果合并入目录,存盘并刷新计划页下拉。
    /// 新条目 Name=Ocr=识别原文;用户可手工把 Name 改成正确写法(显示用),
    /// Ocr 保持识别原文作运行匹配键。去重用 TextMatch 规范形,改名后重扫不产生重复。
    /// </summary>
    public void MergeScanned(FacilityKey key, IReadOnlyList<string> names)
    {
        int added = 0, total;
        lock (_catalogGate)
        {
            string jsonKey = FacilityKeys.JsonKey(key);
            if (!Catalog.Facilities.TryGetValue(jsonKey, out var list))
            {
                list = [];
                Catalog.Facilities[jsonKey] = list;
            }
            // 旧版本写入的条目没有 ocr 字段:一次性物化 Ocr=Name(与"空则用 Name 匹配"语义等价),
            // 之后用户把 name 改成正确写法时,匹配键已固化在 ocr 里不受影响。
            foreach (var legacy in list.Where(i => i.Ocr.Length == 0))
                legacy.Ocr = legacy.Name;
            var known = list
                .SelectMany(i => new[] { TextMatch.Canonical(i.Name), TextMatch.Canonical(i.MatchKey) })
                .ToHashSet(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!known.Add(TextMatch.Canonical(name))) continue;
                list.Add(new CatalogItem { Name = name, Ocr = name });
                added++;
            }
            Store.Save(Paths.ItemsPath, Catalog);
            total = list.Count;
        }
        Log.Information("{Facility} 目录合并:新增 {Added} 个,共 {Total} 个。",
            FacilityKeys.DisplayName(key), added, total);
        _mainWindow?.DispatcherQueue.TryEnqueue(() => PlanVm.RebuildFromCatalog());
    }

    /// <summary>ICatalogLookup:槽位 OCR 名 → 目录规范名(解析规则见 CatalogNameResolver)。</summary>
    public string? ResolveDisplayName(FacilityKey key, string ocrName)
    {
        lock (_catalogGate)
            return CatalogNameResolver.Resolve(Catalog.For(key), ocrName);
    }

    /// <summary>显示名 → 目录条目的运行匹配键。规范形先比显示名,再比匹配键
    /// (用户可能改过显示名写法,匹配键仍是 OCR 原文);目录外名称返回 null,
    /// 计划照常保存并按显示名做游戏内匹配。</summary>
    public string? ResolveCatalogMatchKey(FacilityKey key, string displayName)
    {
        string canonical = TextMatch.Canonical(displayName);
        lock (_catalogGate)
            return Catalog.For(key).FirstOrDefault(i =>
                TextMatch.Canonical(i.Name) == canonical
                || TextMatch.Canonical(i.MatchKey) == canonical)?.MatchKey;
    }

    /// <summary>计划页下拉候选:目录显示名快照(锁内复制,调用方可自由枚举)。</summary>
    public IReadOnlyList<string> CatalogNamesFor(FacilityKey key)
    {
        lock (_catalogGate)
            return Catalog.For(key).Select(i => i.Name).ToList();
    }

    public void AttachMainWindow(MainWindow window)
    {
        _mainWindow = window;
        _theme = new ThemeService(window);
        _theme.Apply(Settings.Theme);
        SettingsVm = new SettingsViewModel(this, _theme);
        _tray = new TrayService(window,
            runNow: () => _ = Task.Run(() => Coordinator.RunOnceAsync("托盘触发", CancellationToken.None)),
            exit: window.RequestExit);
    }

    public void SaveSettings() => Store.Save(Paths.SettingsPath, Settings);

    public void SavePlan()
    {
        Store.Save(Paths.PlanPath, Plan);
        // 计划页无保存键(即改即存),在日志里亮出保存内容,让"存没存"可直接被看见。
        Log.Information("制造计划已保存:{Summary}", string.Join("; ",
            Plan.Facilities.Select(f =>
                $"{FacilityKeys.DisplayName(f.Key)}[{(f.Enabled ? "启用" : "停用")}]{(f.ItemName.Length > 0 ? " " + f.ItemName : " 未选物品")}")));
    }

    public void Shutdown()
    {
        if (_isShutdown) return;
        _isShutdown = true;
        Log.Information("应用退出。");
        _appStop.Cancel();
        _tray?.Dispose();
        SleepGuard.Dispose();
        Notifier.Unregister();
        Serilog.Log.CloseAndFlush();
    }
}
