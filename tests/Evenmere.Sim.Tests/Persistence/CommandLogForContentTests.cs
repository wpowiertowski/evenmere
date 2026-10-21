using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>The same-content log reader: writer and <c>ForContent</c> round-trip (tick, command) pairs.</summary>
public sealed class CommandLogForContentTests
{
    [Fact]
    public void ForContent_RoundTrip_PreservesTicksAndCommands()
    {
        ContentDb content = SiteFinder.Content;
        var expected = new List<(long Tick, Command Command)>
        {
            (1, new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 20, 96, Rotation.Deg0)),
            (100, new PlaceRoad([new TileCoord(1, 2), new TileCoord(1, 3)], content.Required.DirtRoad)),
        };

        using var stream = new MemoryStream();
        var writer = new CommandLogWriter(stream);
        foreach ((long tick, Command command) in expected)
        {
            writer.Append(tick, command);
        }

        stream.Position = 0;
        IReadOnlyList<(long Tick, Command Command)> entries = CommandLogReader.ForContent(stream, content);

        entries.Count.ShouldBe(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            entries[i].Tick.ShouldBe(expected[i].Tick);
            entries[i].Command.ShouldBe(expected[i].Command);
        }
    }
}
