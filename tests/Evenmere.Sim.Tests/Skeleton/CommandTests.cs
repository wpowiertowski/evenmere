using Evenmere.Core.Rng;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class CommandTests
{
    [Fact]
    public void Register_SameCommandTypeTwice_Throws()
    {
        var router = new CommandRouter();
        router.Register(new LambdaHandler<PingCommand>(null, null));

        Should.Throw<InvalidOperationException>(() => router.Register(new LambdaHandler<PingCommand>(null, null)));
    }

    [Fact]
    public void Register_DifferentCommandTypes_Succeeds()
    {
        var router = new CommandRouter();

        router.Register(new LambdaHandler<PingCommand>(null, null));
        router.Register(new LambdaHandler<PickyCommand>(null, null));

        router.IsRegistered<PingCommand>().ShouldBeTrue();
        router.IsRegistered<PickyCommand>().ShouldBeTrue();
        router.IsRegistered<UnhandledCommand>().ShouldBeFalse();
        router.RegisteredCommandTypes.ShouldBe([typeof(PickyCommand), typeof(PingCommand)]);
    }

    [Fact]
    public void Register_NullHandler_Throws()
    {
        var router = new CommandRouter();

        Should.Throw<ArgumentNullException>(() => router.Register<PingCommand>(null!));
    }

    [Fact]
    public void Enqueue_Command_IsNotAppliedUntilTheNextTick()
    {
        Simulation sim = new WorldBuilder().Build();
        var applied = new List<string>();
        sim.Router.Register(new LambdaHandler<PingCommand>(null, (_, command) => applied.Add(command.Label)));

        sim.Enqueue(new PingCommand("a"));

        applied.ShouldBeEmpty();
        sim.PendingCommandCount.ShouldBe(1);

        sim.Tick();

        applied.ShouldBe(["a"]);
        sim.PendingCommandCount.ShouldBe(0);
    }

    [Fact]
    public void Tick_QueuedCommands_ApplyInEnqueueOrder()
    {
        Simulation sim = new WorldBuilder().Build();
        var applied = new List<string>();
        sim.Router.Register(new LambdaHandler<PingCommand>(null, (_, command) => applied.Add(command.Label)));
        sim.Router.Register(new LambdaHandler<PickyCommand>(null, (_, command) => applied.Add("picky" + command.Value)));

        sim.Enqueue(new PingCommand("a"));
        sim.Enqueue(new PickyCommand(1));
        sim.Enqueue(new PingCommand("b"));
        sim.Enqueue(new PickyCommand(2));
        sim.Tick();

        applied.ShouldBe(["a", "picky1", "b", "picky2"]);
    }

    [Fact]
    public void Tick_AppliedCommand_EmitsNoRejection()
    {
        Simulation sim = new WorldBuilder().Build();
        sim.Router.Register(new LambdaHandler<PingCommand>(null, null));
        sim.Enqueue(new PingCommand("a"));

        sim.Tick();

        sim.DrainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Tick_CommandWithNoHandler_IsRejectedAsUnknownCommand()
    {
        Simulation sim = new WorldBuilder().Build();
        var command = new UnhandledCommand();
        sim.Tick();
        sim.Enqueue(command);

        sim.Tick();

        CommandRejected rejected = sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Code.ShouldBe(RejectReason.UnknownCommand);
        rejected.Command.ShouldBeSameAs(command);
        rejected.Tick.ShouldBe(2);
        rejected.Detail.ShouldContain(nameof(UnhandledCommand));
    }

    [Fact]
    public void Tick_CommandOfDerivedType_IsNotServedByTheBaseTypesHandler()
    {
        Simulation sim = new WorldBuilder().Build();
        var applied = new List<string>();
        sim.Router.Register(new LambdaHandler<BaseCommand>(null, (_, _) => applied.Add("base")));

        sim.Enqueue(new BaseCommand());
        sim.Enqueue(new DerivedCommand());
        sim.Tick();

        applied.ShouldBe(["base"]);
        CommandRejected rejected = sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Code.ShouldBe(RejectReason.UnknownCommand);
        rejected.Command.ShouldBeOfType<DerivedCommand>();
    }

    [Fact]
    public void Tick_CommandFailingValidation_EmitsCommandRejectedAndIsNotApplied()
    {
        Simulation sim = new WorldBuilder().Build();
        var applied = new List<int>();
        sim.Router.Register(new LambdaHandler<PickyCommand>(
            static (_, command) => command.Value < 0 ? CommandResult.Invalid("negative values are not allowed") : CommandResult.Ok,
            (_, command) => applied.Add(command.Value)));
        var bad = new PickyCommand(-1);

        sim.Enqueue(new PickyCommand(1));
        sim.Enqueue(bad);
        sim.Enqueue(new PickyCommand(2));
        sim.Tick();

        applied.ShouldBe([1, 2]);
        CommandRejected rejected = sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>();
        rejected.Code.ShouldBe(RejectReason.Invalid);
        rejected.Detail.ShouldBe("negative values are not allowed");
        rejected.Command.ShouldBeSameAs(bad);
        rejected.Tick.ShouldBe(1);
    }

    [Fact]
    public void Tick_ValidationFailureWithCustomReason_CarriesTheReason()
    {
        Simulation sim = new WorldBuilder().Build();
        sim.Router.Register(new LambdaHandler<PickyCommand>(static (_, _) => CommandResult.Reject((RejectReason)99, "future reason"), null));
        sim.Enqueue(new PickyCommand(1));

        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>().Code.ShouldBe((RejectReason)99);
    }

    [Fact]
    public void Tick_CommandEnqueuedByAHandler_WaitsForTheNextTick()
    {
        Simulation sim = new WorldBuilder().Build();
        var applied = new List<string>();
        sim.Router.Register(new LambdaHandler<PingCommand>(
            null,
            (_, command) =>
            {
                applied.Add(command.Label);
                if (command.Label == "first")
                {
                    sim.Enqueue(new PingCommand("second"));
                }
            }));
        sim.Enqueue(new PingCommand("first"));

        sim.Tick();
        applied.ShouldBe(["first"]);

        sim.Tick();
        applied.ShouldBe(["first", "second"]);
    }

    [Fact]
    public void Enqueue_NullCommand_Throws()
    {
        Simulation sim = new WorldBuilder().Build();

        Should.Throw<ArgumentNullException>(() => sim.Enqueue(null!));
    }

    [Fact]
    public void Tick_HandlerRegisteredByASystem_DrawsFromThatSystemsRandomStream()
    {
        Simulation sim = new WorldBuilder().WithSeed(7).Build();
        sim.Pipeline.Replace(
            SystemId.Roads,
            new SpySystem(SystemId.Roads, [], onRegister: router => router.Register(
                new LambdaHandler<PingCommand>(null, (ctx, _) => ctx.Rng.NextUInt()))));
        sim.Pipeline.RegisterCommands(sim.Router);
        Pcg32[] before = [.. sim.World.SystemRngs];

        sim.Enqueue(new PingCommand("draw"));
        sim.Tick();

        Pcg32[] after = sim.World.SystemRngs;
        for (int i = 0; i < after.Length; i++)
        {
            bool advanced = after[i].State != before[i].State;
            advanced.ShouldBe(i == (int)SystemId.Roads, $"{(SystemId)i}'s stream should {(i == (int)SystemId.Roads ? string.Empty : "not ")}have advanced");
        }
    }

    [Fact]
    public void Enqueue_Commands_GetMonotonicSequenceNumbers()
    {
        var queue = new CommandQueue();

        long first = queue.Enqueue(new PingCommand("a"));
        long second = queue.Enqueue(new PingCommand("b"));
        long third = queue.Enqueue(new PingCommand("c"));

        (first, second, third).ShouldBe((1L, 2L, 3L));
        queue.LastSequence.ShouldBe(3);

        queue.TryDequeue(out QueuedCommand oldest).ShouldBeTrue();
        oldest.Sequence.ShouldBe(1);
        oldest.Command.ShouldBe(new PingCommand("a"));

        // Draining never reuses a number.
        queue.Enqueue(new PingCommand("d")).ShouldBe(4);
    }

    [Fact]
    public void CommandResult_Default_IsAccepted()
    {
        CommandResult result = default;

        result.IsRejected.ShouldBeFalse();
        result.Detail.ShouldBeEmpty();
        CommandResult.Ok.ShouldBe(result);
    }
}
