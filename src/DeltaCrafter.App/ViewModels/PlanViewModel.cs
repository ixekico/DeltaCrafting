using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeltaCrafter.App.Services;
using DeltaCrafter.Core.L0;

namespace DeltaCrafter.App.ViewModels;

/// <summary>单个设施的计划行。所有修改即时落盘(plan.json),无草稿态。</summary>
public sealed class PlanFacilityModel : ObservableObject
{
    private readonly FacilityPlan _plan;
    private readonly Action _save;
    private readonly Func<string, string?> _resolveMatchName;
    private readonly Action<FacilityKey, CraftMode> _modeChanged;

    public string Name { get; }
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>只有当前设施的自定义模式允许手选物品。</summary>
    public bool ItemEditable => _plan.Mode == CraftMode.Custom;

    public PlanFacilityModel(FacilityPlan plan, IReadOnlyList<string> suggestions,
        Func<string, string?> resolveMatchName, Action save,
        Action<FacilityKey, CraftMode> modeChanged)
    {
        _plan = plan;
        _save = save;
        _resolveMatchName = resolveMatchName;
        _modeChanged = modeChanged;
        Name = FacilityKeys.DisplayName(plan.Key);
        Suggestions = suggestions;
    }

    public bool Enabled
    {
        get => _plan.Enabled;
        set { if (_plan.Enabled == value) return; _plan.Enabled = value; OnPropertyChanged(); _save(); }
    }

    public string ItemName
    {
        get => _plan.ItemName;
        set
        {
            string itemName = value ?? "";
            // 显示名与匹配名分离:目录里能对上的,写入其 OCR 匹配键;手填目录外名称则留空。
            string matchName = _resolveMatchName(itemName) ?? "";
            if (_plan.ItemName == itemName
                && _plan.MatchName == matchName
                && _plan.CustomItemName == itemName
                && _plan.CustomMatchName == matchName)
                return;
            _plan.SetCustomSelection(itemName, matchName);
            OnPropertyChanged();
            _save();
        }
    }

    /// <summary>ComboBox 顺序与 CraftMode 枚举一一对应。</summary>
    public int ModeIndex
    {
        get => (int)_plan.Mode;
        set
        {
            // ComboBox 重建数据上下文时可能短暂报告 -1,这不是一个业务模式。
            if (value < 0) return;
            if (!Enum.IsDefined(typeof(CraftMode), value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "未知制造模式序号。");

            var mode = (CraftMode)value;
            if (_plan.Mode == mode) return;
            _plan.ChangeMode(mode);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ItemEditable));
            OnPropertyChanged(nameof(ItemName));
            _save();
            _modeChanged(_plan.Key, mode);
        }
    }

    /// <summary>搜索过滤候选:大小写无关、忽略空白、×/x 等价(输 AP 列出全部 AP 弹)。
    /// 空查询返回全部。不用 TextMatch.Canonical——其 I/l→1、O→0 折叠面向 OCR 双向比较,
    /// 对用户键入是不对称的(如输 rip 会配不上 RIP)。</summary>
    public IReadOnlyList<string> Filter(string? query)
    {
        string q = SearchKey(query ?? "");
        if (q.Length == 0) return Suggestions;
        return Suggestions.Where(s => SearchKey(s).Contains(q, StringComparison.Ordinal)).ToList();
    }

    private static string SearchKey(string s) =>
        string.Concat(s.Where(c => !char.IsWhiteSpace(c))
                       .Select(c => c == '×' ? 'X' : char.ToUpperInvariant(c)));
}

/// <summary>制造计划页:全局循环/补齐开关 + 四个设施各自的模式与物品配置。</summary>
public sealed partial class PlanViewModel : ObservableObject
{
    /// <summary>卡片显示顺序 = 游戏特勤处 2×2 排布(左上技术中心、右上工作台、
    /// 左下制药台、右下防具台)。仅影响本页 UI;运行处理顺序仍是 FacilityKeys.All。</summary>
    private static readonly FacilityKey[] DisplayOrder =
        [FacilityKey.TechCenter, FacilityKey.Workbench, FacilityKey.PharmacyLab, FacilityKey.ArmorStation];

