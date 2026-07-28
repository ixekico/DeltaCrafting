using System.Text;
using System.Text.RegularExpressions;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 游戏剩余时间文本解析。支持两类格式:
/// 1) 冒号计时 "HH:MM:SS"(含全角冒号;分/秒域校验 0-59,避免把杂散数字拼成时间);
/// 2) 带「剩余时间」标签的三数字组(OCR 允许把冒号读成顿号或空格);
/// 3) 中文计时 "X天X小时X分钟X秒"(至少出现一个单位)。
/// 解析前折算常见 OCR 形近误读(O→0、l/I/|→1、S→5、B→8、Z→2)。
/// 解析失败返回 false,由调用方判为步骤失败——绝不猜一个默认时长。
/// </summary>
public static partial class CountdownParser
{
    private static readonly Regex ColonPattern =
        new(@"(\d{1,3})\s*:\s*([0-5]?\d)\s*:\s*([0-5]?\d)", RegexOptions.Compiled);

    // 2560×1440 实测「07:59:52」会被读成「剩 余 时 间 ： 07 、 59 52」。
    // 只有标签完整、恰好三个合法数字组且组间仅含已观察到的分隔符时才采信;
    // 不能把任意三个界面数字宽泛拼成倒计时。
    private static readonly Regex LabelledSeparatedDigitsPattern = new(
        @"剩[ \t]*余[ \t]*时[ \t]*间[ \t]*[:、]?[ \t]*" +
        @"(\d{1,3})[ \t:、,.;·]+([0-5]?\d)[ \t:、,.;·]+([0-5]?\d)" +
        @"(?![ \t:、,.;·]*\d)",
        RegexOptions.Compiled);

    private static readonly Regex CjkPattern = new(
        @"(?:(\d{1,3})\s*天)?\s*(?:(\d{1,3})\s*(?:小时|时))?\s*(?:(\d{1,3})\s*分(?:钟)?)?\s*(?:(\d{1,3})\s*秒)?",
        RegexOptions.Compiled);

    public static bool TryParse(string? ocrText, out TimeSpan remaining)
    {
        remaining = default;
        if (string.IsNullOrWhiteSpace(ocrText)) return false;
        string folded = FoldDigitLookalikes(ocrText);

        var m = ColonPattern.Match(folded);
        if (m.Success)
        {
            remaining = new TimeSpan(
                int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value));
            return true;
        }

        m = LabelledSeparatedDigitsPattern.Match(folded);
        if (m.Success)
        {
            remaining = new TimeSpan(
                int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value));
            return true;
        }

        foreach (Match cm in CjkPattern.Matches(folded))
        {
            if (cm.Length == 0) continue; // 全可选分组会匹配空串,跳过
            int days = GroupValue(cm, 1);
            int hours = GroupValue(cm, 2);
            int minutes = GroupValue(cm, 3);
            int seconds = GroupValue(cm, 4);
            if (days + hours + minutes + seconds == 0 &&
                !(cm.Groups[1].Success || cm.Groups[2].Success || cm.Groups[3].Success || cm.Groups[4].Success))
                continue;
            remaining = new TimeSpan(days, hours, minutes, seconds);
            return true;
        }
        return false;
    }

    private static int GroupValue(Match m, int index) =>
        m.Groups[index].Success ? int.Parse(m.Groups[index].Value) : 0;

    private static string FoldDigitLookalikes(string s)
    {
        // 「:0」两个字符常被 OCR 合并误读成摄氏度符号(实测 04:03:05 → 「04：03℃5」、
        // 「04℃3℃6」):先做字符串级还原,再逐字符折叠。℃ 不会出现在物品名里,无误伤面。
        s = s.Replace("℃", ":0");
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                'O' or 'o' or 'О' or 'о' => '0', // 含西里尔 O
                '口' or '囗' or '〇' or '○' => '0', // 中文 OCR 把 0 误读成口形字(实测「04」读作「口4」)
                'l' or 'I' or '|' => '1',
                'S' or 's' => '5',
                'B' => '8',
                'Z' or 'z' => '2',
                '：' => ':',
                '，' => ':', // 实测冒号被误读成全角逗号(11:31:48 → 「11：31，48」)
                _ => c,
            });
        }
        return sb.ToString();
    }
}
