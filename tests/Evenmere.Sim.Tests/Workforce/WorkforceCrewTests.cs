using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Workforce;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Workforce;

/// <summary>
/// The <c>crew</c> stat (ADR-0015) and the planned demand of in-place upgrades (M2-30): demand, fill and allocation see
/// the effective crew, and an upgrade site counts its new staffing for the new tier while the old building keeps working.
/// </summary>
public sealed class WorkforceCrewTests
{
    [Theory]
    [InlineData(4, 0, 4)]
    [InlineData(4, -25, 3)]
    [InlineData(10, 20, 12)]
    [InlineData(3, -33, 2)]
    [InlineData(1, 50, 2)]
    [InlineData(1, -90, 1)]
    [InlineData(4, -100, 1)]
    [InlineData(3, -200, 1)]
    public void EffectiveCrew_Rounding_MatchesAdr0015(int count, int percent, int expected)
    {
        WorkforceMath.EffectiveCrew(count, percent).ShouldBe(expected);
    }

    [Fact]
    public void Demand_UsesCrewReducedCount()
    {
        var h = WorkforceHarness.Create(WorldBuilder.Content);
        EntityId sawpit = h.Add("sawpit", 0, 0);

        // The sawpit asks for 3 settlers; a −34% crew modifier leaves 2.
        h.Queries.Demand(h.Tier("settlers")).ShouldBe(3);
        h.AddCrewModifier("sawpit", -34);

        h.Queries.Demand(h.Tier("settlers")).ShouldBe(2);
        sawpit.ShouldNotBe(EntityId.None);
    }

    [Fact]
    public void FillOf_ReflectsCrewReduction()
    {
        var h = WorkforceHarness.Create(WorldBuilder.Content);
        EntityId sawpit = h.Add("sawpit", 0, 0);
        h.AddCrewModifier("sawpit", -34);

        // Two settlers against an effective crew of 2 is fully staffed, not two thirds.
        h.SetSupply("settlers", 2);
        h.Allocate();

        h.Queries.FillOf(sawpit).PerMille.ShouldBe(1000);
    }

    [Fact]
    public void Allocate_AssignsOnlyTheEffectiveCrew()
    {
        var h = WorkforceHarness.Create(WorldBuilder.Content);
        EntityId first = h.Add("sawpit", 0, 0);
        EntityId second = h.Add("sawpit", 4, 0);
        h.AddCrewModifier("sawpit", -34);

        h.SetSupply("settlers", 10);
        h.Allocate();

        h.Allocated(first).ShouldBe(2);
        h.Allocated(second).ShouldBe(2);
    }

    [Fact]
    public void ListModifiers_ExplainsCrewReductionWithItsSource()
    {
        var h = WorkforceHarness.Create(WorldBuilder.Content);
        EntityId sawpit = h.Add("sawpit", 0, 0);
        h.AddCrewModifier("sawpit", -34);

        // The explanation path is the generic modifier list: the crew contribution names its source.
        BuildingTypeId sawpitType = h.Type("sawpit");
        var contributions = new List<ModifierContribution>();
        h.Sim.Queries.Progression.ListModifiers(
            Stat.Crew, ModifierContext.ForBuilding(sawpit, sawpitType), contributions);

        contributions.ShouldHaveSingleItem()
            .ShouldBe(new ModifierContribution(EffectSource.ForTech(new TechId(0)), -34, true));
    }

    [Fact]
    public void PlannedDemand_UpgradeSiteCountsTheNewStaffingWhileTheOldKeepsWorking()
    {
        var h = WorkforceHarness.Create(UpgradeContent.Db);
        EntityId store = h.Add("storehouse", 0, 0);
        BuildingTypeId upgraded = h.Type("test_upgraded_storehouse");

        // The finished storehouse asks for 2 settlers and plans nothing.
        h.Queries.Demand(h.Tier("settlers")).ShouldBe(2);
        h.Queries.PlannedDemand(h.Tier("villagers")).ShouldBe(0);

        // While the in-place upgrade builds, the old staffing keeps working and the site plans the new one.
        h.Sim.Queries.Construction.SetUpgradeTarget(store, upgraded);

        h.Queries.Demand(h.Tier("settlers")).ShouldBe(2);
        h.Queries.Demand(h.Tier("villagers")).ShouldBe(0);
        h.Queries.PlannedDemand(h.Tier("settlers")).ShouldBe(0);
        h.Queries.PlannedDemand(h.Tier("villagers")).ShouldBe(2);
    }

    [Fact]
    public void Upgrade_Completion_MovesDemandToTheNewTier()
    {
        var h = WorkforceHarness.Create(UpgradeContent.Db);
        EntityId store = h.Add("storehouse", 0, 0);
        BuildingTypeId upgraded = h.Type("test_upgraded_storehouse");
        h.Sim.Queries.Construction.SetUpgradeTarget(store, upgraded);

        // The upgrade completes: the building changes type in place and the site is gone.
        h.Sim.Services.BuildingRegistry.SetType(store, upgraded);
        h.Sim.Queries.Construction.ClearUpgradeTarget(store);

        h.Queries.Demand(h.Tier("settlers")).ShouldBe(0);
        h.Queries.Demand(h.Tier("villagers")).ShouldBe(2);
        h.Queries.PlannedDemand(h.Tier("settlers")).ShouldBe(0);
        h.Queries.PlannedDemand(h.Tier("villagers")).ShouldBe(0);
    }
}

/// <summary>
/// Adds a crew modifier for one building type to the fake progression queries.
/// </summary>
internal static class WorkforceCrewHarnessExtensions
{
    /// <summary>Adds an active <c>crew</c> modifier of the given percent for every building of the named type.</summary>
    /// <param name="harness">The workforce harness.</param>
    /// <param name="type">The building type's content ID.</param>
    /// <param name="percent">The modifier percent; negative reduces the crew.</param>
    public static void AddCrewModifier(this WorkforceHarness harness, string type, int percent)
    {
        ArgumentNullException.ThrowIfNull(harness);

        BuildingTypeId typeId = harness.Type(type);
        harness.Sim.Queries.Progression.AddModifier(
            Stat.Crew,
            new ModifierContribution(EffectSource.ForTech(new TechId(0)), percent, true),
            ctx => ctx.BuildingType == typeId);
    }
}
