namespace DeltaCrafter.Core.L0;

/// <summary>
/// 物品目录条目。Name 用于界面显示(可手工改成正确写法);
/// Ocr 存扫描时的识别原文,是运行期匹配键——空则退回用 Name 匹配。
/// </summary>
public sealed class CatalogItem
{
    public string Name { get; set; } = "";
    public string Ocr { get; set; } = "";
    public string? Note { get; set; }

    /// <summary>运行期在游戏列表里搜索用的名称。</summary>
    public string MatchKey => Ocr.Length > 0 ? Ocr : Name;
}

/// <summary>
/// 可制造物品目录(items.json,键为设施 kebab 名)。仅用于制造计划页下拉候选,
/// 不参与运行判定;允许用户直接填目录之外的名称。
/// </summary>
public sealed class ItemCatalog
{
    /// <summary>默认表修订号。程序启动时若默认表比本地副本新,自动备份并替换本地副本。</summary>
    public int Revision { get; set; }
    public Dictionary<string, List<CatalogItem>> Facilities { get; set; } = [];

    public IReadOnlyList<CatalogItem> For(FacilityKey key) =>
        Facilities.TryGetValue(FacilityKeys.JsonKey(key), out var list) ? list : [];
}

/// <summary>
/// 槽位 OCR 物品名 → 目录规范显示名(纯函数)。判定阶梯:
/// ① 规范形(TextMatch)完全相等 → 命中;
/// ② 惟一最近邻:编辑距离 ≤ max(1, 规范长度÷3),且比第二名近至少 2 → 命中。
///    容差取 1/3 是因为中文 OCR 常把单字拆成偏旁两字(实测「激」读成「氵敫」,距离 2);
///    「近至少 2」的间隔保证同族名(如 7.62×39mm AP / PS)之间绝不误认——分不清就不认。
/// ③ 其余返回 null,调用方保留原文——并列、太短、太烂都不猜。
/// </summary>
public static class CatalogNameResolver
{
    public static string? Resolve(IReadOnlyList<CatalogItem> items, string ocrName)
    {
        string target = TextMatch.Canonical(ocrName);
        if (target.Length < 3 || items.Count == 0) return null; // 过短读数做不了可靠判定

        string? bestName = null;
        int best = int.MaxValue, second = int.MaxValue;
        foreach (var item in items)
        {
            int d = Levenshtein(TextMatch.Canonical(item.Name), target);
            if (item.Ocr.Length > 0 && item.Ocr != item.Name)
                d = Math.Min(d, Levenshtein(TextMatch.Canonical(item.Ocr), target));
            if (d < best) { second = best; best = d; bestName = item.Name; }
            else if (d < second) { second = d; }
        }
        if (best == 0) return bestName;
        int tolerance = Math.Max(1, target.Length / 3);
        bool uniqueEnough = second == int.MaxValue || second - best >= 2;
        return best <= tolerance && uniqueEnough ? bestName : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1),
                                  prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
