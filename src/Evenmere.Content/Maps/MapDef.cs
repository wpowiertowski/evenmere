using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Maps;

/// <summary>
/// What the map format knows about a terrain beyond its content definition (see docs/design/map-format.md, "Terrain meaning").
/// The feature bits have the same values as <c>TileFeatures</c> in the simulation, which a test pins; <see cref="Water"/>
/// is only used to derive the coast and is never stored on a tile.
/// </summary>
[Flags]
public enum MapTerrainFeatures : byte
{
    /// <summary>No special meaning.</summary>
    None = 0,

    /// <summary>A river flows over the tile (value of <c>TileFeatures.River</c>).</summary>
    River = 1,

    /// <summary>A beach tile (value of <c>TileFeatures.Beach</c>).</summary>
    Beach = 4,

    /// <summary>Deep water (value of <c>TileFeatures.DeepWater</c>).</summary>
    DeepWater = 8,

    /// <summary>A reef tile (value of <c>TileFeatures.Reef</c>).</summary>
    Reef = 16,

    /// <summary>A marsh tile (value of <c>TileFeatures.Marsh</c>).</summary>
    Marsh = 32,

    /// <summary>Open water: sea, deep water and reef. A land tile next to it is on the coast. Rivers are not open water.</summary>
    Water = 64,
}

/// <summary>Where the Founders' Hall stands when a new game starts.</summary>
/// <param name="X">The column of the footprint's top-left tile after rotation.</param>
/// <param name="Y">The row of the footprint's top-left tile after rotation.</param>
/// <param name="Rotation">The orientation in quarter turns clockwise, 0 to 3.</param>
public readonly record struct MapStartSite(int X, int Y, int Rotation);

/// <summary>The display name of one region of the map.</summary>
/// <param name="Region">The region number, 0 to 35.</param>
/// <param name="LocaleKey">The locale key of its name.</param>
public readonly record struct MapRegionName(ushort Region, string LocaleKey);

/// <summary>
/// A hand-authored map after loading and validation: immutable, with every reference resolved against a <see cref="ContentDb"/>.
/// Per-tile data is flattened into arrays indexed by <c>Y × Width + X</c>, rows from the top (north) edge. The simulation copies
/// it into its map state with <c>MapSystem.LoadMap</c>. Build one with <see cref="MapLoader"/>.
/// </summary>
public sealed class MapDef
{
    internal MapDef(
        string id,
        int width,
        int height,
        ImmutableArray<TerrainId> terrain,
        ImmutableArray<byte> elevation,
        ImmutableArray<byte> riverWidth,
        ImmutableArray<ushort> region,
        ImmutableArray<byte> deposit,
        ImmutableArray<uint> fertility,
        ImmutableArray<MapTerrainFeatures> terrainFeatures,
        MapStartSite startSite,
        ImmutableArray<GoodAmount> startStock,
        ImmutableArray<MapRegionName> regionNames)
    {
        Id = id;
        Width = width;
        Height = height;
        Terrain = terrain;
        Elevation = elevation;
        RiverWidth = riverWidth;
        Region = region;
        Deposit = deposit;
        Fertility = fertility;
        TerrainFeatures = terrainFeatures;
        StartSite = startSite;
        StartStock = startStock;
        RegionNames = regionNames;
    }

    /// <summary>Gets the map's ID, which is the name of its folder.</summary>
    public string Id { get; }

    /// <summary>Gets the width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the height in tiles.</summary>
    public int Height { get; }

    /// <summary>Gets the number of tiles, <c>Width × Height</c>.</summary>
    public int TileCount => Width * Height;

    /// <summary>Gets the terrain of each tile.</summary>
    public ImmutableArray<TerrainId> Terrain { get; }

    /// <summary>Gets the elevation of each tile in steps of 0.5 world units, 0 to 15.</summary>
    public ImmutableArray<byte> Elevation { get; }

    /// <summary>Gets the river width class of each tile: 0 none, 1 narrow, 2 wide. It is non-zero exactly on river tiles.</summary>
    public ImmutableArray<byte> RiverWidth { get; }

    /// <summary>Gets the region number of each tile, 0 to 35.</summary>
    public ImmutableArray<ushort> Region { get; }

    /// <summary>Gets the deposit under each tile: 0 for none, otherwise the <see cref="DepositKind"/> value plus one.</summary>
    public ImmutableArray<byte> Deposit { get; }

    /// <summary>Gets the fertility of each tile as a bit mask: bit <c>1 &lt;&lt; (int)CropKind</c> is set when the tile is fertile for that crop.</summary>
    public ImmutableArray<uint> Fertility { get; }

    /// <summary>Gets what the map format knows about each terrain, indexed by <c>TerrainId.Value</c>. Its length is the number of terrains in the content the map was loaded against.</summary>
    public ImmutableArray<MapTerrainFeatures> TerrainFeatures { get; }

    /// <summary>Gets where the Founders' Hall stands at the start; its footprint is inside the map on buildable terrain.</summary>
    public MapStartSite StartSite { get; }

    /// <summary>Gets the goods granted at the start, sorted by good ID, each above zero.</summary>
    public ImmutableArray<GoodAmount> StartStock { get; }

    /// <summary>Gets the names of the regions that have one, sorted by region number.</summary>
    public ImmutableArray<MapRegionName> RegionNames { get; }
}
