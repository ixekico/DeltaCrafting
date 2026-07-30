using System.Text.RegularExpressions;

namespace DeltaCrafter.Core.L1;

/// <summary>
/// 把项目发布日志使用的 Markdown 子集转为更新窗口可读纯文本。
/// 只处理标题、项目符号、续行和内联标记；未知内容保留原文，不猜测或丢弃。
/// </summary>
public static partial class ReleaseNotesFormatter
{
    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "该版本未提供更新日志。";

        var output = new List<string>();
        foreach (string sourceLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = sourceLine.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                if (output.Count > 0 && output[^1].Length > 0) output.Add("");
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                string heading = CleanInline(trimmed.TrimStart('#').Trim());
                if (output.Count > 0 && output[^1].Length > 0) output.Add("");
                output.Add(heading);
            }
            else if (trimmed.StartsWith("- "))
            {
                output.Add("• " + CleanInline(trimmed[2..].Trim()));
            }
            else if (line.Length > trimmed.Length && output.Count > 0 && output[^1].StartsWith("• "))
            {
                output[^1] += " " + CleanInline(trimmed);
            }
            else
            {
                output.Add(CleanInline(trimmed));
            }
        }

        while (output.Count > 0 && output[^1].Length == 0) output.RemoveAt(output.Count - 1);
        return string.Join(Environment.NewLine, output);
    }

    private static string CleanInline(string value)
    {
        string text = MarkdownLink().Replace(value, "$1");
        return text.Replace("**", "").Replace("__", "").Replace("`", "");
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex MarkdownLink();
}
