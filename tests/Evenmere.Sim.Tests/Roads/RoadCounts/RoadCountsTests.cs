using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Roads;

namespace Evenmere.Sim.Tests.Roads.RoadCounts;

/// <summary>
/// The per-type road counts (M2-09): placing, upgrading and removing tiles moves the counts, which survive a save
/// round trip and are rebuilt from the layer when an older save carries none.
/// </summary>
public sealed class RoadCountsTests
{
    [Fact]
    public void Place_CountsTilesOfThatType()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Dirt, RoadsHarness.Cobbled);
        h.DirtLine((0, 0), (4, 0));

        h.Queries.RoadTiles(RoadsHarness.Dirt).ShouldBe(5);
        h.Queries.RoadTiles(RoadsHarness.Cobbled).ShouldBe(0);
    }

    [Fact]
    public void Upgrade_MovesTilesFromTheOldTypeToTheNewOne()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Dirt, RoadsHarness.Cobbled);
        h.DirtLine((0, 0), (4, 0));
        h.Line(RoadsHarness.Cobbled, (0, 0), (4, 0));

        h.Queries.RoadTiles(RoadsHarness.Dirt).ShouldBe(0);
        h.Queries.RoadTiles(RoadsHarness.Cobbled).ShouldBe(5);
    }

    [Fact]
    public void Remove_DecrementsTheCounts()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Dirt, RoadsHarness.Cobbled);
        h.DirtLine((0, 0), (4, 0));
        h.Line(RoadsHarness.Cobbled, (5, 0), (9, 0));
        h.RemoveRoads((1, 0), (6, 0));

        h.Queries.RoadTiles(RoadsHarness.Dirt).ShouldBe(4);
        h.Queries.RoadTiles(RoadsHarness.Cobbled).ShouldBe(4);
    }

    [Fact]
    public void ReplacingTheSameType_LeavesTheCountsAlone()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Dirt);
        h.DirtLine((0, 0), (4, 0));
        h.DirtLine((0, 0), (4, 0));

        h.Queries.RoadTiles(RoadsHarness.Dirt).ShouldBe(5);
    }

    [Fact]
    public void RoadTiles_OfANonRoadType_ReadsZero()
    {
        var h = new RoadsHarness();

        h.Queries.RoadTiles(RoadContent.Building("hut")).ShouldBe(0);
    }

    [Fact]
    public void Save_RoundTrip_KeepsTheCountsAndTheHash()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Dirt, RoadsHarness.Cobbled);
        h.DirtLine((0, 0), (4, 0));
        h.Line(RoadsHarness.Cobbled, (5, 0), (9, 0));

        ulong hash = h.StateHash();
        WorldState restored = StateSerializer.Read(StateSerializer.Write(h.Sim.World), h.Sim.Content);

        restored.Roads.TilesByRoadType.ShouldBe(h.Sim.World.Roads.TilesByRoadType);
        StateSerializer.ComputeHash(restored).ShouldBe(hash);
    }

    [Fact]
    public void Load_AStateWithoutCounts_RebuildsThemFromTheLayer()
    {
        var h = new RoadsHarness();
        h.Unlock(RoadsHarness.Dirt, RoadsHarness.Cobbled);
        h.DirtLine((0, 0), (4, 0));
        h.Line(RoadsHarness.Cobbled, (5, 0), (9, 0));

        // An older save carries a layer but no counts, exactly as the M1 format does.
        var old = new RoadsState
        {
            Width = h.Sim.World.Roads.Width,
            Height = h.Sim.World.Roads.Height,
            Layer = (ushort[])h.Sim.World.Roads.Layer.Clone(),
            TilesByRoadType = [],
        };
        WorldState restored = StateSerializer.Read(StateSerializer.Write(h.Sim.World), h.Sim.Content);
        restored.Roads = old;
        var reloaded = new RoadsSystem(restored.Roads, h.Sim.Content, () => h.Sim.Queries);

        reloaded.Queries.RoadTiles(RoadsHarness.Dirt).ShouldBe(5);
        reloaded.Queries.RoadTiles(RoadsHarness.Cobbled).ShouldBe(5);
    }
}
