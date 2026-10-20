using Evenmere.Sim.Tests.Fixtures;
using Evenmere.Sim.Tests.Skeleton;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>Every concrete command the simulation declares has exactly one handler registered by the full pipeline.</summary>
public sealed class CommandHandlerTests
{
    [Fact]
    public void Pipeline_EverySimCommandType_HasExactlyOneHandler()
    {
        // The pipeline registers its handlers while the simulation is created, so this is the real, fully assembled router.
        Simulation sim = new WorldBuilder().Build();
        IReadOnlyList<Type> declared = CommandHandlerRules.FindCommandTypes(typeof(Command).Assembly);

        CommandHandlerRules.Check(declared, sim.Router.RegisteredCommandTypes).ShouldBeEmpty();
    }

    [Fact]
    public void FindCommandTypes_SimAssembly_ReturnsOnlyConcreteCommands()
    {
        IReadOnlyList<Type> declared = CommandHandlerRules.FindCommandTypes(typeof(Command).Assembly);

        declared.ShouldAllBe(static t => t.IsSubclassOf(typeof(Command)) && !t.IsAbstract);
    }

    [Fact]
    public void FindCommandTypes_Assembly_FindsConcreteCommandsAndSkipsAbstractOnes()
    {
        IReadOnlyList<Type> found = CommandHandlerRules.FindCommandTypes(typeof(CommandHandlerTests).Assembly);

        found.ShouldContain(typeof(FakeCommandWithoutHandler));
        found.ShouldContain(typeof(FakeCommandWithHandler));
        found.ShouldContain(typeof(FakeDerivedCommand));
        found.ShouldContain(typeof(PingCommand));
        found.ShouldNotContain(typeof(FakeAbstractCommand));
        found.ShouldNotContain(typeof(Command));
        found.ShouldNotContain(typeof(SimEvent));
    }

    [Fact]
    public void Check_CommandWithNoHandler_IsReported()
    {
        IReadOnlyList<string> violations = CommandHandlerRules.Check([typeof(FakeCommandWithoutHandler)], []);

        violations.ShouldHaveSingleItem().ShouldBe("FakeCommandWithoutHandler has no handler registered by the pipeline");
    }

    [Fact]
    public void Check_CommandWithTwoHandlers_IsReported()
    {
        IReadOnlyList<string> violations = CommandHandlerRules.Check(
            [typeof(FakeCommandWithHandler)],
            [typeof(FakeCommandWithHandler), typeof(FakeCommandWithHandler)]);

        violations.ShouldHaveSingleItem().ShouldBe("FakeCommandWithHandler has 2 handlers registered; exactly one is allowed");
    }

    [Fact]
    public void Check_CommandWithOneHandler_ReportsNothing()
    {
        CommandHandlerRules.Check([typeof(FakeCommandWithHandler)], [typeof(FakeCommandWithHandler)]).ShouldBeEmpty();
    }

    [Fact]
    public void Check_RealRouterMissingOneHandler_NamesTheCommandWithoutIt()
    {
        Simulation sim = new WorldBuilder().Build();
        sim.Router.Register(new LambdaHandler<FakeCommandWithHandler>(null, null));

        IReadOnlyList<string> violations = CommandHandlerRules.Check(
            [typeof(FakeCommandWithHandler), typeof(FakeCommandWithoutHandler)],
            sim.Router.RegisteredCommandTypes);

        violations.ShouldHaveSingleItem().ShouldBe("FakeCommandWithoutHandler has no handler registered by the pipeline");
    }
}
