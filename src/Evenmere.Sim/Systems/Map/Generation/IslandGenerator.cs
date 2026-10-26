using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// A generated island's heightmap stage: terrain and elevation painted by <see cref="IslandGenerator"/>,
/// with the river, region, deposit and fertility layers zeroed. M2-02 fills those layers in place and then
/// converts the finished builder into a <c>MapDef</c>. The arrays are owned by this instance: mutating them
/// mutates the island.
/// </summary>
public sealed class GeneratedHeightmap
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedHeightmap"/> class, taking ownership of the arrays.
    /// </summary>
    /// <param name="size">The width and height in tiles.</param>
    /// <param name="terrain">The terrain of each tile, <c>size × size</c> entries.</param>
    /// <param name="elevation">The elevation of each tile in steps of 0–15, <c>size × size</c> entries.</param>
    /// <param name="riverWidth">The river width class of each tile, zeroed.</param>
    /// <param name="region">The region number of each tile, zeroed.</param>
    /// <param name="deposit">The deposit under each tile, zeroed.</param>
    /// <param name="fertility">The fertility bit mask of each tile, zeroed.</param>
    /// <exception cref="ArgumentNullException">An array is <see langword="null"/>.</exception>
    public GeneratedHeightmap(
        int size,
        TerrainId[] terrain,
        byte[] elevation,
        byte[] riverWidth,
        ushort[] region,
        byte[] deposit,
        uint[] fertility)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(riverWidth);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(deposit);
        ArgumentNullException.ThrowIfNull(fertility);

        Size = size;
        Terrain = terrain;
        Elevation = elevation;
        RiverWidth = riverWidth;
        Region = region;
        Deposit = deposit;
        Fertility = fertility;
    }

    /// <summary>Gets the width and height of the square island in tiles.</summary>
    public int Size { get; }

    /// <summary>Gets the terrain of each tile, <c>Size × Size</c> entries, rows from the top edge.</summary>
    public TerrainId[] Terrain { get; }

    /// <summary>Gets the elevation of each tile in steps of 0–15, 0 for the sea.</summary>
    public byte[] Elevation { get; }

    /// <summary>Gets the river width class of each tile: 0 none, 1 narrow, 2 wide. Zeroed; M2-02 traces rivers.</summary>
    public byte[] RiverWidth { get; }

    /// <summary>Gets the region number of each tile. Zeroed; M2-02 builds regions.</summary>
    public ushort[] Region { get; }

    /// <summary>Gets the deposit under each tile: 0 for none. Zeroed; M2-02 places deposits.</summary>
    public byte[] Deposit { get; }

    /// <summary>Gets the fertility bit mask of each tile. Zeroed by the heightmap stage; painted by the terrain stage.</summary>
    public uint[] Fertility { get; }

    /// <summary>Gets the number of tiles, <c>Size × Size</c>.</summary>
    public int TileCount => Size * Size;
}

/// <summary>
/// The seeded, integer-only island generator (ADR-0003, ADR-0019). A pure function of
/// <c>(seed, size, content)</c>: the same arguments give the same arrays on every CPU. Sizes are the
/// generated-island sizes from the architecture plan: 256, 320 (the default) or 384 tiles across.
/// </summary>
public static class IslandGenerator
{
    /// <summary>The default island size in tiles across.</summary>
    public const int DefaultSize = 320;

    /// <summary>The smallest island size in tiles across.</summary>
    public const int MinSize = 256;

    /// <summary>The largest island size in tiles across.</summary>
    public const int MaxSize = 384;

    /// <summary>
    /// Generates the heightmap stage of an island: elevation from integer noise shaped by a radial
    /// falloff, and terrain from elevation bands plus moisture. River, region, deposit and fertility
    /// layers are zeroed for M2-02.
    /// </summary>
    /// <param name="seed">The generator seed.</param>
    /// <param name="size">The width and height in tiles: 256, 320 or 384.</param>
    /// <param name="content">The loaded content, which must define the island terrains.</param>
    /// <returns>The painted heightmap, owning fresh arrays on every call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not 256, 320 or 384.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain the painter needs.</exception>
    public static GeneratedHeightmap GenerateHeightmap(int seed, int size, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (size != MinSize && size != DefaultSize && size != MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A generated island is {MinSize}, {DefaultSize} or {MaxSize} tiles across.");
        }

        byte[] elevation = HeightmapBuilder.BuildElevation(size, seed);
        TerrainId[] terrain = BiomePainter.Paint(size, elevation, seed, content);

        int tiles = size * size;
        return new GeneratedHeightmap(size, terrain, elevation, new byte[tiles], new ushort[tiles], new byte[tiles], new uint[tiles]);
    }

