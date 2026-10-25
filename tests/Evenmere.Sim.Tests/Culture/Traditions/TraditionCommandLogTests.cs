using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;

namespace Evenmere.Sim.Tests.Culture.Traditions;

/// <summary>The command log carries a slotted card through a write and a read, mapping its tradition ID.</summary>
public sealed class TraditionCommandLogTests
{
    [Fact]
    public void SetTradition_WithCard_RoundTripsThroughTheLog()
    {
        var command = new SetTradition(TraditionSlotType.Wild, 2, TraditionsPack.Content.IdOf<TraditionId>("test_wild_song"));

        using var stream = new MemoryStream();
        var writer = new CommandLogWriter(stream);
        writer.Append(42, command);
        stream.Position = 0;

        var reader = new CommandLogReader(stream, IdentityTables());
        IReadOnlyList<(long Tick, Command Command)> entries = reader.ReadAll();

        entries.ShouldHaveSingleItem();
        entries[0].Tick.ShouldBe(42);
        entries[0].Command.ShouldBe(command);
    }

    private static SaveTables IdentityTables()
    {
        ContentDb content = TraditionsPack.Content;
        var header = new SaveHeader
        {
            Goods = [.. content.Goods.Names],
            Buildings = [.. content.Buildings.Names],
            Recipes = [.. content.Recipes.Names],
            Tiers = [.. content.Tiers.Names],
            Techs = [.. content.Techs.Names],
            Civics = [.. content.Civics.Names],
            Traditions = [.. content.Traditions.Names],
            Festivals = [.. content.Festivals.Names],
            GreatWorks = [.. content.GreatWorks.Names],
            Monuments = [.. content.Monuments.Names],
            Happenings = [.. content.Happenings.Names],
            Eras = [.. content.Eras.Names],
            Terrains = [.. content.Terrains.Names],
        };
        return SaveTables.Build(header, content);
    }
}
