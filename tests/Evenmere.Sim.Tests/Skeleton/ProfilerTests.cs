using Evenmere.Sim.Diagnostics;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class ProfilerTests
{
    [Fact]
    public void Simulation_IsAnOptionalProfilerReadout()
    {
        Simulation sim = new WorldBuilder().Build();
        ISimulation viaInterface = sim;

        (viaInterface as IProfilerReadout).ShouldNotBeNull();
        sim.TimerTicksPerSecond.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GetTimings_BeforeAnyTick_ListsClockCommandsAndTickedSystemsWithNoCalls()
    {
        Simulation sim = new WorldBuilder().Build();

        SystemTiming[] timings = [.. sim.GetTimings()];

        timings.Select(static t => t.Name).ShouldBe(
        [
            "clock", "commands", "construction", "workforce", "production", "logistics", "exploration", "needs",
            "population", "economy", "research", "culture", "progression", "happenings", "advisors", "statistics",
        ]);
        timings.ShouldAllBe(t => t.Calls == 0 && t.ElapsedTicks == 0);
    }

    [Fact]
    public void GetTimings_AfterTwoDays_CountsEachStepsRuns()
    {
        Simulation sim = new WorldBuilder().Build();

        for (int i = 0; i < 240; i++)
        {
            sim.Tick();
        }

        IReadOnlyList<SystemTiming> timings = sim.GetTimings();
        long CallsOf(string name) => timings.Single(t => t.Name == name).Calls;

        CallsOf("clock").ShouldBe(240);
        CallsOf("commands").ShouldBe(240);
        CallsOf("production").ShouldBe(240);
        CallsOf("needs").ShouldBe(240);
        CallsOf("workforce").ShouldBe(2);
        CallsOf("statistics").ShouldBe(2);
        timings.ShouldAllBe(t => t.ElapsedTicks >= 0);
    }

    [Fact]
    public void ResetTimings_AfterTicks_ClearsEverything()
    {
        Simulation sim = new WorldBuilder().Build();
        for (int i = 0; i < 10; i++)
        {
            sim.Tick();
        }

        sim.ResetTimings();

        sim.GetTimings().ShouldAllBe(t => t.Calls == 0 && t.ElapsedTicks == 0);
    }

    [Fact]
    public void Stop_AfterWork_AddsElapsedTimeToTheSlot()
    {
        var profiler = new SimProfiler();
        int slot = profiler.Register("work");

        long start = SimProfiler.Start();
        long sink = 0;
        for (int i = 0; i < 100_000; i++)
        {
            sink += i;
        }

        profiler.Stop(slot, start);

        sink.ShouldBeGreaterThan(0);
        SystemTiming timing = profiler.GetTimings().ShouldHaveSingleItem();
        timing.Name.ShouldBe("work");
        timing.Calls.ShouldBe(1);
        timing.ElapsedTicks.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Stop_NegativeSlot_IsIgnored()
    {
        var profiler = new SimProfiler();

        profiler.Stop(-1, SimProfiler.Start());

        profiler.GetTimings().ShouldBeEmpty();
    }

    [Fact]
    public void Profiling_NeverChangesTheStateHash()
    {
        Simulation withReadout = new WorldBuilder().Build();
        Simulation other = new WorldBuilder().Build();
        for (int i = 0; i < 500; i++)
        {
            withReadout.Tick();
            _ = withReadout.GetTimings();
            other.Tick();
        }

        other.ResetTimings();

        withReadout.ComputeStateHash().ShouldBe(other.ComputeStateHash());
    }
}
