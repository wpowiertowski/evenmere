using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Tests.Advisors;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Population;

/// <summary>ADR-0014: a house holds its advance while its tier's workers are needed, and advances once there are spare workers.</summary>
public sealed class PopulationWorkforceHoldTests
{
    private static (NeedsHarness Harness, EntityId House) ReadyHarnessWithHouse(int timberUnits = 10)
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(1000)).Stock("wood", Amount.FromUnits(1000));
        h.Stock("timber", Amount.FromUnits(timberUnits));
        h.UnlockTier("villagers");
        return (h, house);
    }

    private static int LiveTotal(NeedsHarness h, string tier) => h.Population.LiveTierTotal(h.Tier(tier));

    private static int SumResidents(NeedsHarness h, IEnumerable<EntityId> houses, TierId tier)
    {
        int total = 0;
        foreach (EntityId house in houses)
        {
            HouseInfo info = h.House(house);
            if (info.Tier == tier)
            {
                total += info.Residents;
            }
        }

        return total;
    }

    private static void TotalsShouldMatchHouses(NeedsHarness h, IEnumerable<EntityId> houses)
    {
        foreach (string tier in new[] { "settlers", "villagers" })
        {
            LiveTotal(h, tier).ShouldBe(SumResidents(h, houses, h.Tier(tier)));
        }
    }

    [Fact]
    public void Hold_AdvancingWouldDropSupplyBelowDemand_HoldsWithStreakKept()
    {
        (NeedsHarness h, EntityId house) = ReadyHarnessWithHouse();
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 6);

        h.RunDays(6);

        // Six residents, alone: leaving would drop the tier to zero workers against six jobs.
        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("settlers"));
        info.Residents.ShouldBe(6);
        info.AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
        info.AdvanceStreakDays.ShouldBe(h.Content.Rules.AdvanceStreakDays);
        TotalsShouldMatchHouses(h, [house]);
    }

    [Fact]
    public void Hold_SpareForExactlyOneHouse_FirstEvaluatedAdvancesAndOtherHolds()
    {
        (NeedsHarness h, EntityId first) = ReadyHarnessWithHouse();
        EntityId second = h.AddHouse();
        h.Cover(second, ServiceKind.Market);

        // Both houses grow to six residents while a prohibitive demand holds them.
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 100);
        h.RunDays(6);
        h.House(first).Residents.ShouldBe(6);
        h.House(second).Residents.ShouldBe(6);
        h.House(first).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
        h.House(second).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);

        // Spare covers exactly one house: five jobs plus a reserve of one still leave 12 − 6 = 6 workers behind.
        // The first evaluated advances, the other stays held with its streak kept.
        // Buckets run in ascending (id % 120), so with consecutive IDs the lowest ID is evaluated first.
        (first.Value % 120).ShouldBeLessThan(second.Value % 120);
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 5);
        h.RunDays(1);

        h.House(first).Tier.ShouldBe(h.Tier("villagers"));
        h.House(second).Tier.ShouldBe(h.Tier("settlers"));
        h.House(second).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
        h.House(second).AdvanceStreakDays.ShouldBe(h.Content.Rules.AdvanceStreakDays);
        TotalsShouldMatchHouses(h, [first, second]);
    }

    [Fact]
    public void Hold_NewHouseOfTierFills_ReleasesTheHeldHouse()
    {
        (NeedsHarness h, EntityId first) = ReadyHarnessWithHouse();
        EntityId second = h.AddHouse();
        h.Cover(second, ServiceKind.Market);
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 100);
        h.RunDays(6);

        // Five jobs plus a reserve of one: 12 − 6 = 6 workers left behind still cover them.
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 5);
        h.RunDays(1);
        h.House(first).Tier.ShouldBe(h.Tier("villagers"));
        h.House(second).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);

        // A third house of the tier fills while the demand stays prohibitive, then releases the second house.
        // (Without the prohibitive demand the smaller newcomer would see spare for itself first and leapfrog.)
        EntityId third = h.AddHouse();
        h.Cover(third, ServiceKind.Market);
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 100);
        h.RunDays(6);
        h.House(second).Tier.ShouldBe(h.Tier("settlers"));
        h.House(third).Residents.ShouldBe(6);
        TotalsShouldMatchHouses(h, [first, second, third]);
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 5);
        h.RunDays(1);

        h.House(second).Tier.ShouldBe(h.Tier("villagers"));
        h.House(third).Tier.ShouldBe(h.Tier("settlers"));
        h.House(third).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
        TotalsShouldMatchHouses(h, [first, second, third]);
    }

    [Fact]
    public void Hold_NoDemand_NothingIsHeld()
    {
        (NeedsHarness h, EntityId house) = ReadyHarnessWithHouse();
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 0);

        h.RunDays(6);

        // Demand is zero, so the house advances even though it leaves no workers behind.
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        TotalsShouldMatchHouses(h, [house]);
    }

    [Fact]
    public void Hold_LockedHouse_KeepsReportingUpgradeLocked()
    {
        (NeedsHarness h, EntityId house) = ReadyHarnessWithHouse();
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 6);
        h.Send(new SetHouseUpgradeLock(house, true));

        h.RunDays(5);

        // The lock outranks the hold: the house reports the lock even though it would be held anyway.
        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("settlers"));
        info.AdvanceBlocker.ShouldBe(AdvanceBlocker.UpgradeLocked);

        // Unlocking does not advance it: the hold engages instead.
        h.Send(new SetHouseUpgradeLock(house, false));
        h.RunDays(1);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
        h.House(house).AdvanceBlocker.ShouldBe(AdvanceBlocker.HeldForWorkforce);
        TotalsShouldMatchHouses(h, [house]);
    }

    [Fact]
    public void Hold_DecliningHouse_DeclinesDespiteUncoveredDemand()
    {
        (NeedsHarness h, EntityId house) = ReadyHarnessWithHouse();
        h.Cover(house, ServiceKind.Shrine);
        h.Stock("homespun", Amount.FromUnits(1000)).Stock("bread", Amount.FromUnits(1000));

        // Settle the house as a Villager first, with no demand so nothing is held.
        h.RunDays(6);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));

        // Villager needs are met for three days, so fish counts as ever met at this tier.
        h.RunDays(3);

        // Now the tier is short of workers, and fish runs out: the house cannot advance, but decline still runs.
        h.Sim.Queries.Workforce.SetDemand(h.Tier("villagers"), 100);
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 100);
        h.Stock("fish", Amount.Zero);
        h.RunDays(3);
        h.House(house).AdvanceBlocker.ShouldBe(AdvanceBlocker.NeedsUnmet);

        h.RunDays(7);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
        h.Events().OfType<HouseTierDeclined>().ShouldHaveSingleItem();
        TotalsShouldMatchHouses(h, [house]);
    }

    [Fact]
    public void Hold_HeldHouse_RaisesNoHouseCannotAdvanceAdvice()
    {
        // The advisors rule counts only MissingMaterials and NextTierLocked; a held house must stay silent.
        var h = AdvisorsHarness.Create();
        EntityId house = h.Add("settler_house", 0, 0);
        h.Sim.Queries.Roads.GrantRoadAccess(house);
        h.AddReachedStorehouse(house, 8, 8);
        TierId settlers = h.Tier("settlers");
        h.Sim.Queries.Population.SetHouse(new HouseInfo(house, settlers, 6, 6, 6, 3, 0, false, false, AdvanceBlocker.HeldForWorkforce, 1000));

        h.Run();

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();

        // The same house blocked on materials does raise, proving the harness sees it.
        h.Sim.Queries.Population.SetHouse(new HouseInfo(house, settlers, 6, 6, 6, 3, 0, false, false, AdvanceBlocker.MissingMaterials, 1000));
        h.AdvanceDays(1);
        h.Active().ShouldHaveSingleItem().Kind.ShouldBe(AdviceKind.HouseCannotAdvance);
    }

    [Fact]
    public void LiveTotals_AlwaysEqualTheSumOverHouses_IncludingAfterSaveRoundTrip()
    {
        (NeedsHarness h, EntityId first) = ReadyHarnessWithHouse();
        h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 6);
        EntityId second = h.AddHouse();
        h.Cover(second, ServiceKind.Market);

        h.RunDays(6);
        TotalsShouldMatchHouses(h, [first, second]);

        // A removal and a decline-sized move keep the totals exact.
        h.Sim.Services.BuildingRegistry.Remove(second).ShouldBeTrue();
        TotalsShouldMatchHouses(h, [first]);

        // The serialised bytes carry the totals: restoring them gives the same per-tier counts.
        byte[] bytes = StateSerializer.Write(h.Sim.World);
        WorldState restored = StateSerializer.Read(bytes, h.Content);
        restored.Population.TierResidents.ShouldBe(h.Sim.World.Population.TierResidents);
        var restoredPopulation = new PopulationSystem(restored.Population, h.Content);
        restoredPopulation.LiveTierTotal(h.Tier("settlers")).ShouldBe(SumResidents(h, [first], h.Tier("settlers")));
    }

    [Fact]
    public void Hold_SameScriptedDays_AreDeterministic()
    {
        static byte[] RunScript()
        {
            (NeedsHarness h, EntityId first) = ReadyHarnessWithHouse();
            h.Sim.Queries.Workforce.SetDemand(h.Tier("settlers"), 6);
            EntityId second = h.AddHouse();
            h.Cover(second, ServiceKind.Market);
            h.RunDays(7);
            return StateSerializer.Write(h.Sim.World);
        }

        RunScript().ShouldBe(RunScript());
    }
}
