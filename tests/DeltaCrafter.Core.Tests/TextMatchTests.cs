using DeltaCrafter.Core.L0;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class TextMatchTests
{
    [Theory]
    [InlineData("7.62×39mm AP", "7毛2x39mmAP")]
    [InlineData("7.62×51mm BPZ", "7`62×51mmBPZ")]
    [InlineData("OE2战斗兴奋剂", "0E2战斗兴奋剂")]
    public void Canonical_folds_observed_ocr_variants(string expected, string observed)
    {
        Assert.Equal(TextMatch.Canonical(expected), TextMatch.Canonical(observed));
    }

    [Fact]
    public void Substring_distance_ignores_price_and_badge_text()
    {
        int distance = TextMatch.SubstringDistance("1250 7.62×39mm AP 已解锁", "7.62×39mm AP");

        Assert.Equal(0, distance);
    }

    [Fact]
    public void Similar_ammunition_variants_remain_distinguishable()
    {
        int distance = TextMatch.SubstringDistance("7.62×39mm BP", "7.62×39mm AP");

        Assert.Equal(1, distance);
    }
}
