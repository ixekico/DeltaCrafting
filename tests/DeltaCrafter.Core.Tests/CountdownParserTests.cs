using DeltaCrafter.Core.L1;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class CountdownParserTests
{
    [Theory]
    [InlineData("05:23:41", 5, 23, 41)]
    [InlineData("剩余时间 12:34:56", 12, 34, 56)]
    [InlineData("剩余 0:05:09", 0, 5, 9)]
    [InlineData("102:00:00", 102, 0, 0)] // 超过一天的长订单
    public void Parses_colon_format(string text, int h, int m, int s)
    {
        Assert.True(CountdownParser.TryParse(text, out var t));
        Assert.Equal(new TimeSpan(h, m, s), t);
    }

    [Theory]
    [InlineData("08：15：00", 8, 15, 0)]
    [InlineData("04：03℃8", 4, 3, 8)]
    [InlineData("11：31，48", 11, 31, 48)]
    [InlineData("剩 余 时 间 ： 07 、 59 52", 7, 59, 52)]
    public void Folds_observed_ocr_separators(string text, int h, int m, int s)
    {
        Assert.True(CountdownParser.TryParse(text, out var t));
        Assert.Equal(new TimeSpan(h, m, s), t);
    }

    [Fact]
    public void Folds_ocr_lookalike_digits()
    {
        // O→0、l→1、S→5:OCR 对游戏字体的常见误读。
        Assert.True(CountdownParser.TryParse("O2:l5:3O", out var t));
        Assert.Equal(new TimeSpan(2, 15, 30), t);
    }

    [Theory]
    [InlineData("1天2小时", 26, 0, 0)]
    [InlineData("2小时30分钟", 2, 30, 0)]
    [InlineData("30分钟", 0, 30, 0)]
    [InlineData("45秒", 0, 0, 45)]
    public void Parses_cjk_format(string text, int h, int m, int s)
    {
        Assert.True(CountdownParser.TryParse(text, out var t));
        Assert.Equal(new TimeSpan(h, m, s), t);
    }

    [Theory]
    [InlineData("07 、 59 52")]                 // 没有明确的剩余时间标签
    [InlineData("剩余时间 07 75 52")]           // 分钟越界
    [InlineData("剩余时间 07 59")]              // 数字组不足
    [InlineData("剩余时间 07 59 52 01")]        // 多出第四个数字组
    public void Rejects_ambiguous_space_separated_digits(string text)
    {
        Assert.False(CountdownParser.TryParse(text, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("材料不足")]
    [InlineData("abc")]
    [InlineData("05:75:00")] // 分钟越界不得拼成时间
    public void Rejects_non_countdown_text(string? text)
    {
        Assert.False(CountdownParser.TryParse(text, out _));
    }
}
