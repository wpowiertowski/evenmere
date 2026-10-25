using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Population;

/// <summary>M1-38: a tier with an advanceFrom tier holds no more than the lower tier's capacity while any basic need is unmet.</summary>
public sealed class PopulationOccupancyCapTests
{
    private static (NeedsHarness Harness, EntityId House) AdvancedHarness()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Stock("timber", Amount.FromUnits(10));
        h.UnlockTier("villagers");
        h.RunDays(6);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        return (h, house);
    }

    private static void MeetAllVillagerNeeds(NeedsHarness h, EntityId house)
    {
        h.Cover(house, ServiceKind.Market).Cover(house, ServiceKind.Shrine);
        h.Stock("fish", Amount.FromUnits(100)).Stock("homespun", Amount.FromUnits(100)).Stock("bread", Amount.FromUnits(100));
    }

    [Fact]
    public void Occupancy_AllNeedsMet_FillsToFullCapacity()
    {
        (NeedsHarness h, EntityId house) = AdvancedHarness();
        MeetAllVillagerNeeds(h, house);

        h.RunDays(6);

        HouseInfo info = h.House(house);
        info.TargetResidents.ShouldBe(10);
        info.Residents.ShouldBe(10);
    }

    [Fact]
    public void Occupancy_OneNeedUnmet_CapsAtLowerTierCapacityNotProportional()
    {
        (NeedsHarness h, EntityId house) = AdvancedHarness();
        MeetAllVillagerNeeds(h, house);
        h.RunDays(5);
        h.House(house).Residents.ShouldBe(10);

        // Lose homespun only: 4 of 5 needs met, so the proportional target is 10 × 4 ÷ 5 = 8,
        // but a Villagers house with a basic need unmet holds no more than a Settlers house (6).
        h.Stock("homespun", Amount.Zero);
        h.RunDays(6);

        HouseInfo info = h.House(house);
        info.TargetResidents.ShouldBe(6);
        info.Residents.ShouldBe(6);
    }

    [Fact]
    public void Occupancy_TwoNeedsUnmet_TakesTheLowerOfProportionalAndCap()
    {
        (NeedsHarness h, EntityId house) = AdvancedHarness();
        MeetAllVillagerNeeds(h, house);
        h.RunDays(5);
        h.House(house).Residents.ShouldBe(10);

        // Lose homespun and bread: 3 of 5 met, so the proportional target is 10 × 3 ÷ 5 = 6, which equals the cap.
        h.Stock("homespun", Amount.Zero).Stock("bread", Amount.Zero);
        h.RunDays(6);

        h.House(house).TargetResidents.ShouldBe(6);

        // Lose fish too: market and the shrine are the 2 met, so the proportional target is 10 × 2 ÷ 5 = 4, below the cap.
        h.Stock("fish", Amount.Zero);
        h.RunDays(6);

        HouseInfo info = h.House(house);
        info.TargetResidents.ShouldBe(4);
        info.Residents.ShouldBe(4);
    }

    [Fact]
    public void Occupancy_SettlersUnchangedAndLinear()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.Zero);
        h.RunDays(6);

        // Settlers have no advanceFrom tier: 2 of 3 needs met stays linear at 6 × 2 ÷ 3 = 4.
        HouseInfo info = h.House(house);
        info.TargetResidents.ShouldBe(4);
        info.Residents.ShouldBe(4);
    }

    [Fact]
    public void Occupancy_CapFollowsCapacityModifiers()
    {
        (NeedsHarness h, EntityId house) = AdvancedHarness();
        MeetAllVillagerNeeds(h, house);
        h.Sim.Queries.Progression.AddModifier(
            Stat.Capacity,
            new ModifierContribution(new EffectSource(EffectSourceKind.Tech, 1), 100, true));
        h.RunDays(1);

        // +100% capacity doubles both tiers: villagers 20, settlers 12.
        h.House(house).Capacity.ShouldBe(20);

        h.Stock("homespun", Amount.Zero);
        h.RunDays(1);

        // Proportional is 20 × 4 ÷ 5 = 16, capped at the lower tier's scaled 12.
        h.House(house).TargetResidents.ShouldBe(12);
    }

    [Fact]
    public void Occupancy_JustAdvancedHouse_KeepsSixResidentsWithThreeOfFiveNeedsMet()
    {
        (NeedsHarness h, EntityId house) = AdvancedHarness();

        // A just-advanced house with the shrine and two goods: 3 of 5 met.
        // Proportional is 10 × 3 ÷ 5 = 6, so advancing never empties it.
        h.Cover(house, ServiceKind.Shrine);
        h.Stock("fish", Amount.FromUnits(100)).Stock("bread", Amount.FromUnits(100));
        h.Stock("homespun", Amount.Zero);
        h.RunDays(6);

        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("villagers"));
        info.TargetResidents.ShouldBe(6);
        info.Residents.ShouldBe(6);
    }

    [Fact]
    public void Occupancy_SameSeedsAgree()
    {
        static int Run()
        {
            (NeedsHarness h, EntityId house) = AdvancedHarness();
            MeetAllVillagerNeeds(h, house);
            h.RunDays(3);
            h.Stock("homespun", Amount.Zero);
            h.RunDays(3);
            return h.House(house).Residents;
        }

        Run().ShouldBe(Run());
    }
}
