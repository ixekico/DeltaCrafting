using DeltaCrafter.Core.L0;
using DeltaCrafter.Core.L1;
using Xunit;

namespace DeltaCrafter.Core.Tests;

public class CraftPlanTests
{
    [Fact]
    public void Execution_snapshot_is_isolated_from_later_plan_changes()
    {
        var plan = CraftPlanConfig.CreateDefault();
        var tech = plan.For(FacilityKey.TechCenter);
        tech.Enabled = true;
        tech.Mode = CraftMode.HourlyProfit;
        tech.ItemName = "旧推荐";
        tech.MatchName = "旧 OCR";
        tech.CustomItemName = "旧自定义";
        tech.CustomMatchName = "旧自定义 OCR";

        var snapshot = plan.CreateExecutionSnapshot();

        tech.Enabled = false;
        tech.Mode = CraftMode.TotalProfit;
        tech.ItemName = "新推荐";
        tech.MatchName = "新 OCR";
        tech.CustomItemName = "新自定义";
        tech.CustomMatchName = "新自定义 OCR";
        plan.Facilities.Clear();

        var captured = snapshot.For(FacilityKey.TechCenter);
        Assert.True(captured.Enabled);
        Assert.Equal(CraftMode.HourlyProfit, captured.Mode);
        Assert.Equal("旧推荐", captured.ItemName);
        Assert.Equal("旧 OCR", captured.MatchName);
        Assert.Equal("旧自定义", captured.CustomItemName);
        Assert.Equal("旧自定义 OCR", captured.CustomMatchName);
        Assert.Equal(4, snapshot.Facilities.Count);
    }

    [Fact]
    public void Returning_to_custom_restores_the_last_custom_item_and_match_name()
    {
        var facility = new FacilityPlan();
        facility.SetCustomSelection("感知激活针", "感知激活针 OCR");

        facility.ChangeMode(CraftMode.TotalProfit);
        facility.ItemName = "战地医疗箱";
        facility.MatchName = "战地医疗箱 OCR";
        facility.ChangeMode(CraftMode.Custom);

        Assert.Equal("感知激活针", facility.ItemName);
        Assert.Equal("感知激活针 OCR", facility.MatchName);
        Assert.Equal("感知激活针", facility.CustomItemName);
        Assert.Equal("感知激活针 OCR", facility.CustomMatchName);
    }

    [Fact]
    public void Profit_mode_changes_do_not_overwrite_the_saved_custom_item()
    {
        var facility = new FacilityPlan();
        facility.SetCustomSelection("感知激活针", "感知激活针 OCR");

        facility.ChangeMode(CraftMode.HourlyProfit);
        facility.ItemName = "第一份推荐";
        facility.ChangeMode(CraftMode.TotalProfit);
        facility.ItemName = "第二份推荐";

        Assert.Equal("感知激活针", facility.CustomItemName);
        Assert.Equal("感知激活针 OCR", facility.CustomMatchName);
    }

    [Fact]
    public void Legacy_global_mode_is_copied_to_every_facility_once()
    {
        var plan = CraftPlanConfig.CreateDefault();
        plan.SchemaVersion = 0;
        plan.For(FacilityKey.TechCenter).Mode = CraftMode.Custom;
        var settings = new AppSettings
        {
            LegacyGlobalCraftMode = CraftMode.TotalProfit,
        };

        var result = CraftPlanMigration.Upgrade(plan, settings);

        Assert.True(result.PlanChanged);
        Assert.True(result.SettingsChanged);
        Assert.Equal(CraftPlanConfig.CurrentSchemaVersion, plan.SchemaVersion);
        Assert.All(plan.Facilities, f => Assert.Equal(CraftMode.TotalProfit, f.Mode));
        Assert.Null(settings.LegacyGlobalCraftMode);
    }

    [Fact]
    public void Current_mixed_facility_modes_are_not_overwritten_by_stale_legacy_setting()
    {
        var plan = CraftPlanConfig.CreateDefault();
        plan.For(FacilityKey.TechCenter).Mode = CraftMode.HourlyProfit;
        plan.For(FacilityKey.Workbench).Mode = CraftMode.TotalProfit;
        var settings = new AppSettings
        {
            LegacyGlobalCraftMode = CraftMode.Custom,
        };

        var result = CraftPlanMigration.Upgrade(plan, settings);

        Assert.False(result.PlanChanged);
        Assert.True(result.SettingsChanged);
        Assert.Equal(CraftMode.HourlyProfit, plan.For(FacilityKey.TechCenter).Mode);
        Assert.Equal(CraftMode.TotalProfit, plan.For(FacilityKey.Workbench).Mode);
        Assert.Null(settings.LegacyGlobalCraftMode);
    }

    [Fact]
    public void Schema_two_plan_seeds_custom_memory_from_its_current_item()
    {
        var plan = CraftPlanConfig.CreateDefault();
        plan.SchemaVersion = 2;
        var pharmacy = plan.For(FacilityKey.PharmacyLab);
        pharmacy.Mode = CraftMode.TotalProfit;
        pharmacy.ItemName = "升级时现有物品";
        pharmacy.MatchName = "升级时 OCR";

        var result = CraftPlanMigration.Upgrade(plan, new AppSettings());

        Assert.True(result.PlanChanged);
        Assert.Equal(CraftPlanConfig.CurrentSchemaVersion, plan.SchemaVersion);
        Assert.Equal("升级时现有物品", pharmacy.CustomItemName);
        Assert.Equal("升级时 OCR", pharmacy.CustomMatchName);
    }

    [Fact]
    public void Plan_from_newer_app_is_rejected_instead_of_downgraded()
    {
        var plan = CraftPlanConfig.CreateDefault();
        plan.SchemaVersion = CraftPlanConfig.CurrentSchemaVersion + 1;

        Assert.Throws<InvalidDataException>(
            () => CraftPlanMigration.Upgrade(plan, new AppSettings()));
    }

    [Fact]
    public void Legacy_craft_mode_json_is_read_but_not_written_after_migration()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), $"DeltaCrafter-tests-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """{"craftMode":"HourlyProfit"}""");
            var store = new JsonStoreBrick();
            var settings = store.Load<AppSettings>(path);
            Assert.Equal(CraftMode.HourlyProfit, settings.LegacyGlobalCraftMode);

            CraftPlanMigration.Upgrade(CraftPlanConfig.CreateDefault(), settings);
            store.Save(path, settings);

            Assert.DoesNotContain("craftMode", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
