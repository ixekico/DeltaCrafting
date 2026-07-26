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

    public PlanFacilityModel(FacilityPlan plan, IReadOnlyList<string> suggestions,
        Func<string, string?> resolveMatchName, Action save)
    {
        _plan = plan;
        _save = save;
        _resolveMatchName = resolveMatchName;
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
        foreach (var fp in _host.Plan.Facilities)
        {
            var key = fp.Key;
            var suggestions = _host.Catalog.For(key).Select(i => i.Name).ToList();
            Facilities.Add(new PlanFacilityModel(fp, suggestions,
                display => ResolveMatchName(key, display), _host.SavePlan));
        }
    }

    /// <summary>显示名 → 目录条目的运行匹配键(TextMatch 规范形对齐,抗同形字与空白差异)。</summary>
    private string? ResolveMatchName(FacilityKey key, string displayName)
    {
        string canonical = TextMatch.Canonical(displayName);
        var hit = _host.Catalog.For(key)
            .FirstOrDefault(i => TextMatch.Canonical(i.Name) == canonical);
        return hit?.MatchKey;
    }

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
