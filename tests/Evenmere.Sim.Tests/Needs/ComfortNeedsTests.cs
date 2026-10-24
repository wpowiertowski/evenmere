using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Needs;

public sealed class ComfortNeedsTests
{
    private static (NeedsHarness Harness, EntityId House) PreparedHarness(bool withWool)
    {
        var h = NeedsHarness.Create(ComfortContent.Db);
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        if (withWool)
        {
            h.Stock("wool", Amount.FromUnits(100));
        }

        h.Stock("timber", Amount.FromUnits(2));
        h.UnlockTier("villagers");
        h.RunDays(6);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).Residents.ShouldBe(6);
        return (h, house);
    }

    [Fact]
    public void Comfort_MetMultipliesTheTierOutput()
    {
        (NeedsHarness h, EntityId house) = PreparedHarness(withWool: true);

        // Fish and the market are met, nothing else basic: the target falls to 10 × 2 ÷ 5 = 4 residents, so six
        // step down to five. Wool is met as comfort, so five residents credit 10 × 5 × 1.25 = 62 milli-units of
        // knowledge (the half milli-unit floors away).
        h.HouseNeeds(house).Needs.Count(n => n.Class == NeedClass.Comfort).ShouldBe(1);
        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge);
        h.RunDays(1);

        h.House(house).Residents.ShouldBe(5);
        h.HouseNeeds(house).Needs.Single(n => n.Class == NeedClass.Comfort).Met.ShouldBeTrue();
        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromMilli(62));
    }

    [Fact]
    public void Comfort_UnmetLeavesTheBaseOutput()
    {
        (NeedsHarness h, EntityId house) = PreparedHarness(withWool: false);

        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge);
        h.RunDays(1);

        h.HouseNeeds(house).Needs.Single(n => n.Class == NeedClass.Comfort).Met.ShouldBeFalse();
        h.Sim.Services.PointsLedger.TakeToday(PointKind.Knowledge).ShouldBe(Amount.FromMilli(50));
    }

    [Fact]
    public void Comfort_UnmetNeverCausesDecline()
    {
        (NeedsHarness h, EntityId house) = PreparedHarness(withWool: false);

        // Only fish and the market are ever met at the villagers tier; wool, homespun, bread and the shrine never are.
        // Twelve days later the comfort miss has still not started the timer.
        h.RunDays(12);

        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).DeclineTimerDays.ShouldBe(0);
    }
}
