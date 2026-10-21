using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Needs;

public sealed class FairSharesTests
{
    [Fact]
    public void FairShares_NoHouses_FulfilmentIsFullAndDemandIsZero()
    {
        var h = NeedsHarness.Create();

        h.RunDays(2);

        h.Needs.Queries.Fulfilment(h.Good("fish")).ShouldBe(Ratio.One);
        h.Needs.Queries.DemandYesterday(h.Good("fish")).ShouldBe(Amount.Zero);
        h.Needs.Queries.Fulfilment(h.Good("bread")).ShouldBe(Ratio.One);
    }

    [Fact]
    public void FairShares_FullStock_FulfilmentIsFullAndDemandMatchesYesterday()
    {
        var h = NeedsHarness.Create();
        h.Stock("fish", Amount.FromUnits(10)).Stock("wood", Amount.FromUnits(10));
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);

        // One resident wanting 4 fish and 3 wood per 1,000 a day: 4 and 3 milli-units.
        // Checked after the first processing, before the next day start recomputes the shares.
        h.RunTicks(1);

        h.Needs.Queries.Fulfilment(h.Good("fish")).ShouldBe(Ratio.One);
        h.Needs.Queries.DemandYesterday(h.Good("fish")).ShouldBe(Amount.FromMilli(4));
        h.Needs.Queries.DemandYesterday(h.Good("wood")).ShouldBe(Amount.FromMilli(3));
    }

    [Fact]
    public void FairShares_HalfSupply_SplitsEquallyBetweenHousesRegardlessOfId()
    {
        var h = NeedsHarness.Create();
        EntityId first = h.AddHouse();
        EntityId second = h.AddHouse();
        h.Cover(first, ServiceKind.Market).Cover(second, ServiceKind.Market);

        // Two residents wanting 8 milli-units of fish a day; the pool holds exactly half.
        h.Stock("fish", Amount.FromMilli(4)).Stock("wood", Amount.FromUnits(10));

        // Both buckets fall on the first two ticks, which share the day-start shares computed on tick 1.
        h.RunTicks(2);

        // Both houses see the same share: half of what they wanted, which still counts as met.
        HouseNeedsInfo firstNeeds = h.HouseNeeds(first);
        HouseNeedsInfo secondNeeds = h.HouseNeeds(second);
        firstNeeds.Needs.Single(n => n.Good == h.Good("fish")).Fulfilment.ShouldBe(Ratio.FromPerMille(500));
        secondNeeds.Needs.Single(n => n.Good == h.Good("fish")).Fulfilment.ShouldBe(Ratio.FromPerMille(500));
        firstNeeds.Needs.Single(n => n.Good == h.Good("fish")).Met.ShouldBeTrue();
        secondNeeds.Needs.Single(n => n.Good == h.Good("fish")).Met.ShouldBeTrue();

        // Each house took its equal half: 2 milli-units of fish, and the pool is empty but never negative.
        h.Sim.Queries.Storage.Stock(h.Good("fish")).ShouldBe(Amount.Zero);
        h.Sim.Queries.Progression.Counter(CounterKey.Consumed(h.Good("fish"))).ShouldBe(4);
    }

    [Fact]
    public void FairShares_ProducedYesterday_CountsTowardsSupply()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);

        // No stock, but Production made yesterday's 4 milli-units of fish: the fulfilment counts it,
        // though the house can only draw from the pool, which is empty, so the need stays unmet.
        h.Stock("fish", Amount.FromMilli(2)).ProducedYesterday("fish", Amount.FromMilli(2));
        h.Stock("wood", Amount.FromUnits(10));
        h.RunTicks(1);

        h.Needs.Queries.Fulfilment(h.Good("fish")).ShouldBe(Ratio.One);
        HouseNeedsInfo needs = h.HouseNeeds(house);
        needs.Needs.Single(n => n.Good == h.Good("fish")).Met.ShouldBeTrue();
        h.Sim.Queries.Storage.Stock(h.Good("fish")).ShouldBe(Amount.Zero);
        h.Sim.Queries.Progression.Counter(CounterKey.Consumed(h.Good("fish"))).ShouldBe(2);
    }

    [Fact]
    public void FairShares_EmptyPoolAndNoProduction_FulfilmentIsZero()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.Zero).Stock("wood", Amount.FromUnits(10));

        h.RunDays(1);

        h.Needs.Queries.Fulfilment(h.Good("fish")).ShouldBe(Ratio.Zero);
        h.HouseNeeds(house).Needs.Single(n => n.Good == h.Good("fish")).Met.ShouldBeFalse();
    }

    [Fact]
    public void Fulfilment_ThresholdBoundary_MetAtHalfAndUnmetBelow()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("wood", Amount.FromUnits(10));

        // One resident's 4 milli-units of fish against 2 in the pool: fulfilment is exactly 500 per-mille, met.
        // Checked before the next day start recomputes the shares.
        h.Stock("fish", Amount.FromMilli(2));
        h.RunTicks(1);
        h.HouseNeeds(house).Needs.Single(n => n.Good == h.Good("fish")).Met.ShouldBeTrue();

        // Against 1 milli-unit the fulfilment is 250 per-mille: unmet.
        var h2 = NeedsHarness.Create();
        EntityId house2 = h2.AddHouse();
        h2.Cover(house2, ServiceKind.Market);
        h2.Stock("fish", Amount.FromMilli(1)).Stock("wood", Amount.FromUnits(10));
        h2.RunTicks(1);
        h2.HouseNeeds(house2).Needs.Single(n => n.Good == h2.Good("fish")).Met.ShouldBeFalse();
    }
}
