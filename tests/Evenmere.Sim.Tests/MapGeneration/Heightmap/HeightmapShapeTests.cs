using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Heightmap;

/// <summary>The shape of a generated heightmap: accepted sizes, elevation ranges, terrain/elevation
/// consistency, zeroed M2-02 layers, and a believable island (sea ring, every biome, land in band).</summary>
public sealed class HeightmapShapeTests
{
    [Theory]
    [InlineData(256)]
    [InlineData(320)]
    [InlineData(384)]
    public void GenerateHeightmap_SupportedSize_ReturnsFullGrid(int size)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(1, size, HeightmapFixture.Content);

        map.Size.ShouldBe(size);
        map.TileCount.ShouldBe(size * size);
        map.Terrain.Length.ShouldBe(size * size);
        map.Elevation.Length.ShouldBe(size * size);
        map.RiverWidth.Length.ShouldBe(size * size);
        map.Region.Length.ShouldBe(size * size);
        map.Deposit.Length.ShouldBe(size * size);
        map.Fertility.Length.ShouldBe(size * size);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(321)]
    [InlineData(512)]
    public void GenerateHeightmap_UnsupportedSize_Throws(int size)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => IslandGenerator.GenerateHeightmap(1, size, HeightmapFixture.Content));
    }

    [Fact]
    public void GenerateHeightmap_NullContent_Throws()
    {
        Should.Throw<ArgumentNullException>(() => IslandGenerator.GenerateHeightmap(1, 320, null!));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GenerateHeightmap_Elevation_StaysInRange(int seed)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, 320, HeightmapFixture.Content);

        foreach (byte elevation in map.Elevation)
        {
            elevation.ShouldBeLessThanOrEqualTo((byte)15);
        }

        map.Elevation.ShouldContain((byte)0);
        map.Elevation.ShouldContain((byte)15);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GenerateHeightmap_Terrain_MatchesElevation(int seed)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, 320, HeightmapFixture.Content);
        TerrainId sea = HeightmapFixture.Content.IdOf<TerrainId>("sea");
        TerrainId beach = HeightmapFixture.Content.IdOf<TerrainId>("beach");

        for (int i = 0; i < map.TileCount; i++)
        {
            // Sea is exactly the zero-elevation tiles; the beach is exactly the ring above them.
            (map.Terrain[i] == sea).ShouldBe(map.Elevation[i] == 0, $"tile {i}");
            if (map.Terrain[i] == beach)
            {
                map.Elevation[i].ShouldBe((byte)1, $"tile {i}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GenerateHeightmap_M2O2Layers_AreZeroed(int seed)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, 320, HeightmapFixture.Content);

        map.RiverWidth.ShouldAllBe(static b => b == 0);
        map.Region.ShouldAllBe(static r => r == 0);
        map.Deposit.ShouldAllBe(static d => d == 0);
        map.Fertility.ShouldAllBe(static f => f == 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GenerateHeightmap_ContainsEveryBiome(int seed)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, 320, HeightmapFixture.Content);
        var names = new HashSet<string>(map.Terrain.Select(t => HeightmapFixture.Content.NameOf(t)));

        names.ShouldContain("sea");
        names.ShouldContain("beach");
        names.ShouldContain("plains");
        names.ShouldContain("forest");
        names.ShouldContain("hills");
        names.ShouldContain("marsh");
        names.ShouldContain("highlands");
        names.ShouldContain("peaks");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GenerateHeightmap_LandShare_StaysInBand(int seed)
    {
        GeneratedHeightmap map = IslandGenerator.GenerateHeightmap(seed, 320, HeightmapFixture.Content);
        TerrainId sea = HeightmapFixture.Content.IdOf<TerrainId>("sea");

        int land = map.Terrain.Count(t => t != sea);
        double share = (double)land / map.TileCount;
        share.ShouldBeGreaterThan(0.30, $"seed {seed} is nearly all sea");
        share.ShouldBeLessThan(0.60, $"seed {seed} is nearly all land");
    }

    [Fact]
    public void GenerateHeightmap_ReturnsFreshArrays()
    {
        GeneratedHeightmap first = IslandGenerator.GenerateHeightmap(7, 320, HeightmapFixture.Content);
        GeneratedHeightmap second = IslandGenerator.GenerateHeightmap(7, 320, HeightmapFixture.Content);

        ReferenceEquals(first.Terrain, second.Terrain).ShouldBeFalse();
        ReferenceEquals(first.Elevation, second.Elevation).ShouldBeFalse();
        second.Terrain.ShouldBe(first.Terrain);
        second.Elevation.ShouldBe(first.Elevation);
    }
}
