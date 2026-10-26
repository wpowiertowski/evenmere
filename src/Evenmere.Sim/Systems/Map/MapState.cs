using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// One building in the registry: what <see cref="MapState.Buildings"/> stores per entity ID. The footprint is stored as
/// well as derived from the type, so removing a building frees exactly the tiles it took even if the content changed.
/// </summary>
[MessagePackObject]
public struct BuildingRecord
{
    /// <summary>Gets or sets the building type.</summary>
    [Key(0)]
    public BuildingTypeId Type { get; set; }

    /// <summary>Gets or sets the footprint's top-left tile.</summary>
    [Key(1)]
    public TileCoord Origin { get; set; }

    /// <summary>Gets or sets the orientation.</summary>
    [Key(2)]
    public Rotation Rotation { get; set; }

    /// <summary>Gets or sets the footprint width in tiles after rotation.</summary>
    [Key(3)]
    public int Width { get; set; }

    /// <summary>Gets or sets the footprint height in tiles after rotation.</summary>
    [Key(4)]
    public int Height { get; set; }

    /// <summary>Gets or sets the building's state.</summary>
    [Key(5)]
    public BuildingState State { get; set; }

    /// <summary>Gets or sets the construction stage; it only means something while <see cref="State"/> is <see cref="BuildingState.Site"/>, and is 0 otherwise.</summary>
    [Key(6)]
    public int Stage { get; set; }
}

/// <summary>
/// One discovery kept in the map state, copied from <c>MapDef.Discoveries</c> when the island loads (M2-06, per the M2-02b follow-up).
/// The region, kind, tile and name seed are map data, not content IDs, so the remap leaves them alone.
/// </summary>
/// <param name="Region">The region number of the tile the discovery stands on.</param>
/// <param name="Kind">The kind of discovery: natural wonder, ruins or a shipwreck.</param>
/// <param name="X">The column of the feature's representative tile.</param>
/// <param name="Y">The row of the feature's representative tile.</param>
/// <param name="NameSeed">The seed that picks the display name.</param>
[MessagePackObject]
public readonly record struct StoredDiscovery(
    [property: Key(0)] ushort Region,
    [property: Key(1)] DiscoveryKind Kind,
    [property: Key(2)] int X,
    [property: Key(3)] int Y,
    [property: Key(4)] int NameSeed);

