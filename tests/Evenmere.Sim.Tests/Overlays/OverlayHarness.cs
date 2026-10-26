using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Overlays;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Roads;

namespace Evenmere.Sim.Tests.Overlays;

/// <summary>
/// The real <see cref="OverlayQueries"/> over a <see cref="RoadsHarness"/>: the roads test content, a real road
/// network fed by the fake building registry, and settable map tiles. Tests arrange through it and read layers
/// through <see cref="Overlays"/>.
/// </summary>
internal sealed class OverlayHarness
{
    private readonly RoadsHarness _roads;

    /// <summary>Initializes a new instance of the <see cref="OverlayHarness"/> class.</summary>
    /// <param name="size">The width and height of the square map.</param>
    public OverlayHarness(int size = 32)
    {
        _roads = new RoadsHarness(size);
        Size = size;
        Overlays = new OverlayQueries(
            _roads.Sim.Content,
            () => _roads.Sim.Queries,
            _roads.Network,
            () => _roads.Sim.World.Clock.Tick);
    }

    /// <summary>Gets the map side in tiles.</summary>
    public int Size { get; }

    /// <summary>Gets the overlay queries under test.</summary>
    public IOverlayQueries Overlays { get; }

    /// <summary>Gets the fake simulation, for progression access and direct map edits.</summary>
    public FakeSim Sim => _roads.Sim;

    /// <summary>Gets the real road network, for the exact rules the layers mirror.</summary>
    public RoadNetwork Network => _roads.Network;

    /// <summary>Gets the row-major index of a tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    public int Index(int x, int y) => (y * Size) + x;

    /// <summary>Adds a finished building through the fake registry, which tells the road network.</summary>
    /// <param name="type">The building type's string ID.</param>
    /// <param name="x">The column of the top-left tile.</param>
    /// <param name="y">The row of the top-left tile.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId Add(string type, int x, int y) => _roads.Add(type, x, y);

    /// <summary>Lays a straight dirt road along a row or a column.</summary>
    /// <param name="from">The first tile.</param>
    /// <param name="to">The last tile; it shares a row or a column with <paramref name="from"/>.</param>
    public void DirtLine((int X, int Y) from, (int X, int Y) to) => _roads.DirtLine(from, to);

    /// <summary>Makes a tile.</summary>
    /// <param name="terrain">The terrain's string ID.</param>
    /// <param name="slope">The slope class.</param>
    /// <param name="deposit">The deposit, if any.</param>
    /// <param name="fertility">The fertility mask.</param>
    public static TileInfo Tile(string terrain, SlopeClass slope = SlopeClass.Flat, DepositKind? deposit = null, uint fertility = 0) =>
        new(RoadContent.Terrain(terrain), 0, slope, TileFeatures.None, RiverWidthClass.None, 0, deposit, fertility);

    /// <summary>Paints one tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="tile">The tile.</param>
    public void SetTile(int x, int y, TileInfo tile) => Sim.Queries.Map.SetTile(x, y, tile);
}
