using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Population;

public sealed class PopulationAdvanceTests
{
    private static (NeedsHarness Harness, EntityId House) ReadyHarness()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Stock("timber", Amount.FromUnits(2));
        h.UnlockTier("villagers");
        return (h, house);
    }

    [Fact]
    public void Advance_AllNeedsMetForSixDays_ConsumesCostAndChangesTier()
    {
        (NeedsHarness h, EntityId house) = ReadyHarness();

        // Five days are not enough: the streak is still growing.
        h.RunDays(5);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));

        h.RunDays(1);

        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("villagers"));
        info.AdvanceStreakDays.ShouldBe(0);
        info.DeclineTimerDays.ShouldBe(0);
        info.Residents.ShouldBe(6);
        info.Capacity.ShouldBe(10);
        h.Sim.Queries.Storage.Stock(h.Good("timber")).ShouldBe(Amount.Zero);

        HouseTierAdvanced advanced = h.Events().OfType<HouseTierAdvanced>().Single();
        advanced.House.ShouldBe(house);
        advanced.FromTier.ShouldBe(h.Tier("settlers"));
        advanced.ToTier.ShouldBe(h.Tier("villagers"));
    }

    [Fact]
    public void Advance_NextTierLocked_GrowsToCapacityButNeverAdvances()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Stock("timber", Amount.FromUnits(2));

        // The villagers tier is never unlocked: six days of met needs grow the house without advancing it.
        h.RunDays(6);

        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("settlers"));
        info.Residents.ShouldBe(6);
        info.AdvanceBlocker.ShouldBe(AdvanceBlocker.NextTierLocked);
        h.Events().OfType<HouseTierAdvanced>().ShouldBeEmpty();
    }

    [Fact]
    public void Advance_MissingMaterials_WaitsThenAdvancesWhenTheyArrive()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.UnlockTier("villagers");

        h.RunDays(4);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
        h.House(house).AdvanceBlocker.ShouldBe(AdvanceBlocker.MissingMaterials);

        h.Stock("timber", Amount.FromUnits(2));
        h.RunDays(2);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.Sim.Queries.Storage.Stock(h.Good("timber")).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Advance_LockedHouse_WaitsForTheUnlock()
    {
        (NeedsHarness h, EntityId house) = ReadyHarness();

        h.Send(new SetHouseUpgradeLock(house, true));
        h.Rejection().ShouldBeNull();
        h.House(house).UpgradeLocked.ShouldBeTrue();

        h.RunDays(5);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
        h.House(house).AdvanceBlocker.ShouldBe(AdvanceBlocker.UpgradeLocked);

        h.Send(new SetHouseUpgradeLock(house, false));
        h.Rejection().ShouldBeNull();
        h.RunDays(1);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
    }

    [Fact]
    public void Advance_TopTier_ReportsNoNextTier()
    {
        (NeedsHarness h, EntityId house) = ReadyHarness();

        // Advance to villagers first: its needs are met for three more days.
        h.RunDays(6);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.Cover(house, ServiceKind.Shrine);
        h.Stock("homespun", Amount.FromUnits(100)).Stock("bread", Amount.FromUnits(100));
        h.RunDays(3);

        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).AdvanceBlocker.ShouldBe(AdvanceBlocker.NoNextTier);
        h.Events().Count(e => e is HouseTierAdvanced).ShouldBe(1);
    }

    [Fact]
    public void SetHouseUpgradeLock_UnknownHouse_IsRejected()
    {
        var h = NeedsHarness.Create();

        h.Send(new SetHouseUpgradeLock(new EntityId(7000), true));

        h.Rejection().ShouldNotBeNull().Code.ShouldBe(RejectReason.UnknownTarget);
    }

    [Fact]
    public void SetHouseUpgradeLock_NonHouseBuilding_IsRejected()
    {
        var h = NeedsHarness.Create();
        EntityId storehouse = h.AddBuilding("storehouse");

        h.Send(new SetHouseUpgradeLock(storehouse, true));

        h.Rejection().ShouldNotBeNull().Code.ShouldBe(RejectReason.NotApplicable);
        h.Population.Queries.TryGetHouse(storehouse, out _).ShouldBeFalse();
    }
}