    /// <summary>
    /// Generates the terrain stage of an island: the M2-01 heightmap plus traced rivers, placed deposits
    /// with offshore reefs, filled noise pockets, and fertility patches. Enclosed seawater pockets of four
    /// tiles or fewer are filled with neighbouring land; bigger pockets stay water. Regions and discoveries
    /// stay zeroed for M2-02b. Like <see cref="GenerateHeightmap"/>, a pure function of
    /// <c>(seed, size, content)</c>: the same arguments give the same arrays on every CPU.
    /// </summary>
    /// <param name="seed">The generator seed.</param>
    /// <param name="size">The width and height in tiles: 256, 320 or 384.</param>
    /// <param name="content">The loaded content, which must define the island terrains.</param>
    /// <returns>The painted terrain, owning fresh arrays on every call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not 256, 320 or 384.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain the painters need.</exception>
    public static GeneratedHeightmap GenerateTerrain(int seed, int size, ContentDb content)
    {
        GeneratedHeightmap map = GenerateHeightmap(seed, size, content);
        RiverTracer.Trace(map, seed, content);
        DepositPlacer.Place(map, seed, content);
        RiverTracer.FillTinyPockets(map, content);
        FertilityPainter.Paint(map, seed, content);
        return map;
    }

    /// <summary>
    /// Generates the finished island for a new game: the M2-01 heightmap plus the M2-02a terrain layers, M2-02b regions
    /// and discoveries, and a hall site chosen by the start guarantee. When the island fails the guarantee it is
    /// re-seeded with a deterministic sub-seed (recorded in <c>MapDef.SubSeed</c>), up to 16 attempts. Like the other
    /// entry points, a pure function of <c>(seed, size, content)</c>: the same arguments give the same island on every CPU.
    /// </summary>
    /// <param name="seed">The generator seed.</param>
    /// <param name="size">The width and height in tiles: 256, 320 or 384.</param>
    /// <param name="content">The loaded content, which must define the island terrains, the hall, the start goods and the discovery kinds.</param>
    /// <returns>The finished island, ready to pass as <c>NewGameOptions.Map</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not 256, 320 or 384.</exception>
    /// <exception cref="KeyNotFoundException">The content does not define a terrain, good or building this generator needs.</exception>
    /// <exception cref="InvalidOperationException">No sub-seed satisfied the start guarantee.</exception>
    public static MapDef Generate(int seed, int size, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (size != MinSize && size != DefaultSize && size != MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A generated island is {MinSize}, {DefaultSize} or {MaxSize} tiles across.");
        }

        for (int attempt = 0; attempt < StartGuarantee.MaxAttempts; attempt++)
        {
            int effective = attempt == 0 ? seed : unchecked(seed ^ (attempt * unchecked((int)0x9E3779B9)));
            GeneratedHeightmap map = GenerateTerrain(effective, size, content);
            int regions = RegionBuilder.Build(map, effective, content);
            MapDiscovery[] discoveries = DiscoveryPlacer.Place(map, effective, content);
            MapStartSite site = StartGuarantee.FindStartSite(map, content);
            MapDef island = Assemble(seed, map, content, regions, site, discoveries, (byte)attempt);
            if (StartGuarantee.IsSatisfied(island, content))
            {
                return island;
            }
        }

        throw new InvalidOperationException(
            $"Seed {seed} at size {size} failed the start guarantee on all {StartGuarantee.MaxAttempts} sub-seeds; " +
            "report the seed rather than raising the attempt cap (M2-02b).");
    }

    // Converts a finished heightmap into the immutable map definition the rest of the simulation loads: terrain
    // features by terrain name (the same rule the authored loader uses), one generated region name per region, and
    // the starter stock that every Era I island begins with. The arrays are freshly generated for this call, so they
    // transfer without copying, exactly as MapLoader does for authored maps.
    private static MapDef Assemble(
        int seed,
        GeneratedHeightmap map,
        ContentDb content,
        int regions,
        MapStartSite site,
        MapDiscovery[] discoveries,
        byte subSeed)
    {
        var features = new MapTerrainFeatures[content.Terrains.Count];
        for (int i = 0; i < features.Length; i++)
        {
            features[i] = MapTerrainRules.FeaturesOf(content.Terrains.Names[i]);
        }

        var names = new MapRegionName[regions];
        for (int r = 0; r < regions; r++)
        {
            names[r] = new MapRegionName((ushort)r, $"region.generated.{r}");
        }

        GoodAmount[] stock = new[]
        {
            new GoodAmount(content.IdOf<GoodId>("timber"), Amount.FromUnits(40)),
            new GoodAmount(content.IdOf<GoodId>("fish"), Amount.FromUnits(20)),
            new GoodAmount(content.IdOf<GoodId>("wood"), Amount.FromUnits(10)),
        };
        Array.Sort(stock, static (a, b) => a.Good.Value.CompareTo(b.Good.Value));

        return MapDef.CreateGenerated(
            $"generated_{seed}_{map.Size}",
            map.Size,
            ImmutableCollectionsMarshal.AsImmutableArray(map.Terrain),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Elevation),
            ImmutableCollectionsMarshal.AsImmutableArray(map.RiverWidth),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Region),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Deposit),
            ImmutableCollectionsMarshal.AsImmutableArray(map.Fertility),
            ImmutableCollectionsMarshal.AsImmutableArray(features),
            site,
            ImmutableCollectionsMarshal.AsImmutableArray(stock),
            ImmutableCollectionsMarshal.AsImmutableArray(names),
            subSeed,
            ImmutableCollectionsMarshal.AsImmutableArray(discoveries));
    }
}
