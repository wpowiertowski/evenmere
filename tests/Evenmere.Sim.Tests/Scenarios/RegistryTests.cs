using System.Text.Json;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Tests.Fixtures;
using Evenmere.Sim.Tests.Skeleton;

namespace Evenmere.Sim.Tests.Scenarios;

public sealed class RegistryTests
{
    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static CommandRegistration Registered(string name) =>
        CommandRegistry.Default.TryGet(name, out CommandRegistration? registration)
            ? registration
            : throw new InvalidOperationException($"{name} is not registered.");

    private static T Deserialize<T>(string name, string args)
        where T : Command =>
        CommandRegistry.Deserialize(Registered(name), Json(args), ScenarioJson.CreateOptions(WorldBuilder.Content)).ShouldBeOfType<T>();

    [Fact]
    public void Default_MarkedTestCommands_AreRegisteredByTheirSimpleName()
    {
        CommandRegistry.Default.Names.ShouldContain("RecordTick");
        CommandRegistry.Default.Names.ShouldContain("GrantGoods");
        CommandRegistry.Default.Names.ShouldContain("UnservedCommand");
        CommandRegistration registration = Registered("RecordTick");

        registration.CommandType.ShouldBe(typeof(RecordTick));
        registration.HandlerType.ShouldBe(typeof(RecordTickHandler));
    }

    [Fact]
    public void Default_CommandWithoutHandlerType_HasNoHandler()
    {
        Registered("UnservedCommand").HandlerType.ShouldBeNull();
    }

    [Fact]
    public void Default_UnmarkedTestCommands_AreNotRegistered()
    {
        // PingCommand is a concrete Command in this assembly, but no scenario can name it without the attribute.
        typeof(PingCommand).BaseType.ShouldBe(typeof(Command));

        CommandRegistry.Default.TryGet(nameof(PingCommand), out _).ShouldBeFalse();
        CommandRegistry.Default.TryGet(nameof(PickyCommand), out _).ShouldBeFalse();
    }

    [Fact]
    public void Default_EveryConcreteCommandInTheSimulation_IsRegistered()
    {
        // Vacuous until M1 adds commands; from then on it proves new commands need no registration.
        string[] concrete = [.. typeof(Command).Assembly.GetTypes()
            .Where(static t => !t.IsAbstract && t.IsSubclassOf(typeof(Command)))
            .Select(static t => t.Name)];

        foreach (string name in concrete)
        {
            CommandRegistry.Default.Names.ShouldContain(name);
        }
    }

    [Fact]
    public void TryGet_DifferentCase_IsNotFoundAndTheErrorSuggestsTheRealName()
    {
        CommandRegistry.Default.TryGet("recordtick", out _).ShouldBeFalse();

        string message = CommandRegistry.Default.DescribeUnknown("recordtick");

        message.ShouldContain("unknown command type \"recordtick\"");
        message.ShouldContain("Did you mean \"RecordTick\"?");
        message.ShouldContain("Known command types:");
        message.ShouldContain("GrantGoods, RecordTick, UnservedCommand");
    }

    [Fact]
    public void Create_TwoCommandsWithTheSameName_Throws()
    {
        var first = new CommandRegistration("Dup", typeof(RecordTick), null);
        var second = new CommandRegistration("Dup", typeof(GrantGoods), null);

        InvalidOperationException error = Should.Throw<InvalidOperationException>(() => CommandRegistry.Create([first, second]));

        error.Message.ShouldContain("\"Dup\" is ambiguous");
    }

    [Fact]
    public void FromMarkedType_HandlerForAnotherCommand_Throws()
    {
        InvalidOperationException error = Should.Throw<InvalidOperationException>(
            () => CommandRegistry.FromMarkedType(typeof(RecordTick), typeof(GrantGoodsHandler)));

        error.Message.ShouldBe("GrantGoodsHandler must implement ICommandHandler<RecordTick> to serve RecordTick.");
    }

    [Fact]
    public void FromMarkedType_HandlerForTheCommand_KeepsBothTypes()
    {
        CommandRegistration registration = CommandRegistry.FromMarkedType(typeof(GrantGoods), typeof(GrantGoodsHandler));

        registration.ShouldBe(new CommandRegistration("GrantGoods", typeof(GrantGoods), typeof(GrantGoodsHandler)));
    }