/// <summary>
/// The map system's slice of the world state: the tile grid with its terrain layers, and the building registry with its
/// occupancy grid. Per-tile data is flattened into arrays indexed by <c>Y × Width + X</c> (rows from the top), with no
/// floats and no dictionaries, so equal maps serialise to equal bytes. A new game has an empty 0 × 0 map until
/// <c>MapSystem.LoadMap</c> fills it. The chunk dirty marks are not state and are not stored here.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class MapState
{
    /// <summary>The highest elevation step.</summary>
    internal const int MaxElevation = 15;

    /// <summary>The number of terrains a byte can hold.</summary>
    internal const int MaxTerrains = 256;

    /// <summary>Gets or sets the map width in tiles; 0 until a map is loaded.</summary>
    [Key(0)]
    public int Width { get; set; }

    /// <summary>Gets or sets the map height in tiles; 0 until a map is loaded.</summary>
    [Key(1)]
    public int Height { get; set; }

    /// <summary>Gets or sets the terrain of each tile, as a <c>TerrainId.Value</c>.</summary>
    [Key(2)]
    [ContentValued(ContentKind.Terrain)]
    public byte[] Terrain { get; set; } = [];

    /// <summary>Gets or sets the elevation of each tile, 0 to 15.</summary>
    [Key(3)]
    [NotContentIndexed("tile elevation, an integer step, not a content ID")]
    public byte[] Elevation { get; set; } = [];

    /// <summary>Gets or sets the <see cref="TileFeatures"/> bits of each tile, including the derived coast.</summary>
    [Key(4)]
    [NotContentIndexed("tile feature bits, a code enum, not content IDs")]
    public byte[] Features { get; set; } = [];

    /// <summary>Gets or sets the <see cref="RiverWidthClass"/> of each tile.</summary>
    [Key(5)]
    [NotContentIndexed("river width classes, a code enum, not content IDs")]
    public byte[] RiverWidth { get; set; } = [];

    /// <summary>Gets or sets the region number of each tile.</summary>
    [Key(6)]
    [NotContentIndexed("map-local region numbers, not content IDs")]
    public ushort[] Region { get; set; } = [];

    /// <summary>Gets or sets the deposit under each tile: 0 for none, otherwise the <see cref="DepositKind"/> value plus one.</summary>
    [Key(7)]
    [NotContentIndexed("deposit kinds, a code enum, not content IDs")]
    public byte[] Deposit { get; set; } = [];

    /// <summary>Gets or sets the fertility bit mask of each tile (bit <c>1 &lt;&lt; (int)CropKind</c> per crop).</summary>
    [Key(8)]
    [NotContentIndexed("fertility bit masks over a code enum, not content IDs")]
    public uint[] Fertility { get; set; } = [];

    /// <summary>Gets or sets the <see cref="SlopeClass"/> of each tile, derived from the elevation when the map is loaded.</summary>
    [Key(9)]
    [NotContentIndexed("slope classes, a code enum, not content IDs")]
    public byte[] Slope { get; set; } = [];

    /// <summary>Gets or sets the building registry: every building, whatever its state, by entity ID.</summary>
    [Key(10)]
    [MessagePackFormatter(typeof(IdTableFormatter<BuildingRecord>))]
    public IdTable<BuildingRecord> Buildings { get; set; } = new();

    /// <summary>Gets or sets the occupancy grid: the <c>EntityId.Value</c> of the building whose footprint covers each tile, 0 for none.</summary>
    [Key(11)]
    [NotContentIndexed("entity IDs of the covering buildings, not content IDs")]
    public int[] Occupancy { get; set; } = [];

    /// <summary>Gets or sets which guarantee attempt produced the island: 0 for hand-authored maps, otherwise the successful re-seed attempt. Copied from <c>MapDef.SubSeed</c> on load; exploration never changes it.</summary>
    [Key(12)]
    public byte SubSeed { get; set; }

    /// <summary>Gets or sets the discoveries placed on the island, in region-then-kind order, copied from <c>MapDef.Discoveries</c> on load. Exploration matches its per-feature state by index into this array.</summary>
    [Key(13)]
    [NotContentIndexed("map feature indices, not content IDs")]
    public StoredDiscovery[] Discoveries { get; set; } = [];

    /// <summary>
    /// Checks that a state read from bytes is complete and consistent with the loaded content, so a corrupt or foreign save
    /// fails at load instead of far into a run: array sizes, value ranges, and an occupancy grid that matches the registry
    /// exactly.
    /// </summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="nextEntityId">The ID the entity allocator will hand out next; every building has a smaller one.</param>
    /// <exception cref="InvalidDataException">The state is incomplete or inconsistent.</exception>
    internal void Validate(ContentDb content, int nextEntityId)
    {
        ArgumentNullException.ThrowIfNull(content);

        // MessagePack writes nil for a null member, so a damaged file can leave any reference member null.
        object?[] members = [Terrain, Elevation, Features, RiverWidth, Region, Deposit, Fertility, Slope, Buildings, Occupancy, Discoveries];
        if (Array.IndexOf(members, null) >= 0)
        {
            throw new InvalidDataException("The map state is missing a required member.");
        }

        if (Width < 0 || Height < 0 || (Width == 0) != (Height == 0))
        {
            throw new InvalidDataException($"The map is {Width} × {Height}; both sides must be positive, or both zero for no map.");
        }

        long tiles = (long)Width * Height;
        if (tiles > int.MaxValue)
        {
            throw new InvalidDataException($"The map is {Width} × {Height}, which is too large.");
        }

        if (Terrain.Length != tiles || Elevation.Length != tiles || Features.Length != tiles || RiverWidth.Length != tiles
            || Region.Length != tiles || Deposit.Length != tiles || Fertility.Length != tiles || Slope.Length != tiles || Occupancy.Length != tiles)
        {
            throw new InvalidDataException($"A map layer does not have {tiles} entries for the {Width} × {Height} map.");
        }

        ValidateTiles(content);
        ValidateBuildings(content, nextEntityId);
        ValidateDiscoveries();
    }

    private void ValidateTiles(ContentDb content)
    {
        int terrains = content.Terrains.Count;
        const int knownFeatureBits = 63;
        uint cropMask = (1u << Enum.GetValues<CropKind>().Length) - 1;
        for (int i = 0; i < Terrain.Length; i++)
        {
            if (Terrain[i] >= terrains)
            {
                throw new InvalidDataException($"Tile {i} has terrain {Terrain[i]} but the content defines {terrains} terrains.");
            }

            if (Elevation[i] > MaxElevation || Slope[i] > (byte)SlopeClass.Steep || RiverWidth[i] > (byte)RiverWidthClass.Wide
                || Deposit[i] > Enum.GetValues<DepositKind>().Length || (Features[i] & ~knownFeatureBits) != 0 || (Fertility[i] & ~cropMask) != 0)
            {
                throw new InvalidDataException($"Tile {i} holds a value that is out of range.");
            }
        }
    }

    private void ValidateBuildings(ContentDb content, int nextEntityId)
    {
        int covered = 0;
        foreach (IdTable<BuildingRecord>.Entry entry in Buildings)
        {
            BuildingRecord b = entry.Value;
            if (entry.Id.Value >= nextEntityId)
            {
                throw new InvalidDataException($"{entry.Id} is not below the next entity ID {nextEntityId}.");
            }

            if (b.Type.Value >= content.Buildings.Count || !b.Rotation.IsValid() || !Enum.IsDefined(b.State) || b.Stage < 0 || (b.Stage != 0 && b.State != BuildingState.Site) || b.Width < 1 || b.Height < 1)
            {
                throw new InvalidDataException($"{entry.Id} is not a valid building.");
            }

            if (b.Origin.X < 0 || b.Origin.Y < 0 || (long)b.Origin.X + b.Width > Width || (long)b.Origin.Y + b.Height > Height)
            {
                throw new InvalidDataException($"{entry.Id} leaves the {Width} × {Height} map.");
            }

            for (int dy = 0; dy < b.Height; dy++)
            {
                for (int dx = 0; dx < b.Width; dx++)
                {
                    if (Occupancy[((b.Origin.Y + dy) * Width) + b.Origin.X + dx] != entry.Id.Value)
                    {
                        throw new InvalidDataException($"The occupancy grid does not show {entry.Id} on ({b.Origin.X + dx}, {b.Origin.Y + dy}).");
                    }
                }
            }

            covered += b.Width * b.Height;
        }

        // Every footprint tile matched its building, so a count that differs means a tile is claimed by no building.
        int occupied = 0;
        foreach (int id in Occupancy)
        {
            if (id != 0)
            {
                occupied++;
            }
        }

        if (occupied != covered)
        {
            throw new InvalidDataException($"The occupancy grid claims {occupied} tiles but the buildings cover {covered}.");
        }
    }

    private void ValidateDiscoveries()
    {
        // Map data, not content IDs: the kind must name a DiscoveryKind and the tile must lie on the map.
        // An empty map (a test world without an island) holds no discoveries.
        foreach (StoredDiscovery discovery in Discoveries)
        {
            if (!Enum.IsDefined(discovery.Kind))
            {
                throw new InvalidDataException($"A discovery names an unknown kind {(int)discovery.Kind}.");
            }

            if (discovery.X < 0 || discovery.Y < 0 || discovery.X >= Width || discovery.Y >= Height)
            {
                throw new InvalidDataException($"A discovery at ({discovery.X}, {discovery.Y}) leaves the {Width} × {Height} map.");
            }
        }
    }
}
