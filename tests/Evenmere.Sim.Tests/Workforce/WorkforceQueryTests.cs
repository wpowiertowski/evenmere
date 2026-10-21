using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Workforce;

public sealed class WorkforceQueryTests
{
    [Fact]
    public void Supply_ReflectsWhatPopulationSet()
    {
        var h = WorkforceHarness.Create();

        h.Queries.Supply(h.Tier("settlers")).ShouldBe(0);

        h.SetSupply("settlers", 7);

        h.Queries.Supply(h.Tier("settlers")).ShouldBe(7);
    }

    [Fact]
    public void Supply_WorkforceModifier_ScalesItPerTier()
    {
        var h = WorkforceHarness.Create();
        h.SetSupply("settlers", 10);
        h.SetSupply("villagers", 10);
        h.Sim.Queries.Progression.AddModifier(
            Stat.Workforce,
            new ModifierContribution(EffectSource.ForTech(new TechId(0)), 50, true),
            static ctx => ctx.Tier == new TierId(0));

        // Settlers is the first tier in string order, so the modifier applies to it and not to villagers.
        h.Tier("settlers").ShouldBe(new TierId(0));
        h.Queries.Supply(h.Tier("settlers")).ShouldBe(15);
        h.Queries.Supply(h.Tier("villagers")).ShouldBe(10);
    }

    [Fact]
    public void Supply_NegativeModifier_FloorsAtZero()
    {
        var h = WorkforceHarness.Create();
        h.SetSupply("settlers", 10);
        h.Sim.Queries.Progression.AddModifier(
            Stat.Workforce,
            new ModifierContribution(EffectSource.ForTech(new TechId(0)), -150, true));

        h.Queries.Supply(h.Tier("settlers")).ShouldBe(0);
    }

    [Fact]
    public void Demand_SumsActiveWorkplacesByTier()
    {
        var h = WorkforceHarness.Create();
        h.Add("test_workshop", 0, 0);
        h.Add("sawpit", 4, 0);
        h.Add("settler_house", 8, 0);

        // The workshop asks for 5 settlers, the sawpit for 3, and the house asks for nobody.
        h.Queries.Demand(h.Tier("settlers")).ShouldBe(8);
        h.Queries.Demand(h.Tier("villagers")).ShouldBe(0);
    }

    [Fact]
    public void FillOf_BuildingWithNoWorkforceSpec_CountsAsFullyStaffed()
    {
        var h = WorkforceHarness.Create();
        EntityId house = h.Add("settler_house", 0, 0);

        h.Allocate();

        h.Queries.FillOf(house).PerMille.ShouldBe(1000);
    }

    [Fact]
    public void FillOf_UnknownBuilding_GivesZero()
    {
        var h = WorkforceHarness.Create();

        h.Queries.FillOf(new EntityId(999)).PerMille.ShouldBe(0);
        h.Queries.IsHighPriority(new EntityId(999)).ShouldBeFalse();
    }

    [Fact]
    public void SetSupply_Negative_Throws()
    {
        var h = WorkforceHarness.Create();

        Should.Throw<ArgumentOutOfRangeException>(() => h.Supply.SetSupply(h.Tier("settlers"), -1));
    }
}