    [Fact]
    public void RegisterHandler_TestOnlyCommand_ServesItOnTheSimulation()
    {
        Simulation sim = new WorldBuilder().Build();

        Registered("RecordTick").RegisterHandler(sim.Router);

        sim.Router.IsRegistered<RecordTick>().ShouldBeTrue();
    }

    [Fact]
    public void RegisterHandler_CommandWithoutHandlerType_RegistersNothing()
    {
        Simulation sim = new WorldBuilder().Build();

        Registered("UnservedCommand").RegisterHandler(sim.Router);

        sim.Router.IsRegistered<UnservedCommand>().ShouldBeFalse();
    }

    [Fact]
    public void Deserialize_ArgsIgnoringCase_FillTheConstructorParameters()
    {
        RecordTick command = Deserialize<RecordTick>("RecordTick", """{ "LABEL": "hello" }""");

        command.ShouldBe(new RecordTick("hello"));
    }

    [Fact]
    public void Deserialize_ContentIdsAndAmounts_ReadFromStringIdsAndDecimals()
    {
        GrantGoods fromString = Deserialize<GrantGoods>("GrantGoods", """{ "good": "timber", "amount": "12.5" }""");
        GrantGoods fromNumber = Deserialize<GrantGoods>("GrantGoods", """{ "good": "fish", "amount": 3 }""");

        fromString.Good.ShouldBe(WorldBuilder.Content.IdOf<GoodId>("timber"));
        fromString.Amount.ShouldBe(Amount.Parse("12.5"));
        fromNumber.Good.ShouldBe(WorldBuilder.Content.IdOf<GoodId>("fish"));
        fromNumber.Amount.ShouldBe(Amount.FromUnits(3));
    }

    [Fact]
    public void Deserialize_CommandWithNoParameters_NeedsNoArgs()
    {
        Deserialize<UnservedCommand>("UnservedCommand", "{}").ShouldBe(new UnservedCommand());
    }

    [Theory]
    [InlineData("RecordTick", """{ "lable": "typo" }""", "could not be mapped")]
    [InlineData("RecordTick", """{ }""", "missing required properties")]
    [InlineData("RecordTick", """{ "label": 5 }""", "label")]
    [InlineData("RecordTick", """{ "label": null }""", "label")]
    [InlineData("GrantGoods", """{ "good": "timbr", "amount": "1" }""", "No GoodId named \"timbr\"")]
    [InlineData("GrantGoods", """{ "good": 3, "amount": "1" }""", "must be the item's string id")]
    [InlineData("GrantGoods", """{ "good": "timber", "amount": "1.2345" }""", "more than 3 decimals")]
    [InlineData("GrantGoods", """{ "good": "timber", "amount": true }""", "must be a decimal string")]
    [InlineData("UnservedCommand", """{ "extra": 1 }""", "could not be mapped")]
    public void Deserialize_ArgsThatDoNotFit_ThrowJsonException(string name, string args, string expected)
    {
        CommandRegistration registration = Registered(name);

        JsonException error = Should.Throw<JsonException>(
            () => CommandRegistry.Deserialize(registration, Json(args), ScenarioJson.CreateOptions(WorldBuilder.Content)));

        error.Message.ShouldContain(expected);
    }

    [Fact]
    public void Default_EventRegistry_ListsSimulationAndHarnessEvents()
    {
        EventRegistry.Default.Contains("CommandRejected").ShouldBeTrue();
        EventRegistry.Default.Contains("TickRecorded").ShouldBeTrue();
        EventRegistry.Default.Contains("GoodsGranted").ShouldBeTrue();
        EventRegistry.Default.Contains("SimEvent").ShouldBeFalse();
    }

    [Fact]
    public void DescribeUnknown_MisspeltEvent_ListsTheRealOnes()
    {
        string message = EventRegistry.Default.DescribeUnknown("commandrejected");

        message.ShouldContain("unknown event type \"commandrejected\"");
        message.ShouldContain("Did you mean \"CommandRejected\"?");
        message.ShouldContain("Known event types:");
    }

    [Fact]
    public void Create_TwoEventsWithTheSameName_Throws()
    {
        InvalidOperationException error = Should.Throw<InvalidOperationException>(
            () => EventRegistry.Create([typeof(TickRecorded), typeof(TickRecorded)]));

        error.Message.ShouldContain("\"TickRecorded\" is ambiguous");
    }
}
