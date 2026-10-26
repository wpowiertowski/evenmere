using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>The M2-02b start-guarantee contract: seeds 1–100 at the default size all satisfy the shared predicate, and
/// the predicate rejects islands whose start was broken on purpose.</summary>
public sealed class StartGuaranteeTests(ITestOutputHelper output)
{
    [Fact]
    public void Generate_Seeds1To100At320_AllSatisfyTheStartGuarantee()
    {
        int worstAttempts = 0;
        var worstSeeds = new List<int>();
        for (int seed = 1; seed <= 100; seed++)
        {
            MapDef island = IslandGenerator.Generate(seed, 320, GeneratedIslandFixture.Content);
            StartGuarantee.IsSatisfied(island, GeneratedIslandFixture.Content)
                .ShouldBeTrue($"seed {seed} (sub-seed {island.SubSeed}) fails the start guarantee: " +
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

        // Visible in the test output: how hard the guarantee worked. A seed listed here needed more than the
        // first attempt; a seed needing all 16 attempts throws inside Generate instead (M2-02b: report the seed).
        output.WriteLine(
            $"start guarantee at 320: worst sub-seed {worstAttempts} on seeds {string.Join(", ", worstSeeds)}");
        worstAttempts.ShouldBeLessThan(StartGuarantee.MaxAttempts, "a seed needed every attempt");
    }

    [Fact]
    public void IsSatisfied_GeneratedIsland_Holds()
    {
        MapDef island = IslandGenerator.Generate(7, 320, GeneratedIslandFixture.Content);

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
        MapDef island = IslandGenerator.Generate(7, 320, GeneratedIslandFixture.Content);
        int sea = FindTerrain(island, GeneratedIslandFixture.Content.IdOf<TerrainId>("sea"));

        MapDef broken = Rebuild(island, new MapStartSite(sea % island.Width, sea / island.Width, 0));

        StartGuarantee.IsSatisfied(broken, GeneratedIslandFixture.Content).ShouldBeFalse();
    }

    [Fact]
    public void IsSatisfied_BadRotation_Fails()
    {
        MapDef island = IslandGenerator.Generate(7, 320, GeneratedIslandFixture.Content);

        MapDef broken = Rebuild(island, new MapStartSite(island.StartSite.X, island.StartSite.Y, 5));

        StartGuarantee.IsSatisfied(broken, GeneratedIslandFixture.Content).ShouldBeFalse();
    }

    [Fact]
    public void IsSatisfied_HallOffTheMap_Fails()
    {
        MapDef island = IslandGenerator.Generate(7, 320, GeneratedIslandFixture.Content);

        MapDef broken = Rebuild(island, new MapStartSite(island.Width - 1, island.Height - 1, 0));

        StartGuarantee.IsSatisfied(broken, GeneratedIslandFixture.Content).ShouldBeFalse();
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
