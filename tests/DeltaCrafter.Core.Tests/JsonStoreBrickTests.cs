using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class JsonStoreBrickTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deltacrafter-tests-" + Guid.NewGuid());
    private readonly JsonStoreBrick _store = new();

    public JsonStoreBrickTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Roundtrips_settings_with_enum_as_string()
    {
        string path = Path.Combine(_dir, "settings.json");
        var original = new AppSettings { GamePath = @"D:\Game\delta.exe", AfterRun = AfterRunAction.KeepRunning };
        _store.Save(path, original);

        Assert.Contains("KeepRunning", File.ReadAllText(path)); // 枚举必须存字符串,手改不出魔法数字
        var loaded = _store.Load<AppSettings>(path);
        Assert.Equal(original.GamePath, loaded.GamePath);
        Assert.Equal(AfterRunAction.KeepRunning, loaded.AfterRun);
    }

    [Fact]
    public void LoadOrCreate_writes_default_only_when_missing()
    {
        string path = Path.Combine(_dir, "plan.json");
        var created = _store.LoadOrCreate(path, CraftPlanConfig.CreateDefault);
        Assert.Equal(4, created.Facilities.Count);
        Assert.True(File.Exists(path));

        created.For(FacilityKey.Workbench).ItemName = "自定义";
        _store.Save(path, created);
        var reloaded = _store.LoadOrCreate(path, CraftPlanConfig.CreateDefault);
        Assert.Equal("自定义", reloaded.For(FacilityKey.Workbench).ItemName); // 已存在文件绝不被默认值覆盖
    }

    [Fact]
    public void Corrupted_file_throws_with_path_in_message()
    {
        string path = Path.Combine(_dir, "broken.json");
        File.WriteAllText(path, "{ not valid json !!");
        var ex = Assert.Throws<InvalidDataException>(() => _store.Load<AppSettings>(path));
        Assert.Contains(path, ex.Message); // 报错必须指出是哪个文件坏了
    }

    [Fact]
    public void Anchor_table_json_keys_match_code_constants()
    {
        // anchors.json 与 AnchorKeys 常量的契约:默认模板必须含全部界面/点位/区域键。
        string source = Path.Combine(AppContext.BaseDirectory, "Data", "anchors.json");
        var table = _store.Load<AnchorTable>(source);
        Assert.True(table.Calibrated); // 已按 2560×1440 实机截图标定出厂

        Assert.NotNull(table.Screen(AnchorKeys.ModeSelect).Point(AnchorKeys.PointModeEntry));
        Assert.NotNull(table.Screen(AnchorKeys.Safehouse));
        Assert.NotNull(table.Screen(AnchorKeys.Lobby).Point(AnchorKeys.PointSpecOpsEntry));
        Assert.NotNull(table.Screen(AnchorKeys.CollectResult).Point(AnchorKeys.PointDismiss));
        Assert.NotNull(table.Screen(AnchorKeys.ReplenishPopup).Point(AnchorKeys.PointBuy));

        var specOps = table.Screen(AnchorKeys.SpecOpsHome);
        foreach (var key in FacilityKeys.All)
        {
            Assert.NotNull(specOps.Point(AnchorKeys.FacilitySlot(key)));
            Assert.NotNull(specOps.Roi(AnchorKeys.FacilitySlot(key)));
        }

        var production = table.Screen(AnchorKeys.Production);
        Assert.NotNull(production.Point(AnchorKeys.PointActionButton));
        Assert.NotNull(production.Roi(AnchorKeys.RoiListArea));
        Assert.NotNull(production.Roi(AnchorKeys.RoiDetailTitle));
        Assert.NotNull(production.Roi(AnchorKeys.RoiActionButton));
        Assert.NotNull(production.Roi(AnchorKeys.RoiRemainingTime));

        // 中止确认弹窗:已按实机截图标定,confirm 点应为非 0(0,0 表示未校准会被程序拒点)。
        var confirm = table.Screen(AnchorKeys.AbortConfirm).Point(AnchorKeys.PointConfirm);
        Assert.True(confirm.X > 0 && confirm.Y > 0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
    }
}
