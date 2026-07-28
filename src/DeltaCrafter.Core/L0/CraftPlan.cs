namespace DeltaCrafter.Core.L0;

/// <summary>特勤处四类制造设施。枚举值同时作为锚点/数据文件中的稳定键,不得随意改名。</summary>
public enum FacilityKey
{
    Workbench,     // 工作台
    PharmacyLab,   // 制药台
    ArmorStation,  // 防具台
    TechCenter,    // 技术中心
}

public static class FacilityKeys
{
    public static readonly FacilityKey[] All =
        [FacilityKey.Workbench, FacilityKey.PharmacyLab, FacilityKey.ArmorStation, FacilityKey.TechCenter];

    /// <summary>界面显示名。以游戏内称呼为准,校准阶段如与游戏不一致须同步修改。</summary>
    public static string DisplayName(FacilityKey key) => key switch
    {
        FacilityKey.Workbench => "工作台",
        FacilityKey.PharmacyLab => "制药台",
        FacilityKey.ArmorStation => "防具台",
        FacilityKey.TechCenter => "技术中心",
        _ => key.ToString(),
    };

    /// <summary>anchors.json / items.json 中使用的 kebab 键。与枚举一一对应,两侧同步维护。</summary>
    public static string JsonKey(FacilityKey key) => key switch
    {
        FacilityKey.Workbench => "workbench",
        FacilityKey.PharmacyLab => "pharmacy-lab",
        FacilityKey.ArmorStation => "armor-station",
        FacilityKey.TechCenter => "tech-center",
        _ => key.ToString(),
    };
}

/// <summary>
/// 单个设施的制造计划。ItemName 为显示名;MatchName 为运行期匹配名
/// (从目录选中时写入该条目的 OCR 原文),空则直接用 ItemName 匹配。
/// </summary>
public sealed class FacilityPlan
{
    public FacilityKey Key { get; set; }
    public bool Enabled { get; set; }
    public string ItemName { get; set; } = "";
    public string MatchName { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>运行期在游戏列表里搜索用的名称。</summary>
    public string SearchName => MatchName.Length > 0 ? MatchName : ItemName;
}

/// <summary>制造计划(持久化于 plan.json)。四个设施各一条,顺序即处理顺序。</summary>
public sealed class CraftPlanConfig
{
    public List<FacilityPlan> Facilities { get; set; } = [];

    public FacilityPlan For(FacilityKey key) =>
        Facilities.FirstOrDefault(f => f.Key == key)
        ?? throw new InvalidOperationException($"制造计划缺少设施 {key},plan.json 已损坏或被手改出错。");

    public static CraftPlanConfig CreateDefault() => new()
    {
        Facilities = FacilityKeys.All.Select(k => new FacilityPlan { Key = k }).ToList(),
    };

    /// <summary>
    /// 建立与全局计划完全隔离的执行快照。一轮开始后只消费该副本,
    /// 后台推荐或用户编辑不会让同一轮混用新旧物品与 OCR 匹配名。
    /// </summary>
    public CraftPlanConfig CreateExecutionSnapshot() => new()
    {
        Facilities = Facilities.Select(f => new FacilityPlan
        {
            Key = f.Key,
            Enabled = f.Enabled,
            ItemName = f.ItemName,
            MatchName = f.MatchName,
            Note = f.Note,
        }).ToList(),
    };
}
