using DeltaCrafter.Core.L0;
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
        tech.ItemName = "旧推荐";
        tech.MatchName = "旧 OCR";
        tech.Note = "保留备注";

        var snapshot = plan.CreateExecutionSnapshot();

        tech.Enabled = false;
        tech.ItemName = "新推荐";
        tech.MatchName = "新 OCR";
        tech.Note = "新备注";
        plan.Facilities.Clear();

        var captured = snapshot.For(FacilityKey.TechCenter);
        Assert.True(captured.Enabled);
        Assert.Equal("旧推荐", captured.ItemName);
        Assert.Equal("旧 OCR", captured.MatchName);
        Assert.Equal("保留备注", captured.Note);
        Assert.Equal(4, snapshot.Facilities.Count);
    }
}
