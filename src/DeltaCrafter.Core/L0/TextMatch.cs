using System.Text;

namespace DeltaCrafter.Core.L0;

/// <summary>
/// 文本匹配的规范形(纯函数)。规则:去空白与弱标点 → 同形字折叠(O/o→0,I/l→1)→ 转大写。
/// 折叠同时作用于比较双方,专治 OCR 对拉丁字符的 0/O、1/I/l 误读
/// (如「OE2战斗兴奋剂」被读成「0E2战斗兴奋剂」仍可对上)。
/// 弱标点(./`/·/引号)一律忽略:游戏字体的小数点常被读成 ` 等
/// (实测「7.62×51mm BPZ」读作「7`62×51mmBPZ」),忽略后仍可对上。
/// 仅用于比较与去重;显示文本永不经过此函数。
/// </summary>
public static class TextMatch
{
    public static string Canonical(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (c is '.' or '`' or '·' or '\'' or '’' or '‘' or '´' or ',' or '，') continue; // 弱标点(实测 . 被读成 ` 或 ，)
            char folded = c switch
            {
                'O' or 'o' or 'О' or 'о' => '0', // 含西里尔 O
                'I' or 'l' => '1',
                '×' => 'X', // 弹药口径写法:规范名用乘号,游戏/OCR 常用字母 x
                '毛' => '6', // 实测「.6」被 OCR 连体误读成「毛」(7.62 → 7毛2);目录无含「毛」物品
                _ => c,
            };
            sb.Append(char.ToUpperInvariant(folded));
        }
        return sb.ToString();
    }

    public static bool LineContains(string lineText, string target) =>
        Canonical(lineText).Contains(Canonical(target), StringComparison.Ordinal);

    /// <summary>子串近似匹配距离:needle 与 haystack 任意子串的最小编辑距离
    /// (半全局对齐,haystack 头尾多余字符免费;双方先取规范形)。
    /// 用于生产列表行内找物品名:行首价格/行尾角标不计罚,单点误读计 1
    /// (实测「7.62」会被读成「7毛2」或吞掉「7.」,规范形包含匹配对此无能为力)。</summary>
    public static int SubstringDistance(string haystack, string needle)
    {
        string h = Canonical(haystack), n = Canonical(needle);
        if (n.Length == 0) return 0;
        if (h.Length == 0) return n.Length;
        var prev = new int[h.Length + 1]; // 全 0:允许从 haystack 任意位置开始
        var cur = new int[h.Length + 1];
        for (int i = 1; i <= n.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= h.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1),
                                  prev[j - 1] + (n[i - 1] == h[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev.Min(); // 任意位置结束:尾部多余字符免费
    }
}
