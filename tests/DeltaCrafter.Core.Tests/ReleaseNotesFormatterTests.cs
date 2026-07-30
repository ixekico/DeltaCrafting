using DeltaCrafter.Core.L1;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class ReleaseNotesFormatterTests
{
    [Fact]
    public void Converts_project_changelog_markdown_to_readable_text()
    {
        string markdown = """
            ### 新增

            - 更新窗口显示 **完整更新内容**，并支持
              跨行项目说明。

            ### 修复

            - 修复 [`标题`](https://example.test) 中的 `v0.3.1`。
            """;

        string text = ReleaseNotesFormatter.ToPlainText(markdown);

        Assert.Equal(
            "新增\n\n• 更新窗口显示 完整更新内容，并支持 跨行项目说明。\n\n" +
            "修复\n\n• 修复 标题 中的 v0.3.1。",
            text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Missing_release_notes_are_explicit()
    {
        Assert.Equal("该版本未提供更新日志。",
            ReleaseNotesFormatter.ToPlainText("  "));
    }
}
