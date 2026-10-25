using Evenmere.Content;
using Evenmere.Core.Hashing;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.MapGeneration.Heightmap;

/// <summary>Shared fixtures for the heightmap tests: the real game content and the golden-hash function.</summary>
internal static class HeightmapFixture
{
    private static readonly Lazy<ContentDb> _content = new(() => ContentLoader.Load([ContentRoot]).GetDbOrThrow());

    /// <summary>Gets the real game content.</summary>
    public static ContentDb Content => _content.Value;

    /// <summary>Gets the folder of the real game content.</summary>
    public static string ContentRoot => Path.Combine(WorldBuilder.RepoRoot, "content");

    /// <summary>Hashes a heightmap exactly the way <c>generated-heightmap.md</c> records it.</summary>
    /// <param name="map">The generated heightmap.</param>
    /// <returns>The hash as sixteen lowercase hex digits.</returns>
    public static string HashOf(GeneratedHeightmap map)
    {
        var hasher = new StableHasher();
        foreach (TerrainId terrain in map.Terrain)
        {
            hasher.Add(Content.NameOf(terrain));
        }

        hasher.Add((ReadOnlySpan<byte>)map.Elevation);
        return hasher.ToUInt64().ToString("x16");
    }
}
