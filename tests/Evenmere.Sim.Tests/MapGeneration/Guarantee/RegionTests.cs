using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>The M2-02b region contract: 40–80 regions shaped by a seeded Voronoi flood, every tile claimed, and the
/// same regions on a second run.</summary>
public sealed class RegionTests
{
    /// <summary>Gets every (seed, size) pair: seeds 1–20 at all three island sizes.</summary>
    /// <returns>The pairs.</returns>
    public static TheoryData<int, int> SeedSizePairs()
    {
        var data = new TheoryData<int, int>();
        for (int seed = 1; seed <= 20; seed++)
        {
            data.Add(seed, 256);
            data.Add(seed, 320);
            data.Add(seed, 384);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void Generate_HasFortyToEightyRegions(int seed, int size)
    {
        MapDef island = IslandGenerator.Generate(seed, size, GeneratedIslandFixture.Content);

        int top = 0;
        foreach (ushort region in island.Region)
        {
            if (region > top)
            {
                top = region;
            }
        }

        int count = top + 1;
        count.ShouldBeGreaterThanOrEqualTo(RegionBuilder.MinRegions, $"seed {seed} at size {size} has only {count} regions");
        count.ShouldBeLessThanOrEqualTo(RegionBuilder.MaxRegions, $"seed {seed} at size {size} has {count} regions");
        island.RegionNames.Length.ShouldBe(count, "every region has a name entry");
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void Generate_EveryRegionIsNonEmpty(int seed, int size)
    {
        MapDef island = IslandGenerator.Generate(seed, size, GeneratedIslandFixture.Content);

        int top = 0;
        foreach (ushort region in island.Region)
        {
            if (region > top)
            {
                top = region;
            }
        }

        bool[] seen = new bool[top + 1];
        foreach (ushort region in island.Region)
        {
            seen[region] = true;
        }

        for (int r = 0; r < seen.Length; r++)
        {
            seen[r].ShouldBeTrue($"region {r} (seed {seed}) holds no tile");
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void Generate_RegionNames_AreSortedAndUnique(int seed, int size)
    {
        MapDef island = IslandGenerator.Generate(seed, size, GeneratedIslandFixture.Content);

        for (int r = 0; r < island.RegionNames.Length; r++)
        {
            island.RegionNames[r].Region.ShouldBe((ushort)r, "region names run 0, 1, 2, ...");
        }

        island.RegionNames.Select(static name => name.LocaleKey).ShouldBeUnique();
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void Generate_TwoRuns_ClaimIdenticalRegions(int seed, int size)
    {
        MapDef first = IslandGenerator.Generate(seed, size, GeneratedIslandFixture.Content);
        MapDef second = IslandGenerator.Generate(seed, size, GeneratedIslandFixture.Content);

        second.Region.ShouldBe(first.Region);
    }
}
