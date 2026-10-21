using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Population;

public sealed class PopulationGrowthTests
{
    [Fact]
    public void House_CreatedAtLowestTier_WithOneResident()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();

        // The record exists from activation, before the house is ever processed.
        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("settlers"));
        info.Residents.ShouldBe(1);
        info.TargetResidents.ShouldBe(1);
        info.Capacity.ShouldBe(6);
        info.AdvanceBlocker.ShouldBe(AdvanceBlocker.NeedsUnmet);
        info.UpgradeLocked.ShouldBeFalse();
    }

    [Fact]
    public void House_RemovedBuilding_LosesItsRecord()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();

        h.Sim.Services.BuildingRegistry.Remove(house).ShouldBeTrue();

        h.Population.Queries.TryGetHouse(house, out _).ShouldBeFalse();
        h.Needs.Queries.TryGetHouseNeeds(house, out _).ShouldBeFalse();
        h.Population.Queries.Residents(h.Tier("settlers")).ShouldBe(0);
        h.Population.Queries.TotalResidents.ShouldBe(0);
    }

    [Fact]
    public void Occupancy_AllNeedsMet_GrowsOnePerDayToCapacity()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));

        // Six residents is the target; the house reports it while still growing.
        h.RunDays(2);
        h.House(house).Residents.ShouldBe(3);
        h.House(house).TargetResidents.ShouldBe(6);

        h.RunDays(3);
        h.House(house).Residents.ShouldBe(6);

        // At capacity the house stays put and emits no more move-ins.
        h.Events();
        h.RunDays(2);
        h.House(house).Residents.ShouldBe(6);
        h.Events().OfType<ResidentsMovedIn>().ShouldBeEmpty();
    }

    [Fact]
    public void Occupancy_EmitsMoveInsInDailyBatches()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));

        h.RunDays(1);

        ResidentsMovedIn movedIn = h.Events().OfType<ResidentsMovedIn>().Single();
        movedIn.House.ShouldBe(house);
        movedIn.Count.ShouldBe(1);
    }

    [Fact]
    public void Occupancy_PartialNeeds_HoldsGrowthBelowCapacity()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);

        // Two of three basic needs met (fish and market, no wood): the target is 6 × 2 ÷ 3 = 4.
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.Zero);
        h.RunDays(6);

        h.House(house).Residents.ShouldBe(4);
        h.House(house).TargetResidents.ShouldBe(4);
    }

    [Fact]
    public void Occupancy_LosingGoods_ShrinksTowardTheCoveredServicesTarget()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));

        h.RunDays(3);
        h.House(house).Residents.ShouldBe(4);
        h.Events();

        // Losing every goods need drops the target to 6 × 1 ÷ 3 = 2 for the still-covered market;
        // the house shrinks one resident a day, without move-in events.
        h.Stock("fish", Amount.Zero).Stock("wood", Amount.Zero);
        h.RunDays(3);

        h.House(house).Residents.ShouldBe(2);
        h.House(house).TargetResidents.ShouldBe(2);
        h.Events().OfType<ResidentsMovedIn>().ShouldBeEmpty();
    }

    [Fact]
    public void Occupancy_CapacityBonus_RaisesTheCeiling()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Sim.Queries.Progression.AddModifier(
            Stat.Capacity,
            new ModifierContribution(new EffectSource(EffectSourceKind.Tech, 1), 100, true));

        h.RunDays(1);

        h.House(house).Capacity.ShouldBe(12);
        h.House(house).TargetResidents.ShouldBe(12);
    }

    [Fact]
    public void Queries_UnknownHouse_ReturnsFalse()
    {
        var h = NeedsHarness.Create();

        h.Population.Queries.TryGetHouse(new EntityId(7000), out _).ShouldBeFalse();
        h.Population.Queries.TotalResidents.ShouldBe(0);
    }
}
