using System.Reflection;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Services;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Contracts;

/// <summary>
/// The command contract of M1-00: every command is a sealed record with value semantics, every one has a handler from the
/// start, and a command whose task has not landed is refused politely instead of crashing the simulation.
/// </summary>
public sealed class CommandContractTests
{
    public static TheoryData<string> CommandNames()
    {
        var data = new TheoryData<string>();
        foreach (Type type in CommandSamples.AllCommandTypes())
        {
            data.Add(type.Name);
        }

        return data;
    }

    private static Type CommandNamed(string name) => CommandSamples.AllCommandTypes().Single(t => t.Name == name);

    [Fact]
    public void AllCommandTypes_Simulation_DeclaresTheM1Commands()
    {
        string[] names = [.. CommandSamples.AllCommandTypes().Select(static t => t.Name)];

        names.ShouldContain("PlaceBuilding");
        names.ShouldContain("Demolish");
        names.ShouldContain("UpgradeBuilding");
        names.ShouldContain("PlaceRoad");
        names.ShouldContain("RemoveRoad");
        names.ShouldContain("SetBuildingPaused");
        names.ShouldContain("SetWorkforcePriority");
        names.ShouldContain("SetHouseUpgradeLock");
        names.ShouldContain("SetResearchTarget");
        names.ShouldContain("SetResearchQueue");
        names.ShouldContain("SetCivicTarget");
        names.ShouldContain("SetCivicQueue");
        names.ShouldContain("DismissAdvice");
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void Command_Type_IsASealedPublicRecord(string name)
    {
        Type type = CommandNamed(name);

        type.IsSealed.ShouldBeTrue($"{name} must be sealed");
        type.IsPublic.ShouldBeTrue($"{name} must be public so the game can enqueue it");

        // The compiler gives every record a <Clone>$ method; a class or struct has none.
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldNotBeNull($"{name} must be a record");
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void Command_EqualValues_AreEqualWithTheSameHashCode(string name)
    {
        Type type = CommandNamed(name);

        // Two samples are built separately, so any array inside is a different object with the same contents.
        Command first = CommandSamples.Create(type, 0);
        Command second = CommandSamples.Create(type, 0);

        first.ShouldNotBeSameAs(second);
        first.Equals(second).ShouldBeTrue($"{name}: equal values must be equal");
        first.Equals((object)second).ShouldBeTrue();
        second.GetHashCode().ShouldBe(first.GetHashCode(), $"{name}: equal values must hash alike");
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void Command_DifferentValues_AreNotEqual(string name)
    {
        Type type = CommandNamed(name);

        Command first = CommandSamples.Create(type, 0);
        Command other = CommandSamples.Create(type, 1);

        first.Equals(other).ShouldBeFalse($"{name}: different values must not be equal");
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void Command_Type_HasExactlyOneHandlerInTheFullPipeline(string name)
    {
        Type type = CommandNamed(name);
        Simulation sim = new WorldBuilder().Build();

        sim.Router.RegisteredCommandTypes.Count(t => t == type).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void Command_ThroughARealSimulation_IsRefusedNotCrashed(string name)
    {
        Type type = CommandNamed(name);
        Simulation sim = new WorldBuilder().Build();
        Command command = CommandSamples.Create(type, 0);

        sim.Enqueue(command);
        sim.Tick();

        sim.PendingCommandCount.ShouldBe(0);

        // The samples name things that do not exist in an empty world, so a real handler refuses them too; what it must
        // never do is answer UnknownCommand (no handler) or throw. While a task's placeholder is still in place the refusal
        // is NotImplemented and names the task, so this test needs no change when that task lands.
        CommandRejected rejection = sim.DrainEvents().OfType<CommandRejected>().ShouldHaveSingleItem();
        rejection.Command.ShouldBe(command);
        rejection.Code.ShouldNotBe(RejectReason.UnknownCommand);
        if (rejection.Code == RejectReason.NotImplemented)
        {
            rejection.Detail.ShouldContain(name);
            rejection.Detail.ShouldMatch(@"task M\d+-\d+ adds its handler");
        }
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void NotImplementedHandler_AnyCommand_RejectsWithTheTaskAndNeverApplies(string name)
    {
        Type type = CommandNamed(name);
        Command command = CommandSamples.Create(type, 0);
        var fake = FakeSim.Create();
        object handler = Activator.CreateInstance(typeof(NotImplementedCommandHandler<>).MakeGenericType(type), "M9-99")!;
        MethodInfo validate = handler.GetType().GetMethod(nameof(ICommandHandler<Command>.Validate))!;
        MethodInfo apply = handler.GetType().GetMethod(nameof(ICommandHandler<Command>.Apply))!;

        var result = (CommandResult)validate.Invoke(handler, [fake.Context, command])!;

        result.IsRejected.ShouldBeTrue();
        result.Reason.ShouldBe(RejectReason.NotImplemented);
        result.Detail.ShouldBe($"{name} is not implemented yet; task M9-99 adds its handler.");
        TargetInvocationException thrown = Should.Throw<TargetInvocationException>(() => apply.Invoke(handler, [fake.Context, command]));
        thrown.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void RejectReason_Members_KeepTheirNumbersSoLogsStayReadable()
    {
        // Hosts and command logs may store the numeric code, so the numbers are frozen (append only).
        ((int)RejectReason.UnknownCommand).ShouldBe(0);
        ((int)RejectReason.Invalid).ShouldBe(1);
        ((int)RejectReason.NotImplemented).ShouldBe(2);
        ((int)RejectReason.UnknownTarget).ShouldBe(3);
        ((int)RejectReason.PlacementInvalid).ShouldBe(4);
        ((int)RejectReason.NodeNotAvailable).ShouldBe(5);
        ((int)RejectReason.NotApplicable).ShouldBe(6);
        ((int)RejectReason.Protected).ShouldBe(7);
    }
}
