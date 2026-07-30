using DeltaCrafter.Core.L1;
using System.Text.Json;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class UpdateBrickTests
{
    [Theory]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("0.2.3", 0, 2, 3)]
    [InlineData("V1.0.0", 1, 0, 0)]
    [InlineData("v1.2.3-beta.1", 1, 2, 3)] // 预发布后缀忽略,仅取核心版本
    [InlineData("v10.20.30", 10, 20, 30)]
    public void Parses_release_tags(string tag, int major, int minor, int build)
    {
        var v = UpdateBrick.TryParseVersionTag(tag);
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("v1.2")]      // 两段不构成 x.y.z 发布号
    [InlineData("v1.2.3.4")]  // 四段装配体版本不是发布标签
    [InlineData("vv1.2.3")]   // 只允许一个可选 v 前缀
    [InlineData("1.2.x")]
    public void Rejects_malformed_tags(string tag) =>
        Assert.Null(UpdateBrick.TryParseVersionTag(tag));

    [Fact]
    public void Newer_tag_compares_greater_than_current_three_part()
    {
        // 装配体版本是四段(x.y.z.0),tag 是三段;归一后才可比较,否则 0.1.0.0 > 0.1.0 会误判。
        var current = UpdateBrick.Normalize(new Version(0, 1, 0, 0));
        Assert.True(UpdateBrick.TryParseVersionTag("v0.1.1") > current);
        Assert.False(UpdateBrick.TryParseVersionTag("v0.1.0") > current);
        Assert.False(UpdateBrick.TryParseVersionTag("v0.0.9") > current);
    }

    [Theory]
    [InlineData(
        "3b2f1a4c5d6e7f8091a2b3c4d5e6f7089a1b2c3d4e5f60718293a4b5c6d7e8f9  DeltaCrafter-Setup-0.2.0.exe",
        "3b2f1a4c5d6e7f8091a2b3c4d5e6f7089a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData(
        "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789 *file",
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")] // 大写归一化为小写
    public void Parses_sha256_checksum_line(string text, string expected) =>
        Assert.Equal(expected, UpdateBrick.ParseChecksumText(text));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash  file.exe")]
    [InlineData("3b2f1a4c  file.exe")]                         // 长度不足 64
    [InlineData("zzzz1a4c5d6e7f8091a2b3c4d5e6f7089a1b2c3d4e5f60718293a4b5c6d7e8f9  file")] // 含非十六进制
    public void Rejects_invalid_checksum_text(string text) =>
        Assert.Null(UpdateBrick.ParseChecksumText(text));

    [Fact]
    public void Release_assets_must_match_tag_exactly()
    {
        using var json = JsonDocument.Parse("""
        {
          "tag_name": "v0.2.0",
          "body": "### 新增\n\n- 自动更新显示更新日志。",
          "assets": [
            {
              "name": "DeltaCrafter-Setup-0.1.0.exe",
              "browser_download_url": "https://github.com/ixekico/DeltaCrafting/old.exe",
              "size": 10
            },
            {
              "name": "DeltaCrafter-Setup-0.2.0.exe.sha256",
              "browser_download_url": "https://github.com/ixekico/DeltaCrafting/new.sha256",
              "size": 96
            },
            {
              "name": "DeltaCrafter-Setup-0.2.0.exe",
              "browser_download_url": "https://github.com/ixekico/DeltaCrafting/new.exe",
              "size": 42
            },
            {
              "name": "DeltaCrafter-Setup-0.1.0.exe.sha256",
              "browser_download_url": "https://github.com/ixekico/DeltaCrafting/old.sha256",
              "size": 96
            }
          ]
        }
        """);

        var info = UpdateBrick.ParseRelease(json.RootElement, new Version(0, 1, 0, 0));

        Assert.True(info.IsNewer);
        Assert.Equal($"新增{Environment.NewLine}{Environment.NewLine}• 自动更新显示更新日志。",
            info.ReleaseNotes);
        Assert.Equal("DeltaCrafter-Setup-0.2.0.exe", info.SetupName);
        Assert.EndsWith("/new.exe", info.SetupUrl);
        Assert.EndsWith("/new.sha256", info.ChecksumUrl);
        Assert.Equal(42, info.SetupBytes);
    }

    [Fact]
    public void New_release_without_exact_checksum_is_rejected()
    {
        using var json = JsonDocument.Parse("""
        {
          "tag_name": "v0.2.0",
          "assets": [
            {
              "name": "DeltaCrafter-Setup-0.2.0.exe",
              "browser_download_url": "https://github.com/ixekico/DeltaCrafting/new.exe",
              "size": 42
            },
            {
              "name": "DeltaCrafter-Setup-0.1.0.exe.sha256",
              "browser_download_url": "https://github.com/ixekico/DeltaCrafting/old.sha256",
              "size": 96
            }
          ]
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() =>
            UpdateBrick.ParseRelease(json.RootElement, new Version(0, 1, 0)));

        Assert.Contains("精确对应", error.Message);
    }

    [Fact]
    public void Non_https_release_asset_is_rejected()
    {
        using var json = JsonDocument.Parse("""
        {
          "tag_name": "v0.2.0",
          "assets": [
            {
              "name": "DeltaCrafter-Setup-0.2.0.exe",
              "browser_download_url": "http://example.test/new.exe",
              "size": 42
            },
            {
              "name": "DeltaCrafter-Setup-0.2.0.exe.sha256",
              "browser_download_url": "https://example.test/new.sha256",
              "size": 96
            }
          ]
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() =>
            UpdateBrick.ParseRelease(json.RootElement, new Version(0, 1, 0)));

        Assert.Contains("非 HTTPS", error.Message);
    }
}
