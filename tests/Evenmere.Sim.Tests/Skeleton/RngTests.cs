using Evenmere.Core.Rng;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class RngTests
{
    [Fact]
    public void CreateNew_EverySystem_GetsItsOwnForSystemStream()
    {
        Simulation sim = new WorldBuilder().WithSeed(1234).Build();

        sim.World.SystemRngs.Length.ShouldBe(SystemNames.Count);
        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            var expected = Pcg32.ForSystem(1234, SystemNames.Of(id));
            Pcg32 actual = sim.World.SystemRngs[(int)id];

            actual.State.ShouldBe(expected.State, $"{id} state");
            actual.Inc.ShouldBe(expected.Inc, $"{id} inc");
        }
    }

    [Fact]
    public void CreateNew_EverySystem_HasADistinctStream()
    {
        Simulation sim = new WorldBuilder().Build();

        ulong[] increments = [.. sim.World.SystemRngs.Select(static rng => rng.Inc)];

        increments.Distinct().Count().ShouldBe(SystemNames.Count);
    }

    [Fact]
    public void CreateNew_DifferentSeeds_GiveDifferentStreams()
    {
        Simulation a = new WorldBuilder().WithSeed(1).Build();
        Simulation b = new WorldBuilder().WithSeed(2).Build();

        a.World.SystemRngs[(int)SystemId.Production].State.ShouldNotBe(b.World.SystemRngs[(int)SystemId.Production].State);
        a.ComputeStateHash().ShouldNotBe(b.ComputeStateHash());
    }

    [Fact]
    public void Rng_DrawInsideARunningSystem_AdvancesTheStoredGeneratorOnly()
    {
        Simulation sim = new WorldBuilder().WithSeed(42).Build();
        sim.Pipeline.Replace(
            SystemId.Production,
            new SpySystem(SystemId.Production, [], ctx => _ = ctx.Rng.NextUInt()));
        Pcg32[] before = [.. sim.World.SystemRngs];

        sim.Tick();
        sim.Tick();
        sim.Tick();

        // The same three draws made by hand on a copy of the starting generator land on the same stored state.
        Pcg32 expected = before[(int)SystemId.Production];
        _ = expected.NextUInt();
        _ = expected.NextUInt();
        _ = expected.NextUInt();

        sim.World.SystemRngs[(int)SystemId.Production].State.ShouldBe(expected.State);
        foreach (SystemId id in Enum.GetValues<SystemId>().Where(static id => id != SystemId.Production))
        {
            sim.World.SystemRngs[(int)id].State.ShouldBe(before[(int)id].State, $"{id} must not be touched");
        }
    }

    [Fact]
    public void Rng_EachRunningSystem_SeesItsOwnStream()
    {
        Simulation sim = new WorldBuilder().WithSeed(42).Build();
        var firstDraws = new Dictionary<string, uint>();
        foreach (SystemId id in new[] { SystemId.Construction, SystemId.Production, SystemId.Logistics })
        {
            SystemId captured = id;
            sim.Pipeline.Replace(id, new SpySystem(id, [], ctx => firstDraws.TryAdd(SystemNames.Of(captured), ctx.Rng.NextUInt())));
        }

        sim.Tick();

        foreach ((string name, uint draw) in firstDraws)
        {
            var fresh = Pcg32.ForSystem(42, name);
            draw.ShouldBe(fresh.NextUInt(), $"{name} draws from the stream named after it");
        }

        firstDraws.Count.ShouldBe(3);
    }

    [Fact]
    public void RngState_AfterRestore_ContinuesTheSameSequence()
    {
        Simulation sim = new WorldBuilder().WithSeed(5).Build();
        sim.Pipeline.Replace(SystemId.Needs, new SpySystem(SystemId.Needs, [], ctx => _ = ctx.Rng.NextUInt()));
        for (int i = 0; i < 10; i++)
        {
            sim.Tick();
        }

        var restored = Simulation.Restore(WorldBuilder.Content, sim.SerializeState());

        restored.World.SystemRngs[(int)SystemId.Needs].State.ShouldBe(sim.World.SystemRngs[(int)SystemId.Needs].State);
        restored.World.SystemRngs[(int)SystemId.Needs].Inc.ShouldBe(sim.World.SystemRngs[(int)SystemId.Needs].Inc);
    }
}
