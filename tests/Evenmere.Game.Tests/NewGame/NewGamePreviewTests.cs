using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Hashing;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Menus.NewGame;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Game.Tests.NewGame;

/// <summary>
/// The new-game preview contract (M2-25): the thumbnail is a pure function of the island, so fixed seeds give
/// fixed hashes and the same seed gives the same bytes twice. The colour test pins the palette mapping on a
/// hand-built island without generating anything.
/// </summary>
public sealed class NewGamePreviewTests
{
    [Fact]
    public void GeneratorSeed_SmallSeeds_PassThrough()
    {
        NewGamePreview.GeneratorSeed(42).ShouldBe(42);
        NewGamePreview.GeneratorSeed(0).ShouldBe(0);
    }

    [Fact]
    public void GeneratorSeed_LargeSeeds_StayNonNegative()
    {
        NewGamePreview.GeneratorSeed(ulong.MaxValue).ShouldBe(int.MaxValue);
        NewGamePreview.GeneratorSeed((ulong)int.MaxValue + 1).ShouldBe(0);
    }

    [Fact]
    public void Render_Size_MatchesTheMap()
    {
        MapDef island = IslandGenerator.Generate(7, 256, TestContent.Db);
        byte[] rgba = NewGamePreview.Render(island, TestContent.Db);
        rgba.Length.ShouldBe(island.TileCount * 4);
    }

    [Fact]
    public void Render_SameSeedTwice_GivesTheSameBytes()
    {
        MapDef first = IslandGenerator.Generate(7, 320, TestContent.Db);
        MapDef second = IslandGenerator.Generate(7, 320, TestContent.Db);
        NewGamePreview.Render(first, TestContent.Db).ShouldBe(NewGamePreview.Render(second, TestContent.Db));
    }

    [Theory]
    [InlineData(1, "495fcb9d5b974452")]
    [InlineData(2, "88177323dc42c269")]
    [InlineData(3, "9c46206fcb51f027")]
    public void Render_FixedSeeds_HaveFixedHashes(int seed, string expected)
    {
        MapDef island = IslandGenerator.Generate(seed, 320, TestContent.Db);
        HashOf(NewGamePreview.Render(island, TestContent.Db)).ShouldBe(expected);
    }

    [Fact]
    public void Render_KnownTerrains_MatchTheSharedPalette()
    {
        // One row per terrain, in tile order: plains, forest, beach, hills, highlands, peaks, fen, marsh, reef,
        // sea, deep_water, river. The expected bytes are the terrain.* tokens of palette.tres as authored sRGB.
        string[] names =
        [
            "plains", "forest", "beach", "hills", "highlands", "peaks",
            "fen", "marsh", "reef", "sea", "deep_water", "river",
        ];

        byte[][] expected =
        [
            [115, 161, 84], [61, 115, 66], [217, 199, 147], [140, 133, 89],
            [128, 119, 102], [191, 189, 184], [140, 158, 102], [89, 107, 77],
            [64, 153, 153], [51, 107, 128], [26, 59, 97], [94, 147, 163],
        ];

        // A square island needs size x size tiles; the 12 terrains repeat across a 4 x 4 map.
        const int size = 4;
        var terrain = new TerrainId[size * size];
        for (int i = 0; i < terrain.Length; i++)
        {
            terrain[i] = TestContent.Db.Terrains.IdOf(names[i % names.Length]);
        }

        var island = MapDef.CreateGenerated(
            "preview-test",
            size,
            terrain.ToImmutableArray(),
            new byte[size * size].ToImmutableArray(),
            new byte[size * size].ToImmutableArray(),
            new ushort[size * size].ToImmutableArray(),
            new byte[size * size].ToImmutableArray(),
            new uint[size * size].ToImmutableArray(),
            new MapTerrainFeatures[TestContent.Db.Terrains.Count].ToImmutableArray(),
            new MapStartSite(0, 0, 0),
            ImmutableArray<GoodAmount>.Empty,
            ImmutableArray<MapRegionName>.Empty,
            0,
            ImmutableArray<MapDiscovery>.Empty);

        byte[] rgba = NewGamePreview.Render(island, TestContent.Db);
        rgba.Length.ShouldBe(size * 4 * size);
        for (int i = 0; i < terrain.Length; i++)
        {
            string name = names[i % names.Length];
            byte[] want = expected[i % names.Length];
            rgba[(i * 4) + 0].ShouldBe(want[0], $"tile {i} ({name}) red");
            rgba[(i * 4) + 1].ShouldBe(want[1], $"tile {i} ({name}) green");
            rgba[(i * 4) + 2].ShouldBe(want[2], $"tile {i} ({name}) blue");
            rgba[(i * 4) + 3].ShouldBe((byte)255, $"tile {i} ({name}) alpha");
        }
    }

    private static string HashOf(byte[] rgba)
    {
        var hasher = new StableHasher();
        hasher.Add(rgba);
        return $"{hasher.ToUInt64():x16}";
    }
}
