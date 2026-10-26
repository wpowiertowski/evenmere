using Evenmere.Content;
using Evenmere.Core.Hashing;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Sim.Tests.Fixtures;
using System.Runtime.InteropServices;

namespace Evenmere.Sim.Tests.MapGeneration.Rivers;

/// <summary>Shared fixtures for the terrain-stage tests: the real game content and the golden-hash function.</summary>
internal static class TerrainFixture
{
    private static readonly Lazy<ContentDb> _content = new(() => ContentLoader.Load([ContentRoot]).GetDbOrThrow());

    /// <summary>Gets the real game content.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Gets the folder of the real game content.</summary>
    public static string ContentRoot => Path.Combine(WorldBuilder.RepoRoot, "content");

    /// <summary>
    /// Hashes a terrain stage exactly the way <c>fertility-goldens.md</c> records it: the river-width bytes,
    /// then the deposit bytes, then the fertility words as little-endian bytes, in tile order.
    /// </summary>
    /// <param name="map">The generated terrain.</param>
    /// <returns>The hash as sixteen lowercase hex digits.</returns>
    public static string HashOf(GeneratedHeightmap map)
    {
        var hasher = new StableHasher();
        hasher.Add((ReadOnlySpan<byte>)map.RiverWidth);
        hasher.Add((ReadOnlySpan<byte>)map.Deposit);
        hasher.Add(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(map.Fertility)));
        return hasher.ToUInt64().ToString("x16");
    }
}
