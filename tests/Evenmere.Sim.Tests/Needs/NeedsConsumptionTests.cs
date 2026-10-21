using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Needs;

public sealed class NeedsConsumptionTests
{
    [Fact]
    public void Consumption_FullSupply_TakesRateTimesResidentsAndCountsIt()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(10)).Stock("wood", Amount.FromUnits(10));

        // One resident takes 4 milli-units of fish and 3 of wood on its first day.
        h.RunTicks(1);

        h.Sim.Queries.Storage.Stock(h.Good("fish")).ShouldBe(Amount.FromUnits(10) - Amount.FromMilli(4));
        h.Sim.Queries.Storage.Stock(h.Good("wood")).ShouldBe(Amount.FromUnits(10) - Amount.FromMilli(3));
        h.Sim.Queries.Progression.Counter(CounterKey.Consumed(h.Good("fish"))).ShouldBe(4);
        h.Sim.Queries.Progression.Counter(CounterKey.Consumed(h.Good("wood"))).ShouldBe(3);
    }

    [Fact]
    public void Consumption_ShortSupply_TakesWhatIsThereAndLeavesThePoolAtZero()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);

        // One milli-unit of fish against a want of 4: the house takes it all and the need is unmet.
        h.Stock("fish", Amount.FromMilli(1)).Stock("wood", Amount.FromUnits(10));
        h.RunTicks(1);

        h.Sim.Queries.Storage.Stock(h.Good("fish")).ShouldBe(Amount.Zero);
        h.Sim.Queries.Progression.Counter(CounterKey.Consumed(h.Good("fish"))).ShouldBe(1);
        h.HouseNeeds(house).Needs.Single(n => n.Good == h.Good("fish")).Met.ShouldBeFalse();
    }

    [Fact]
    public void Needs_NoMarketCoverage_EveryGoodsNeedIsUnmetAndNothingIsTaken()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Stock("fish", Amount.FromUnits(10)).Stock("wood", Amount.FromUnits(10));

        h.RunTicks(1);

        HouseNeedsInfo needs = h.HouseNeeds(house);
        needs.HasMarketCoverage.ShouldBeFalse();
        foreach (NeedStatus need in needs.Needs.Where(static n => n.Class == NeedClass.Basic && n.Good.HasValue))
        {
            need.Met.ShouldBeFalse();
            need.Reason.ShouldBe(NeedUnmetReason.NoMarketCoverage);
        }

        h.Sim.Queries.Storage.Stock(h.Good("fish")).ShouldBe(Amount.FromUnits(10));
        h.Sim.Queries.Progression.Counter(CounterKey.Consumed(h.Good("fish"))).ShouldBe(0);
    }

    [Fact]
    public void Needs_ServiceNeeds_AreMetByCoverage()
    {
        var h = NeedsHarness.Create();
        EntityId covered = h.AddHouse();
        EntityId uncovered = h.AddHouse();
        h.Cover(covered, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(10)).Stock("wood", Amount.FromUnits(10));

        h.RunTicks(2);

        HouseNeedsInfo coveredNeeds = h.HouseNeeds(covered);
        coveredNeeds.Needs.Single(n => n.Service == ServiceKind.Market).Met.ShouldBeTrue();
        coveredNeeds.Needs.Single(n => n.Service == ServiceKind.Market).Fulfilment.ShouldBe(Ratio.One);
        coveredNeeds.BasicMet.ShouldBe(3);
        coveredNeeds.BasicTotal.ShouldBe(3);

        HouseNeedsInfo uncoveredNeeds = h.HouseNeeds(uncovered);
        NeedStatus market = uncoveredNeeds.Needs.Single(n => n.Service == ServiceKind.Market);
        market.Met.ShouldBeFalse();
        market.EverMet.ShouldBeFalse();
        market.Fulfilment.ShouldBe(Ratio.Zero);
        market.Reason.ShouldBe(NeedUnmetReason.ServiceNotCovered);
    }

    [Fact]
    public void Needs_LowFulfilment_ReportsLowFulfilmentReason()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.Zero).Stock("wood", Amount.FromUnits(10));

        h.RunTicks(1);

        NeedStatus fish = h.HouseNeeds(house).Needs.Single(n => n.Good == h.Good("fish"));
        fish.Met.ShouldBeFalse();
        fish.EverMet.ShouldBeFalse();
        fish.Reason.ShouldBe(NeedUnmetReason.LowFulfilment);
    }

    [Fact]
    public void Consumption_ConsumptionBonus_ScalesWhatTheHouseTakes()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(10)).Stock("wood", Amount.FromUnits(10));
        h.Sim.Queries.Progression.AddModifier(
            Stat.Consumption,
            new ModifierContribution(new EffectSource(EffectSourceKind.Tech, 1), 100, true));

        // +100% consumption doubles one resident's 4 milli-units of fish to 8.
        h.RunTicks(1);

        h.Sim.Queries.Storage.Stock(h.Good("fish")).ShouldBe(Amount.FromUnits(10) - Amount.FromMilli(8));
    }
}
