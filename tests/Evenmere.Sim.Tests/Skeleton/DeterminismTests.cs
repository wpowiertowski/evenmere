using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class DeterminismTests
{
    private const int TenThousandTicks = 10_000;
    private const int HalfWay = 5_000;

    private static ulong RunAndHash(ulong seed, int ticks)
    {
        Simulation sim = new WorldBuilder().WithSeed(seed).Build();
        for (int i = 0; i < ticks; i++)
        {
            sim.Tick();
        }

        return sim.ComputeStateHash();
    }

    [Fact]
    public void Tick_TenThousandTicksTwice_GivesIdenticalHashes()
    {
        ulong first = RunAndHash(42, TenThousandTicks);
        ulong second = RunAndHash(42, TenThousandTicks);

        second.ShouldBe(first);
    }

    [Fact]
    public void RestoreAndContinue_FromTickFiveThousand_MatchesUninterruptedRun()
    {
        ulong uninterrupted = RunAndHash(42, TenThousandTicks);

        Simulation sim = new WorldBuilder().WithSeed(42).Build();
        for (int i = 0; i < HalfWay; i++)
        {
            sim.Tick();
        }

        byte[] saved = sim.SerializeState();
        var restored = Simulation.Restore(WorldBuilder.Content, saved);
        restored.CurrentTick.ShouldBe(HalfWay);
        restored.ComputeStateHash().ShouldBe(sim.ComputeStateHash());

        for (int i = HalfWay; i < TenThousandTicks; i++)
        {
            restored.Tick();
        }

        restored.CurrentTick.ShouldBe(TenThousandTicks);
        restored.ComputeStateHash().ShouldBe(uninterrupted);
    }
}
