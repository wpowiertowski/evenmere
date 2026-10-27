using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>The M2-02c start-guarantee contract: seeds 1–100 at every size all satisfy the shared predicate —
/// every count measured over the tiles a dirt road can reach within the hall's storehouse reach, with a fishing-hut
/// site on reachable coast — and the predicate rejects islands whose start was broken on purpose. The exhaustive
/// sweep runs only when EVENMERE_FULL_SWEEP=1 (nightly); the normal suite runs seeds 1–20, which include every
/// seed that previously failed (1, 16, 18, 19 and 20 at 256).</summary>
public sealed class StartGuaranteeTests(ITestOutputHelper output)
{
    [Fact]
    public void Generate_Seeds1To100AtAllSizes_AllSatisfyTheStartGuarantee()
    {
        if (!SweepGate.FullSweepEnabled)
        {
            output.WriteLine("FULL SWEEP SKIPPED: set EVENMERE_FULL_SWEEP=1 to run seeds 1-100 (nightly covers it).");
            return;
        }

        AssertSeedsSatisfy(1, 100);
    }

    [Fact]
    public void Generate_Seeds1To20AtAllSizes_AllSatisfyTheStartGuarantee()
    {
        AssertSeedsSatisfy(1, 20);
    }

    private void AssertSeedsSatisfy(int firstSeed, int lastSeed)
    {
        foreach (int size in new[] { 256, 320, 384 })
        {
            int worstAttempts = 0;
            List<int> worstSeeds = new();
            for (int seed = firstSeed; seed <= lastSeed; seed++)
            {
                MapDef island = GeneratedIslands.Get(seed, size);
                StartGuarantee.IsSatisfied(island, GeneratedIslandFixture.Content)
                    .ShouldBeTrue($"seed {seed} at size {size} (sub-seed {island.SubSeed}) fails the start guarantee: " +
                        string.Join(", ", StartGuarantee.FailingChecks(island, GeneratedIslandFixture.Content)));
                if (island.SubSeed > worstAttempts)
                {
                    worstAttempts = island.SubSeed;
                    worstSeeds.Clear();
                }

                if (island.SubSeed == worstAttempts)
                {
                    worstSeeds.Add(seed);
                }
            }

            // Visible in the test output: how hard the guarantee worked. A seed listed here needed more than
            // the first attempt; a seed needing all 16 attempts falls back to the start repair instead of throwing.
            output.WriteLine(
                $"start guarantee at {size}: worst sub-seed {worstAttempts} on seeds {string.Join(", ", worstSeeds)}");
            worstAttempts.ShouldBeLessThan(StartGuarantee.MaxAttempts, "a seed needed every attempt");
        }
    }

    [Fact]
    public void IsSatisfied_GeneratedIsland_Holds()
    {
        MapDef island = GeneratedIslands.Get(7, 320);

        StartGuarantee.IsSatisfied(island, GeneratedIslandFixture.Content).ShouldBeTrue();
    }

    [Fact]
    public void MapLoader_HandAuthoredMap_LoadsWithDefaultValues()
    {
        MapDef island = Evenmere.Content.Maps.MapLoader
            .Load([GeneratedIslandFixture.ContentRoot], "starter", GeneratedIslandFixture.Content)
            .GetMapOrThrow();

        island.SubSeed.ShouldBe((byte)0);
        island.Discoveries.ShouldBeEmpty();
    }

    [Fact]
    public void IsSatisfied_HallInTheSea_Fails()
    {
        MapDef island = GeneratedIslands.Get(7, 320);
        int sea = FindTerrain(island, GeneratedIslandFixture.Content.IdOf<TerrainId>("sea"));

        MapDef broken = Rebuild(island, new MapStartSite(sea % island.Width, sea / island.Width, 0));

        StartGuarantee.IsSatisfied(broken, GeneratedIslandFixture.Content).ShouldBeFalse();
    }

    [Fact]
    public void IsSatisfied_BadRotation_Fails()
    {
        MapDef island = GeneratedIslands.Get(7, 320);

        MapDef broken = Rebuild(island, new MapStartSite(island.StartSite.X, island.StartSite.Y, 5));

        StartGuarantee.IsSatisfied(broken, GeneratedIslandFixture.Content).ShouldBeFalse();
    }

