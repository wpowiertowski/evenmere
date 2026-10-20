using Evenmere.Core.Ids;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class TickLoopTests
{
    // Replaces every system with a spy that appends "<name>@<tick>" to one shared log.
    private static (Simulation Sim, List<string> Log) SpiedSimulation()
    {
        Simulation sim = new WorldBuilder().Build();
        var log = new List<string>();
        foreach (PipelineEntry entry in sim.Pipeline.Entries.ToArray())
        {
            sim.Pipeline.Replace(entry.Id, new SpySystem(entry.Id, log));
        }

        return (sim, log);
    }

    private static int CountRuns(List<string> log, SystemId id) =>
        log.Count(entry => entry.StartsWith(SystemNames.Of(id) + "@", StringComparison.Ordinal));

    [Fact]
    public void Tick_NewWorld_StartsAtTickZeroAndAdvancesByOne()
    {
        Simulation sim = new WorldBuilder().Build();

        sim.CurrentTick.ShouldBe(0);
        sim.Tick();
        sim.CurrentTick.ShouldBe(1);
        sim.Tick();
        sim.CurrentTick.ShouldBe(2);
    }

    [Fact]
    public void Tick_OneTick_RunsTheEveryTickAndBucketedSystemsInArchitectureOrder()
    {
        (Simulation sim, List<string> log) = SpiedSimulation();

        sim.Tick();

        // Tick 1 is not a day start, so the daily systems wait for tick 120.
        log.ShouldBe(
        [
            "construction@1", "production@1", "logistics@1", "exploration@1", "needs@1", "population@1", "economy@1",
        ]);
    }

    [Fact]
    public void Tick_DayStart_RunsEveryTickedSystemOnceInArchitectureOrder()
    {
        (Simulation sim, List<string> log) = SpiedSimulation();

        for (int i = 0; i < 119; i++)
        {
            sim.Tick();
        }

        log.Clear();
        sim.Tick();

        log.ShouldBe(
        [
            "construction@120", "workforce@120", "production@120", "logistics@120", "exploration@120", "needs@120",
            "population@120", "economy@120", "research@120", "culture@120", "progression@120", "happenings@120",
            "advisors@120", "statistics@120",
        ]);
    }

    [Fact]
    public void Tick_TwoDays_RunsEachCadenceTheRightNumberOfTimes()
    {
        (Simulation sim, List<string> log) = SpiedSimulation();

        for (int i = 0; i < 240; i++)
        {
            sim.Tick();
        }

        foreach (SystemId id in new[] { SystemId.Construction, SystemId.Production, SystemId.Logistics, SystemId.Exploration })
        {
            CountRuns(log, id).ShouldBe(240, $"{id} runs every tick");
        }

        foreach (SystemId id in new[] { SystemId.Needs, SystemId.Population, SystemId.Economy })
        {
            CountRuns(log, id).ShouldBe(240, $"{id} is daily-bucketed, so it runs every tick and filters its entities");
        }

        foreach (SystemId id in new[]
        {
            SystemId.Workforce, SystemId.Research, SystemId.Culture, SystemId.Progression, SystemId.Happenings,
            SystemId.Advisors, SystemId.Statistics,
        })
        {
            CountRuns(log, id).ShouldBe(2, $"{id} runs once per day");
        }

        foreach (SystemId id in new[] { SystemId.Map, SystemId.Roads, SystemId.Storage, SystemId.Modifiers })
        {
            CountRuns(log, id).ShouldBe(0, $"{id} is a service and is never ticked");
        }

        log.Where(static entry => entry.StartsWith("workforce@", StringComparison.Ordinal)).ShouldBe(["workforce@120", "workforce@240"]);
    }

    [Fact]
    public void Tick_ClockStep_AdvancesBeforeCommandsRun()
    {
        Simulation sim = new WorldBuilder().Build();
        long seenByHandler = -1;
        sim.Router.Register(new LambdaHandler<PingCommand>(null, (ctx, _) => seenByHandler = ctx.Tick));
        sim.Enqueue(new PingCommand("first"));

        sim.Tick();

        seenByHandler.ShouldBe(1);
    }

    [Fact]
    public void Tick_CommandStep_RunsBeforeAnyTickedSystem()
    {
        (Simulation sim, List<string> log) = SpiedSimulation();
        sim.Router.Register(new LambdaHandler<PingCommand>(null, (_, _) => log.Add("command")));
        sim.Enqueue(new PingCommand("first"));

        sim.Tick();

        log[0].ShouldBe("command");
        log[1].ShouldBe("construction@1");
    }

    [Fact]
    public void IsInBucket_Context_SelectsEachEntityOnceADay()
    {
        Simulation sim = new WorldBuilder().Build();
        EntityId entity = new(5);

        var ticksHandled = new List<long>();
        for (int i = 0; i < 360; i++)
        {
            sim.Tick();
            if (sim.Context.IsInBucket(entity))
            {
                ticksHandled.Add(sim.CurrentTick);
            }
        }

        ticksHandled.ShouldBe([5, 125, 245]);
    }
}
