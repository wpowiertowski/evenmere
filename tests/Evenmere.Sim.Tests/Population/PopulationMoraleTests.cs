using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Population;

/// <summary>ADR-0016: underemployed residents set their house's morale, which blocks advancement; the hold also counts planned demand and a reserve.</summary>
public sealed class PopulationMoraleTests
{
    private static (NeedsHarness Harness, EntityId First, EntityId Second, EntityId Third) AdvancingHarness()
    {
        var h = NeedsHarness.Create();
        EntityId first = h.AddHouse();
        EntityId second = h.AddHouse();
        EntityId third = h.AddHouse();
        foreach (EntityId house in new[] { first, second, third })
        {
            h.Cover(house, ServiceKind.Market);
        }

        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.Stock("timber", Amount.FromUnits(10));
        h.UnlockTier("villagers");
        return (h, first, second, third);
    }

    [Fact]
    public void Rules_SliceContent_AdvancesAfterSixDaysWithATwentyPercentReserve()
    {
        var h = NeedsHarness.Create();

        h.Content.Rules.AdvanceStreakDays.ShouldBe(6);
        h.Content.Rules.WorkforceReservePercent.ShouldBe(20);
    }

    [Fact]
    public void Morale_UnderemploymentDistributesInAscendingId_FirstHousesCarryIt()
    {
        (NeedsHarness h, EntityId first, EntityId second, EntityId third) = AdvancingHarness();

        // Eight villagers working settler jobs, assigned as the houses become villagers on day six.
        h.Sim.Queries.Workforce.SetWorkingBelow(h.Tier("villagers"), 8);
        h.RunDays(6);

        h.House(first).Tier.ShouldBe(h.Tier("villagers"));
        h.House(second).Tier.ShouldBe(h.Tier("villagers"));
        h.House(third).Tier.ShouldBe(h.Tier("villagers"));

        // Six residents each: the first house carries six, the second two, the third none.
        h.House(first).MoralePerMille.ShouldBe(0);
        h.House(second).MoralePerMille.ShouldBe(666);
        h.House(third).MoralePerMille.ShouldBe(1000);
    }

    // The blocker tests run on settler houses with stubbed underemployment: in the slice villagers are the top
    // tier, so a villager house always reports NoNextTier before morale could block it. The distribution,
    // morale and blocker logic is tier-agnostic; the stub stands in for a middle tier's real numbers.
    [Fact]
    public void Morale_UnderemployedHouse_IsBlockedUntilSubstitutesAreNoLongerNeeded()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.Stock("timber", Amount.FromUnits(10));
        h.UnlockTier("villagers");
        h.Sim.Queries.Workforce.SetWorkingBelow(h.Tier("settlers"), 4);
        h.RunDays(6);
        h.RunDays(1);

        // Four of six residents work below their tier: morale is 1000 × 2 ÷ 6, and the house waits.
        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("settlers"));
        info.Residents.ShouldBe(6);
        info.MoralePerMille.ShouldBe(333);
        info.AdvanceBlocker.ShouldBe(AdvanceBlocker.Underemployed);

        // Once nobody works below the tier, morale is full and the house advances: the next bucket tick still
        // sees the old assignment, the day start after it clears it, and the following bucket tick advances.
        h.Sim.Queries.Workforce.SetWorkingBelow(h.Tier("settlers"), 0);
        h.RunDays(2);

        HouseInfo recovered = h.House(house);
        recovered.Tier.ShouldBe(h.Tier("villagers"));
        recovered.MoralePerMille.ShouldBe(1000);
    }

    [Fact]
    public void Morale_UnderemployedOutranksTheHold()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.Stock("timber", Amount.FromUnits(10));
        h.UnlockTier("villagers");
        h.Sim.Queries.Workforce.SetWorkingBelow(h.Tier("settlers"), 1);
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 100);
        h.RunDays(6);
        h.RunDays(1);

        // The demand alone would hold the house, but underemployment reports first.
        h.House(house).AdvanceBlocker.ShouldBe(AdvanceBlocker.Underemployed);
    }

    [Fact]
    public void Morale_MissingMaterialsOutrankUnderemployment()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.UnlockTier("villagers");
        h.Sim.Queries.Workforce.SetWorkingBelow(h.Tier("settlers"), 6);
        h.RunDays(6);
        h.RunDays(1);

        // No timber in the pool: materials report before morale does.
        h.House(house).AdvanceBlocker.ShouldBe(AdvanceBlocker.MissingMaterials);
    }

    [Fact]
    public void Hold_ReserveHoldsWhatDemandAloneWouldRelease()
    {
        var h = NeedsHarness.Create();
        EntityId first = h.AddHouse();
        EntityId second = h.AddHouse();
        h.Cover(first, ServiceKind.Market).Cover(second, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.Stock("timber", Amount.FromUnits(10));
        h.UnlockTier("villagers");

        // Six jobs and no sites: the need is six, the reserve is ceil(20% × 6) = 2, so eight workers must stay.
        // Twelve minus the house's six leaves six, which demand alone would accept.
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 6);
        h.RunDays(6);

        // Both houses hold with their streaks kept.
        h.House(first).Tier.ShouldBe(h.Tier("settlers"));
        h.House(first).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
        h.House(first).AdvanceStreakDays.ShouldBe(6);
        h.House(second).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
    }

    [Fact]
    public void Hold_PlannedDemandCountsSiteJobs()
    {
        var h = NeedsHarness.Create();
        EntityId first = h.AddHouse();
        EntityId second = h.AddHouse();
        h.Cover(first, ServiceKind.Market).Cover(second, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.Stock("timber", Amount.FromUnits(10));
        h.UnlockTier("villagers");

        // Two current jobs plus eight at construction sites: the need is ten, the reserve two, so twelve must stay.
        // Demand alone would release the first house (12 − 6 = 6 ≥ 2).
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 2);
        h.Sim.Queries.Workforce.SetPlannedDemand(h.Tier("settlers"), 8);
        h.RunDays(6);

        h.House(first).Tier.ShouldBe(h.Tier("settlers"));
        h.House(first).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
        h.House(second).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
    }

    [Fact]
    public void Hold_NoJobsAtAll_NothingIsHeld()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.Stock("timber", Amount.FromUnits(10));
        h.UnlockTier("villagers");

        // No current jobs and no sites: nothing is held, planned or otherwise.
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 0);
        h.Sim.Queries.Workforce.SetPlannedDemand(h.Tier("settlers"), 0);
        h.RunDays(6);

        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
    }
}
