using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Population;

public sealed class PopulationDeclineTests
{
    private static (NeedsHarness Harness, EntityId House) AdvancedHarness()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Stock("timber", Amount.FromUnits(2));
        h.UnlockTier("villagers");
        h.RunDays(3);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        return (h, house);
    }

    /// <summary>
    /// An advanced house whose villagers needs were all met on their first day, so every basic need counts as
    /// once-met. Everything is arranged before the run, because the fair shares are fixed at each day start.
    /// </summary>
    private static (NeedsHarness Harness, EntityId House) MetHarness()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market).Cover(house, ServiceKind.Shrine);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Stock("homespun", Amount.FromUnits(100)).Stock("bread", Amount.FromUnits(100));
        h.Stock("timber", Amount.FromUnits(2));
        h.UnlockTier("villagers");
        h.RunDays(4);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.HouseNeeds(house).Needs.Single(n => n.Good == h.Good("bread")).EverMet.ShouldBeTrue();
        return (h, house);
    }

    [Fact]
    public void Decline_NeverMetNeeds_NeverStartsTheTimer()
    {
        (NeedsHarness h, EntityId house) = AdvancedHarness();

        // Homespun and bread were never met at the villagers tier, and the shrine never covered them:
        // twelve days later the house is still there with a quiet timer.
        h.RunDays(12);

        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).DeclineTimerDays.ShouldBe(0);
        h.Events().OfType<HouseTierDeclined>().ShouldBeEmpty();
    }

    [Fact]
    public void Decline_LostBread_DeclinesExactlyTenDaysLaterOneTierOnly()
    {
        (NeedsHarness h, EntityId house) = MetHarness();

        // Bread was met, then the supply dries up: nine days count the timer up without declining.
        h.Stock("bread", Amount.Zero);
        for (int day = 1; day <= 9; day++)
        {
            h.RunDays(1);
            h.House(house).Tier.ShouldBe(h.Tier("villagers"));
            h.House(house).DeclineTimerDays.ShouldBe(day);
        }

        // On the tenth day the house steps down one tier, keeps its residents within the lower capacity,
        // and restarts with a fresh timer.
        h.RunDays(1);
        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("settlers"));
        info.DeclineTimerDays.ShouldBe(0);
        info.Residents.ShouldBe(6);

        HouseTierDeclined declined = h.Events().OfType<HouseTierDeclined>().Single();
        declined.House.ShouldBe(house);
        declined.FromTier.ShouldBe(h.Tier("villagers"));
        declined.ToTier.ShouldBe(h.Tier("settlers"));

        // The lower tier's needs are all still met, so ten more days bring no second step.
        h.RunDays(10);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
        h.Events().OfType<HouseTierDeclined>().ShouldBeEmpty();
    }

    [Fact]
    public void Decline_NeedReturnsBeforeGraceEnd_ResetsTheTimer()
    {
        (NeedsHarness h, EntityId house) = MetHarness();
        h.Stock("bread", Amount.Zero);

        h.RunDays(5);
        h.House(house).DeclineTimerDays.ShouldBe(5);

        // Bread returns: the next day-start picks it up, and the day after the need is met the timer resets.
        h.Stock("bread", Amount.FromUnits(100));
        h.RunDays(2);
        h.House(house).DeclineTimerDays.ShouldBe(0);

        h.Stock("bread", Amount.Zero);
        h.RunDays(9);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.RunDays(1);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
    }

    [Fact]
    public void Decline_LowestTier_NeverDeclines()
    {
        var h = NeedsHarness.Create();
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));

        // Fish is met, then lost: settlers have nothing below them, so the timer never even counts.
        // Wood and the market still hold occupancy at 6 × 2 ÷ 3 = 4.
        h.RunDays(1);
        h.Stock("fish", Amount.Zero);
        h.RunDays(12);

        HouseInfo info = h.House(house);
        info.Tier.ShouldBe(h.Tier("settlers"));
        info.DeclineTimerDays.ShouldBe(0);
        info.Residents.ShouldBe(4);
        h.Events().OfType<HouseTierDeclined>().ShouldBeEmpty();
    }

    [Fact]
    public void Decline_FestivalActive_PausesTheTimer()
    {
        (NeedsHarness h, EntityId house) = MetHarness();
        h.Stock("bread", Amount.Zero);

        h.RunDays(5);
        h.House(house).DeclineTimerDays.ShouldBe(5);

        // While a festival runs the timer neither counts nor resets.
        h.Sim.World.Population.FestivalActive = true;
        h.RunDays(5);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).DeclineTimerDays.ShouldBe(5);

        h.Sim.World.Population.FestivalActive = false;
        h.RunDays(5);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
    }

    [Fact]
    public void Decline_GraceBonus_LengthensTheCountdown()
    {
        (NeedsHarness h, EntityId house) = MetHarness();
        h.Stock("bread", Amount.Zero);
        h.Sim.Queries.Progression.AddModifier(
            Stat.DeclineGrace,
            new ModifierContribution(new EffectSource(EffectSourceKind.Tech, 1), 100, true));

        // +100% doubles the ten-day grace to twenty.
        h.RunDays(10);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.RunDays(9);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.RunDays(1);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
    }
}
