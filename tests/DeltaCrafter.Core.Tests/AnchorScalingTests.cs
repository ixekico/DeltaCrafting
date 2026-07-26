using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class AnchorScalingTests
{
    [Fact]
    public void Point_maps_by_client_ratio_with_screen_offset()
    {
        var p = new NPoint { X = 0.5, Y = 0.25 };
        var (x, y) = PixelMapper.ToPixel(p, left: 100, top: 50, width: 1920, height: 1080);
        Assert.Equal(100 + 960, x);
        Assert.Equal(50 + 270, y);
    }

    [Fact]
    public void Same_normalized_point_scales_across_resolutions()
    {
        var p = new NPoint { X = 0.9, Y = 0.9 };
        var (x1080, _) = PixelMapper.ToPixel(p, 0, 0, 1920, 1080);
        var (x1440, _) = PixelMapper.ToPixel(p, 0, 0, 2560, 1440);
        Assert.Equal(1728, x1080);
        Assert.Equal(2304, x1440);
    }

    [Fact]
    public void Roi_is_clamped_inside_frame()
    {
        var r = new NRect { X = 0.9, Y = 0.9, W = 0.5, H = 0.5 }; // 越界区域
        var (x, y, w, h) = PixelMapper.ToPixelRect(r, 1000, 500);
        Assert.Equal(900, x);
        Assert.Equal(450, y);
        Assert.Equal(100, w); // 被夹取到帧内
        Assert.Equal(50, h);
    }

    [Theory]
    [InlineData(1920, 1080, true)]
    [InlineData(2560, 1440, true)]
    [InlineData(1280, 720, true)]
    [InlineData(3440, 1440, false)] // 带鱼屏
    [InlineData(1920, 1200, false)] // 16:10
    public void Aspect_check_accepts_only_16_by_9(int w, int h, bool expected)
    {
        Assert.Equal(expected, GameWindowBrick.IsAspect16By9(new PixelRect(0, 0, w, h)));
    }
}
