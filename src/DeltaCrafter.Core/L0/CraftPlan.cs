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
/// 单个设施的制造物品选择方式。利润模式由 kkrb.net 推荐自动填充,
/// 自定义模式允许用户在计划页手选物品。
/// </summary>
public enum CraftMode
{
    Custom,
    HourlyProfit,
    TotalProfit,
}

/// <summary>
/// 单个设施的制造计划。ItemName 为显示名;MatchName 为运行期匹配名
/// (从目录选中时写入该条目的 OCR 原文),空则直接用 ItemName 匹配;
/// CustomItemName/CustomMatchName 独立保留最后一次自定义选择,利润推荐不得改写。
/// </summary>
public sealed class FacilityPlan
{
    public FacilityKey Key { get; set; }
    public bool Enabled { get; set; }
    public CraftMode Mode { get; set; }
    public string ItemName { get; set; } = "";
    public string MatchName { get; set; } = "";
    public string CustomItemName { get; set; } = "";
    public string CustomMatchName { get; set; } = "";

    /// <summary>运行期在游戏列表里搜索用的名称。</summary>
    public string SearchName => MatchName.Length > 0 ? MatchName : ItemName;

    public void SetCustomSelection(string itemName, string matchName)
    {
        if (Mode != CraftMode.Custom)
            throw new InvalidOperationException("只有自定义模式可以修改自定义物品。");

        ItemName = itemName;
        MatchName = matchName;
        CustomItemName = itemName;
        CustomMatchName = matchName;
    }

    /// <summary>离开自定义时封存选择;返回自定义时原子恢复显示名与 OCR 匹配名。</summary>
    public void ChangeMode(CraftMode mode)
    {
        if (Mode == mode) return;
        if (Mode == CraftMode.Custom)
        {
            CustomItemName = ItemName;
            CustomMatchName = MatchName;
        }

        Mode = mode;
        if (mode == CraftMode.Custom)
        {
            ItemName = CustomItemName;
            MatchName = CustomMatchName;
        }
    }
}

/// <summary>制造计划(持久化于 plan.json)。四个设施各一条,顺序即处理顺序。</summary>
public sealed class CraftPlanConfig
{
    public const int CurrentSchemaVersion = 3;

    /// <summary>缺失代表 v0.3.x 及更早计划;默认计划由工厂显式写入当前版本。</summary>
    public int SchemaVersion { get; set; }

    public List<FacilityPlan> Facilities { get; set; } = [];

    public FacilityPlan For(FacilityKey key) =>
        Facilities.FirstOrDefault(f => f.Key == key)
        ?? throw new InvalidOperationException($"制造计划缺少设施 {key},plan.json 已损坏或被手改出错。");

    public static CraftPlanConfig CreateDefault() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Facilities = FacilityKeys.All.Select(k => new FacilityPlan { Key = k }).ToList(),
    };

    /// <summary>
    /// 建立与全局计划完全隔离的执行快照。一轮开始后只消费该副本,
    /// 后台推荐或用户编辑不会让同一轮混用新旧物品与 OCR 匹配名。
    /// </summary>
    public CraftPlanConfig CreateExecutionSnapshot() => new()
    {
        SchemaVersion = SchemaVersion,
        Facilities = Facilities.Select(f => new FacilityPlan
        {
            Key = f.Key,
            Enabled = f.Enabled,
            Mode = f.Mode,
            ItemName = f.ItemName,
            MatchName = f.MatchName,
            CustomItemName = f.CustomItemName,
            CustomMatchName = f.CustomMatchName,
        }).ToList(),
    };
}
