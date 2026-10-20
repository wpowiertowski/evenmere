using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class EventTests
{
    [Fact]
    public void DrainEvents_NothingEmitted_ReturnsEmpty()
    {
        Simulation sim = new WorldBuilder().Build();

        sim.Tick();

        sim.DrainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void DrainEvents_AfterTicks_ReturnsEventsInEmissionOrderAndClears()
    {
        Simulation sim = new WorldBuilder().Build();
        sim.Pipeline.Replace(SystemId.Production, new SpySystem(SystemId.Production, [], ctx => ctx.Events.Emit(new TestEvent(ctx.Tick, "production"))));
        sim.Pipeline.Replace(SystemId.Construction, new SpySystem(SystemId.Construction, [], ctx => ctx.Events.Emit(new TestEvent(ctx.Tick, "construction"))));
        sim.Router.Register(new LambdaHandler<PingCommand>(null, (ctx, _) => ctx.Events.Emit(new TestEvent(ctx.Tick, "command"))));
        sim.Enqueue(new PingCommand("a"));

        sim.Tick();
        sim.Tick();

        string[] labels = [.. sim.DrainEvents().Cast<TestEvent>().Select(static e => $"{e.Label}@{e.Tick}")];

        // Commands apply first, then systems in tick order (construction before production), tick after tick.
        labels.ShouldBe(["command@1", "construction@1", "production@1", "construction@2", "production@2"]);
        sim.DrainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void DrainEvents_Result_IsASnapshotTheHostOwns()
    {
        Simulation sim = new WorldBuilder().Build();
        sim.Pipeline.Replace(SystemId.Production, new SpySystem(SystemId.Production, [], ctx => ctx.Events.Emit(new TestEvent(ctx.Tick, "p"))));

        sim.Tick();
        IReadOnlyList<SimEvent> first = sim.DrainEvents();
        sim.Tick();
        IReadOnlyList<SimEvent> second = sim.DrainEvents();

        first.Count.ShouldBe(1);
        second.Count.ShouldBe(1);
        first[0].Tick.ShouldBe(1);
        second[0].Tick.ShouldBe(2);
    }

    [Fact]
    public void Emit_NullEvent_Throws()
    {
        Simulation sim = new WorldBuilder().Build();

        Should.Throw<ArgumentNullException>(() => sim.Context.Events.Emit(null!));
    }

    [Fact]
    public void ComputeStateHash_EventsDrainedOrNot_DoesNotChange()
    {
        Simulation sim = new WorldBuilder().Build();
        sim.Pipeline.Replace(SystemId.Production, new SpySystem(SystemId.Production, [], ctx => ctx.Events.Emit(new TestEvent(ctx.Tick, "p"))));
        sim.Tick();

        ulong before = sim.ComputeStateHash();
        _ = sim.DrainEvents();

        sim.ComputeStateHash().ShouldBe(before);
    }
}
