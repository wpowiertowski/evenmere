using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Population;

public sealed class PopulationOutputsTests
{
    [Fact]
    public void Outputs_LedgerCredit_MatchesPer1000Rates()
    {
        (NeedsHarness h, EntityId house) = AdvanceHarness();
        h.Cover(house, ServiceKind.Shrine);
        h.Stock("homespun", Amount.FromUnits(100)).Stock("bread", Amount.FromUnits(100));

        // Four residents at a villagers output of 10 knowledge per 1,000: the next day credits
        // 10 × 5 = 50 milli-units, and the slice defines no culture output for the tier.
        // (The credit does not depend on which needs are met, only on comfort for its multiplier.)
        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge);
        h.RunDays(1);

        h.House(house).Residents.ShouldBe(5);
        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromMilli(50));
        h.Sim.Services.PointsLedger.TakeToday(PointKind.Culture).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Outputs_UnmetNeeds_StillCreditTheTierOutput()
    {
        (NeedsHarness h, EntityId house) = AdvanceHarness();

        // Nothing but fish and the market is met, yet the house still produces knowledge from its four residents.
        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge);
        h.RunDays(1);

        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromMilli(40));
    }

    [Fact]
    public void WorkforceSupply_PublishedPerTier_AtDayStart()
    {
        var h = NeedsHarness.Create();
        EntityId first = h.AddHouse();
        EntityId second = h.AddHouse();
        h.Cover(first, ServiceKind.Market).Cover(second, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));

        h.RunDays(2);

        h.Population.Queries.Residents(h.Tier("settlers")).ShouldBe(6);
        h.Population.Queries.TotalResidents.ShouldBe(6);
        h.Sim.Queries.Workforce.Supply(h.Tier("settlers")).ShouldBe(6);
        h.Sim.Queries.Workforce.Supply(h.Tier("villagers")).ShouldBe(0);
    }

    [Fact]
    public void Queries_HouseNeeds_ReportsTotals()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.RunDays(1);

        HouseNeedsInfo needs = h.HouseNeeds(house);
        needs.House.ShouldBe(house);
        needs.HasMarketCoverage.ShouldBeTrue();
        needs.BasicTotal.ShouldBe(3);
        needs.BasicMet.ShouldBe(3);
        needs.ComfortMet.ShouldBe(0);
        needs.Needs.Count(n => n.Class == NeedClass.Comfort).ShouldBe(0);
    }

    [Fact]
    public void Queries_UnknownHouseNeeds_ReturnsFalse()
    {
        var h = NeedsHarness.Create();

        h.Needs.Queries.TryGetHouseNeeds(new EntityId(7000), out _).ShouldBeFalse();
    }

    private static (NeedsHarness Harness, EntityId House) AdvanceHarness()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Stock("timber", Amount.FromUnits(2));
        h.UnlockTier("villagers");
        h.RunDays(3);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).Residents.ShouldBe(4);
        return (h, house);
    }
}
