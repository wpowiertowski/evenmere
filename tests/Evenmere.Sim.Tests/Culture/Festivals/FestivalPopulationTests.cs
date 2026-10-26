using Evenmere.Core.Ids;
using Evenmere.Content.Definitions;
using Evenmere.Core.Numerics;
using Evenmere.Sim;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;
using Evenmere.Sim.Tests.Needs;

namespace Evenmere.Sim.Tests.Culture.Festivals;

/// <summary>Decline timers pause while a festival runs and resume after it, across the real needs, population and
/// culture systems sharing one world.</summary>
public sealed class FestivalPopulationTests
{
    /// <summary>
    /// An advanced house whose villagers needs were all met on their first day, so bread counts as once-met and its
    /// loss starts the decline timer. The arrangement mirrors the decline tests' met harness.
    /// </summary>
    private static (NeedsHarness Harness, EntityId House) MetHarness()
    {
        var h = NeedsHarness.Create(FestivalsPack.Slice);
        EntityId house = h.AddHouse();
        h.Cover(house, ServiceKind.Market).Cover(house, ServiceKind.Shrine);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        h.Stock("homespun", Amount.FromUnits(100)).Stock("bread", Amount.FromUnits(100));
        h.Stock("timber", Amount.FromUnits(2));
        h.UnlockTier("villagers");
        h.RunDays(7);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.HouseNeeds(house).Needs.Single(n => n.Good == h.Good("bread")).EverMet.ShouldBeTrue();
        return (h, house);
    }

    private sealed class FestivalWorld
    {
        private readonly CommandQueue _commands = new();
        private readonly CommandRouter _router = new();

        public FestivalWorld(NeedsHarness h)
        {
            Harness = h;
            Culture = new CultureSystem(h.Sim.World.Culture, h.Sim.Content, () => h.Sim.Queries);
            Queries = new ForwardedCultureQueries(h.Sim.Queries, Culture.Queries);
            Context = new SimContext(h.Sim.Content, h.Sim.World, Queries, h.Sim.Events, h.Sim.Services.ToSimServices());
            Culture.Register(_router);
            Harness.Population.Register(_router);
        }

        public NeedsHarness Harness { get; }

        public CultureSystem Culture { get; }

        public ForwardedCultureQueries Queries { get; }

        public SimContext Context { get; }

        public void Send(Command command)
        {
            _commands.Enqueue(command);
            _router.ApplyQueued(_commands, Context);
        }

        public void RunDays(int days)
        {
            for (int day = 0; day < days; day++)
            {
                Culture.Run(Context);
                for (int tick = 0; tick < 120; tick++)
                {
                    Harness.Sim.SetTick(Harness.Sim.World.Clock.Tick + 1);
                    Harness.Needs.Run(Context);
                    Harness.Population.Run(Context);
                }
            }
        }
    }

    [Fact]
    public void Decline_PausedDuringFestival_ResumesAfter()
    {
        (NeedsHarness h, EntityId house) = MetHarness();
        var world = new FestivalWorld(h);
        FestivalId festival = h.Sim.Content.IdOf<FestivalId>("test_harvest");
        h.Sim.Queries.Progression.UnlockFestivals(festival);
        h.Stock("fish", Amount.FromUnits(100)).Stock("wood", Amount.FromUnits(100));
        EntityId ground = h.AddBuilding("test_festival_ground");
        h.Stock("bread", Amount.Zero);
        world.Send(new HoldFestival(ground, festival));
        h.Events();

        // Five festival days pass with a quiet timer and the inspector reporting the pause.
        world.RunDays(5);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).DeclineTimerDays.ShouldBe(0);
        h.House(house).DeclinePaused.ShouldBeTrue();

        // Four more days count the timer up: nine days since the bread dried, only four counted.
        world.RunDays(4);
        h.House(house).Tier.ShouldBe(h.Tier("villagers"));
        h.House(house).DeclineTimerDays.ShouldBe(4);
        h.House(house).DeclinePaused.ShouldBeFalse();

        // Six days later the timer reaches ten and the house steps down, five days later than without the festival.
        world.RunDays(6);
        h.House(house).Tier.ShouldBe(h.Tier("settlers"));
    }
}
