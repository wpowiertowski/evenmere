using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Hashing;
using Evenmere.Core.Ids;
using Evenmere.Sim.Tests.Fixtures;
using System.Runtime.InteropServices;

namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>Shared fixtures for the M2-02b tests: the real game content and the finished-island golden-hash function.</summary>
internal static class GeneratedIslandFixture
{
    private static readonly Lazy<ContentDb> _content = new(() => ContentLoader.Load([ContentRoot]).GetDbOrThrow());

    /// <summary>Gets the real game content.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Gets the folder of the real game content.</summary>
    public static string ContentRoot => Path.Combine(WorldBuilder.RepoRoot, "content");

    /// <summary>
    /// Hashes a finished island exactly the way <c>generated-mapdef.md</c> records it: the terrain string IDs in tile
    /// order, then the elevation, river-width, region, deposit and fertility bytes, then the hall site, the sub-seed,
    /// the discoveries, the region names and the start stock. Terrain names (not numeric IDs) are hashed so the
    /// goldens survive content ID reassignment.
    /// </summary>
    /// <param name="map">The finished island.</param>
    /// <returns>The hash as sixteen lowercase hex digits.</returns>
    public static string HashOf(MapDef map)
    {
        var hasher = new StableHasher();
        foreach (TerrainId terrain in map.Terrain)
        {
            hasher.Add(Content.NameOf(terrain));
        }

        hasher.Add((ReadOnlySpan<byte>)map.Elevation.AsSpan());
        hasher.Add((ReadOnlySpan<byte>)map.RiverWidth.AsSpan());
        hasher.Add(MemoryMarshal.AsBytes(map.Region.AsSpan()));
        hasher.Add((ReadOnlySpan<byte>)map.Deposit.AsSpan());
        hasher.Add(MemoryMarshal.AsBytes(map.Fertility.AsSpan()));
        hasher.Add(map.StartSite.X);
        hasher.Add(map.StartSite.Y);
        hasher.Add(map.StartSite.Rotation);
        hasher.Add(map.SubSeed);
        foreach (MapDiscovery discovery in map.Discoveries)
        {
            hasher.Add(discovery.Region);
            hasher.Add((int)discovery.Kind);
            hasher.Add(discovery.X);
            hasher.Add(discovery.Y);
            hasher.Add(discovery.NameSeed);
        }

        foreach (MapRegionName name in map.RegionNames)
        {
            hasher.Add(name.Region);
            hasher.Add(name.LocaleKey);
        }

        foreach (GoodAmount stock in map.StartStock)
        {
            hasher.Add(Content.NameOf(stock.Good));
            hasher.Add(stock.Amount.Milli);
        }

        return hasher.ToUInt64().ToString("x16");
    }
}
