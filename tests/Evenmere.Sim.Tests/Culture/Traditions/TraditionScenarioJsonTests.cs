using System.Text.Json;
using Evenmere.Content.Definitions;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Sim.Tests.Scenarios;

namespace Evenmere.Sim.Tests.Culture.Traditions;

/// <summary>The scenario JSON for <c>SetTradition</c>: the slot type reads from its snake_case name, and a null card clears the slot.</summary>
public sealed class TraditionScenarioJsonTests
{
    private static SetTradition Deserialize(string args)
    {
        using var document = JsonDocument.Parse(args);
        CommandRegistration registration = CommandRegistry.Default.TryGet(nameof(SetTradition), out CommandRegistration? found)
            ? found
            : throw new InvalidOperationException($"{nameof(SetTradition)} is not registered.");
        return CommandRegistry.Deserialize(registration, document.RootElement.Clone(), ScenarioJson.CreateOptions(TraditionsPack.Content))
            .ShouldBeOfType<SetTradition>();
    }

    [Fact]
    public void SetTradition_ParsesSlotTypeAndTraditionId()
    {
        SetTradition command = Deserialize("""{"slot":"wild","index":0,"tradition":"test_wild_song"}""");

        command.Slot.ShouldBe(TraditionSlotType.Wild);
        command.Index.ShouldBe(0);
        command.Tradition.ShouldBe(TraditionsPack.Content.IdOf<Core.Ids.TraditionId>("test_wild_song"));
    }

    [Fact]
    public void SetTradition_ParsesNullTraditionAsUnslot()
    {
        SetTradition command = Deserialize("""{"slot":"hearth","index":0,"tradition":null}""");

        command.Slot.ShouldBe(TraditionSlotType.Hearth);
        command.Tradition.ShouldBeNull();
    }

    [Fact]
    public void SetTradition_RejectsAnUnknownSlotName()
    {
        Should.Throw<JsonException>(() => Deserialize("""{"slot":"hearthstone","index":0,"tradition":null}"""));
    }
}
