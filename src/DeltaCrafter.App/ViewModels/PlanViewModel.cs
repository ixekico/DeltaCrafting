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

    public string Name { get; }
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>物品是否允许手选。利润优先模式下为 false(物品由推荐自动填充),
    /// 模式切换时 PlanViewModel 重建模型,不做单模型热切换。</summary>
    public bool ItemEditable { get; }

    public PlanFacilityModel(FacilityPlan plan, IReadOnlyList<string> suggestions,
        Func<string, string?> resolveMatchName, Action save, bool itemEditable)
    {
        _plan = plan;
        _save = save;
        _resolveMatchName = resolveMatchName;
        Name = FacilityKeys.DisplayName(plan.Key);
        Suggestions = suggestions;
        ItemEditable = itemEditable;
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
            if (_plan.ItemName == (value ?? "")) return;
            _plan.ItemName = value ?? "";
            // 显示名与匹配名分离:目录里能对上的,写入其 OCR 匹配键;手填目录外名称则留空。
            _plan.MatchName = _resolveMatchName(_plan.ItemName) ?? "";
            OnPropertyChanged();
            _save();
        }
    }

    public string Note
    {
        get => _plan.Note;
        set { if (_plan.Note == (value ?? "")) return; _plan.Note = value ?? ""; OnPropertyChanged(); _save(); }
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

/// <summary>制造计划页:全局循环/补齐开关 + 四个设施的物品配置。</summary>
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

    /// <summary>按当前目录重建设施行(「扫描配方目录」合并完成后调用,下拉候选即时更新;
    /// 制造模式切换后也经此刷新物品显示与可编辑态)。</summary>
    public void RebuildFromCatalog()
    {
        bool itemEditable = _host.Settings.CraftMode == CraftMode.Custom;
        Facilities.Clear();
        foreach (var key in DisplayOrder)
            Facilities.Add(CreateModel(key, itemEditable));
    }

    /// <summary>利润推荐替换了部分设施的物品后,只重建受影响的卡片:未受影响的卡片
    /// 保留输入状态(如正在编辑的备注),不因后台自动填充被整页重建打断。</summary>
    public void RefreshFacilities(IReadOnlyCollection<FacilityKey> keys)
    {
        bool itemEditable = _host.Settings.CraftMode == CraftMode.Custom;
        for (int i = 0; i < DisplayOrder.Length && i < Facilities.Count; i++)
            if (keys.Contains(DisplayOrder[i]))
                Facilities[i] = CreateModel(DisplayOrder[i], itemEditable);
    }

    private PlanFacilityModel CreateModel(FacilityKey key, bool itemEditable) =>
        new(_host.Plan.For(key), _host.CatalogNamesFor(key),
            display => _host.ResolveCatalogMatchKey(key, display), _host.SavePlan, itemEditable);

    /// <summary>制造模式(自定义/利润优先)是否处于利润优先,控制横幅显隐与物品锁定。</summary>
    public bool IsProfitMode => _host.Settings.CraftMode != CraftMode.Custom;

    public string ProfitBannerTitle => _host.Settings.CraftMode switch
    {
        CraftMode.HourlyProfit => "制造模式:每小时利润优先",
        CraftMode.TotalProfit => "制造模式:总利润优先",
        _ => "",
    };

    public string ProfitBannerMessage
    {
        get
        {
            const string baseText = "四个设施的物品按 kkrb.net「特勤处制作产物推荐」自动填充,每 2 小时更新;此模式下不可手选物品,启用开关与备注仍可修改。";
            string status = _host.ProfitPlan.LastStatus;
            return status.Length > 0 ? baseText + "\n" + status : baseText;
        }
    }

    /// <summary>设置页切换制造模式后调用:刷新横幅并按新模式重建设施行(锁定/解锁物品)。</summary>
    public void NotifyCraftModeChanged()
    {
        OnPropertyChanged(nameof(IsProfitMode));
        OnPropertyChanged(nameof(ProfitBannerTitle));
        OnPropertyChanged(nameof(ProfitBannerMessage));
        RebuildFromCatalog();
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