    [Fact]
    public void IsSatisfied_HallOffTheMap_Fails()
    {
        MapDef island = GeneratedIslands.Get(7, 320);

        MapDef broken = Rebuild(island, new MapStartSite(island.Width - 1, island.Height - 1, 0));

        StartGuarantee.IsSatisfied(broken, GeneratedIslandFixture.Content).ShouldBeFalse();
    }

    [Fact]
    public void IsSatisfied_NoOpenWater_FailsHut()
    {
        // An island with no open water anywhere has no fishing-hut site, so the hut check fails.
        MapDef island = TinyIsland(48, 4, 4);

        StartGuarantee.FailingChecks(island, GeneratedIslandFixture.Content).ShouldContain("hut");
    }

    [Fact]
    public void IsSatisfied_ReachableCoast_PassesHut()
    {
        // Open water beside road-reachable land is a valid fishing-hut site: the hut check passes. (The other
        // checks still fail on the tiny island, but none of them is the hut.)
        MapDef island = TinyIsland(48, 4, 4, (12, 8));

        StartGuarantee.FailingChecks(island, GeneratedIslandFixture.Content).ShouldNotContain("hut");
    }

    [Fact]
    public void IsSatisfied_CoastBeyondReach_FailsHut()
    {
        // Open water further than the hall's storehouse reach admits no hut site: the hut check fails.
        MapDef island = TinyIsland(64, 4, 4, (50, 50));

        StartGuarantee.FailingChecks(island, GeneratedIslandFixture.Content).ShouldContain("hut");
    }

    // A tiny synthetic island for the hut checks: plains at elevation 1 with the given sea tiles at elevation 0,
    // no rivers, one region, no deposits or fertility. Small enough to read, big enough that the far-coast case
    // sits outside the hall's storehouse reach.
    private static MapDef TinyIsland(int size, int hallX, int hallY, params (int X, int Y)[] seaTiles)
    {
        ContentDb content = GeneratedIslandFixture.Content;
        TerrainId plains = content.IdOf<TerrainId>("plains");
        TerrainId sea = content.IdOf<TerrainId>("sea");
        var terrain = new TerrainId[size * size];
        Array.Fill(terrain, plains);
        byte[] elevation = new byte[size * size];
        Array.Fill(elevation, (byte)1);
        foreach ((int X, int Y) in seaTiles)
        {
            terrain[(Y * size) + X] = sea;
            elevation[(Y * size) + X] = 0;
        }

        var features = new MapTerrainFeatures[content.Terrains.Count];
        for (int i = 0; i < features.Length; i++)
        {
            features[i] = MapTerrainRules.FeaturesOf(content.Terrains.Names[i]);
        }

        return MapDef.CreateGenerated(
            "tiny",
            size,
            [.. terrain],
            [.. elevation],
            [.. new byte[size * size]],
            [.. new ushort[size * size]],
            [.. new byte[size * size]],
            [.. new uint[size * size]],
            [.. features],
            new MapStartSite(hallX, hallY, 0),
            ImmutableArray<GoodAmount>.Empty,
            ImmutableArray<MapRegionName>.Empty,
            0,
            ImmutableArray<MapDiscovery>.Empty);
    }

    private static int FindTerrain(MapDef island, TerrainId terrain)
    {
        for (int i = 0; i < island.TileCount; i++)
        {
            if (island.Terrain[i].Equals(terrain))
            {
                return i;
            }
        }

        throw new InvalidOperationException("The island has no tile of the wanted terrain.");
    }

    private static MapDef Rebuild(MapDef island, MapStartSite site) => MapDef.CreateGenerated(
        island.Id,
        island.Width,
        island.Terrain,
        island.Elevation,
        island.RiverWidth,
        island.Region,
        island.Deposit,
        island.Fertility,
        island.TerrainFeatures,
        site,
        island.StartStock,
        island.RegionNames,
        island.SubSeed,
        island.Discoveries);
}