    private readonly AppHost _host;

    public ObservableCollection<PlanFacilityModel> Facilities { get; } = [];

    public PlanViewModel(AppHost host)
    {
        _host = host;
        RebuildFromCatalog();
    }

    /// <summary>按当前目录重建设施行(「扫描配方目录」合并完成后调用,下拉候选即时更新)。</summary>
    public void RebuildFromCatalog()
    {
        Facilities.Clear();
        foreach (var key in DisplayOrder)
            Facilities.Add(CreateModel(key));
    }

    /// <summary>利润推荐替换了部分设施的物品后,只重建受影响的卡片:未受影响的卡片
    /// 保留搜索框输入状态,不因后台自动填充被整页重建打断。</summary>
    public void RefreshFacilities(IReadOnlyCollection<FacilityKey> keys)
    {
        for (int i = 0; i < DisplayOrder.Length && i < Facilities.Count; i++)
            if (keys.Contains(DisplayOrder[i]))
                Facilities[i] = CreateModel(DisplayOrder[i]);
    }

    private PlanFacilityModel CreateModel(FacilityKey key) =>
        new(_host.Plan.For(key), _host.CatalogNamesFor(key),
            display => _host.ResolveCatalogMatchKey(key, display), _host.SavePlan,
            OnFacilityModeChanged);

    /// <summary>至少一个设施启用利润模式时显示说明横幅。</summary>
    public bool IsProfitMode =>
        _host.Plan.Facilities.Any(f => f.Mode != CraftMode.Custom);

    public string ProfitBannerTitle => "设施利润推荐已启用";

    public string ProfitBannerMessage
    {
        get
        {
            const string baseText = "行情在应用启动后预热并于每个整点后台更新;选择利润模式时优先使用最近缓存,缓存为空则立即获取;自定义物品的设施仍可手选。";
            string status = _host.ProfitPlan.LastStatus;
            return status.Length > 0 ? baseText + "\n" + status : baseText;
        }
    }

    /// <summary>设施卡切换模式后更新横幅,并让利润服务只接管该设施。</summary>
    private void OnFacilityModeChanged(FacilityKey key, CraftMode mode)
    {
        // AutoSuggestBox 使用事件回填而非 Text 绑定;切回自定义后定点重建卡片,
        // 才能把恢复的物品名同步显示出来。
        if (mode == CraftMode.Custom)
            RefreshFacilities([key]);
        OnPropertyChanged(nameof(IsProfitMode));
        OnPropertyChanged(nameof(ProfitBannerTitle));
        OnPropertyChanged(nameof(ProfitBannerMessage));
        _host.ProfitPlan.OnFacilityModeChanged(key, mode);
    }

    /// <summary>利润推荐服务每次刷新(成功或失败)后调用,更新横幅里的最近结论。</summary>
    public void NotifyProfitStatusChanged() => OnPropertyChanged(nameof(ProfitBannerMessage));

    public bool AutoLoopEnabled
    {
        get => _host.Settings.AutoLoopEnabled;
        set
        {
            if (_host.Settings.AutoLoopEnabled == value) return;
            _host.Settings.AutoLoopEnabled = value;
            _host.SaveSettings();
            OnPropertyChanged();
            _host.Log.Information(value ? "自动循环已开启。" : "自动循环已关闭。");
        }
    }

    public double RunBufferSeconds
    {
        get => _host.Settings.RunBufferSeconds;
        set
        {
            if (double.IsNaN(value) || (int)value == _host.Settings.RunBufferSeconds) return;
            _host.Settings.RunBufferSeconds = Math.Clamp((int)value, 0, 3600);
            _host.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool AutoReplenishMaterials
    {
        get => _host.Settings.AutoReplenishMaterials;
        set
        {
            if (_host.Settings.AutoReplenishMaterials == value) return;
            _host.Settings.AutoReplenishMaterials = value;
            _host.SaveSettings();
            OnPropertyChanged();
            _host.Log.Information(value ? "已开启自动一键补齐材料。" : "已关闭自动补齐,缺料将标记需人工。");
        }
    }
}
