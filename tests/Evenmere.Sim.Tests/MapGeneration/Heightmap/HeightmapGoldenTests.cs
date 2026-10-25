using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.MapGeneration.Heightmap;

/// <summary>The M2-01 determinism contract: the golden hashes in <c>generated-heightmap.md</c> hold, and every
/// seed/size pair generates byte-identical arrays on a second run.</summary>
public sealed class HeightmapGoldenTests
{
    private static readonly int[] _sizes = [256, 320, 384];
    private static readonly int[] _seeds = [1, 2, 3, 4, 5];

    /// <summary>Parses the golden-hash document into (seed, size) → hash.</summary>
    /// <returns>The committed hashes.</returns>
    public static Dictionary<(int Seed, int Size), string> LoadGoldens()
    {
        string path = Path.Combine(WorldBuilder.RepoRoot, "tests", "fixtures", "maps", "generated-heightmap.md");
        var goldens = new Dictionary<(int Seed, int Size), string>();
        foreach (string line in File.ReadAllLines(path))
        {
            string[] cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length != 3 || !int.TryParse(cells[0], out int seed) || !int.TryParse(cells[1], out int size))
            {
                continue;
            }

            goldens[(seed, size)] = cells[2];
        }

        return goldens;
    }

    [Fact]
    public void GoldenFile_CoversEverySeedAndSize()
    {
        Dictionary<(int Seed, int Size), string> goldens = LoadGoldens();

        foreach (int seed in _seeds)
        {
            foreach (int size in _sizes)
            {
                goldens.ShouldContainKey((seed, size));
            }
        }

        goldens.Count.ShouldBe(_seeds.Length * _sizes.Length);
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateHeightmap_MatchesGoldenHash(int seed, int size)
    {
        Dictionary<(int Seed, int Size), string> goldens = LoadGoldens();

        HeightmapFixture.HashOf(IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content))
            .ShouldBe(goldens[(seed, size)]);
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void GenerateHeightmap_TwoRuns_AreByteIdentical(int seed, int size)
    {
        GeneratedHeightmap first = IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content);
        GeneratedHeightmap second = IslandGenerator.GenerateHeightmap(seed, size, HeightmapFixture.Content);

        second.Size.ShouldBe(first.Size);
        second.Terrain.ShouldBe(first.Terrain);
        second.Elevation.ShouldBe(first.Elevation);
        second.RiverWidth.ShouldBe(first.RiverWidth);
        second.Region.ShouldBe(first.Region);
        second.Deposit.ShouldBe(first.Deposit);
        second.Fertility.ShouldBe(first.Fertility);
    }

    /// <summary>Gets every golden (seed, size) pair.</summary>
    /// <returns>The pairs.</returns>
    public static TheoryData<int, int> SeedSizePairs()
    {
        var data = new TheoryData<int, int>();
        foreach (int seed in _seeds)
        {
            foreach (int size in _sizes)
            {
                data.Add(seed, size);
            }
        }

        return data;
    }
}
