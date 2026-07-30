using DeltaCrafter.Core.L0;

namespace DeltaCrafter.Core.L1;

public readonly record struct CraftPlanMigrationResult(
    bool PlanChanged,
    bool SettingsChanged);

/// <summary>
/// 制造计划配置的单向版本迁移。迁移必须显式、幂等;不接受由更新版本写出的配置,
/// 避免旧程序以默认值覆盖它无法理解的新字段。
/// </summary>
public static class CraftPlanMigration
{
    public static CraftPlanMigrationResult Upgrade(
        CraftPlanConfig plan,
        AppSettings settings)
    {
        if (plan.SchemaVersion < 0
            || plan.SchemaVersion > CraftPlanConfig.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"不支持制造计划版本 {plan.SchemaVersion}，当前程序仅支持到 "
                + $"{CraftPlanConfig.CurrentSchemaVersion}。");
        }

        bool planChanged = false;
        if (plan.SchemaVersion < 2)
        {
            // v0.3.x 只有一个全局模式。升级时复制到每个设施,保持用户原有行为;
            // 之后各设施独立修改,不再保留双写兼容层。
            if (settings.LegacyGlobalCraftMode is { } legacyMode)
                foreach (var facility in plan.Facilities)
                    facility.Mode = legacyMode;
            planChanged = true;
        }

        if (plan.SchemaVersion < 3)
        {
            // 旧格式从未保存自定义历史,只能以升级当刻的现有物品作为初始记忆。
            // 之后利润推荐只改当前物品,不再覆盖这两个字段。
            foreach (var facility in plan.Facilities)
            {
                facility.CustomItemName = facility.ItemName;
                facility.CustomMatchName = facility.MatchName;
            }
            planChanged = true;
        }

        if (planChanged)
            plan.SchemaVersion = CraftPlanConfig.CurrentSchemaVersion;

        bool settingsChanged = settings.LegacyGlobalCraftMode is not null;
        settings.LegacyGlobalCraftMode = null;
        return new CraftPlanMigrationResult(planChanged, settingsChanged);
    }
}
