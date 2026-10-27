using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>The M2-02b discovery contract: wonders on the heights, ruins on buildable land, wrecks on shore seas, in
/// the committed count ranges, sorted by region then kind, and identical on a second run.</summary>
public sealed class DiscoveryTests
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
    public void Generate_Discoveries_AreInRange(int seed, int size)
    {
        MapDef island = GeneratedIslands.Get(seed, size);

        int wonders = island.Discoveries.Count(static d => d.Kind == DiscoveryKind.NaturalWonder);
        int ruins = island.Discoveries.Count(static d => d.Kind == DiscoveryKind.Ruin);
        int wrecks = island.Discoveries.Count(static d => d.Kind == DiscoveryKind.Shipwreck);
        wonders.ShouldBeGreaterThanOrEqualTo(DiscoveryPlacer.MinWonders);
        wonders.ShouldBeLessThanOrEqualTo(DiscoveryPlacer.MaxWonders);
        ruins.ShouldBeGreaterThanOrEqualTo(DiscoveryPlacer.MinRuins);
        ruins.ShouldBeLessThanOrEqualTo(DiscoveryPlacer.MaxRuins);
        wrecks.ShouldBeGreaterThanOrEqualTo(DiscoveryPlacer.MinWrecks);
        wrecks.ShouldBeLessThanOrEqualTo(DiscoveryPlacer.MaxWrecks);
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void Generate_Discoveries_AreSortedAndValid(int seed, int size)
    {
        MapDef island = GeneratedIslands.Get(seed, size);
        ContentDb content = GeneratedIslandFixture.Content;

        ushort top = 0;
        foreach (ushort region in island.Region)
        {
            if (region > top)
            {
                top = region;
            }
        }

        MapDiscovery? previous = null;
        var tiles = new HashSet<int>();
        foreach (MapDiscovery discovery in island.Discoveries)
        {
            discovery.Region.ShouldBeLessThanOrEqualTo(top, "every discovery sits in a real region");
            island.Region[(discovery.Y * island.Width) + discovery.X].ShouldBe(
                discovery.Region, $"discovery at ({discovery.X}, {discovery.Y}) records its tile's region");
            tiles.Add((discovery.Y * island.Width) + discovery.X).ShouldBeTrue("two discoveries share a tile");
            if (previous is not null)
            {
                (discovery.Region > previous.Region
                    || (discovery.Region == previous.Region && discovery.Kind >= previous.Kind))
                    .ShouldBeTrue("discoveries run sorted by region then kind");
            }

            previous = discovery;
            CheckGround(island, content, discovery);
        }
    }

    [Theory]
    [MemberData(nameof(SeedSizePairs))]
    public void Generate_TwoRuns_PlaceIdenticalDiscoveries(int seed, int size)
    {
        MapDef first = IslandGenerator.Generate(seed, size, GeneratedIslandFixture.Content);
        MapDef second = IslandGenerator.Generate(seed, size, GeneratedIslandFixture.Content);

        second.Discoveries.ShouldBe(first.Discoveries);
    }

    private static void CheckGround(MapDef island, ContentDb content, MapDiscovery discovery)
    {
        int tile = (discovery.Y * island.Width) + discovery.X;
        switch (discovery.Kind)
        {
            case DiscoveryKind.NaturalWonder:
                ((int)island.Elevation[tile]).ShouldBeGreaterThanOrEqualTo(
                    DiscoveryPlacer.WonderElevation, $"the wonder at ({discovery.X}, {discovery.Y}) crowns no height");
                break;
            case DiscoveryKind.Ruin:
                island.RiverWidth[tile].ShouldBe((byte)0, "no ruin stands on a river");
                content.Get(island.Terrain[tile]).Buildable
                    .ShouldBeTrue($"the ruin at ({discovery.X}, {discovery.Y}) stands on unbuildable ground");
                break;
            case DiscoveryKind.Shipwreck:
                island.Terrain[tile].ShouldBe(
                    content.IdOf<TerrainId>("sea"), $"the wreck at ({discovery.X}, {discovery.Y}) lies on no sea");
                HasLandNeighbour(island, discovery.X, discovery.Y)
                    .ShouldBeTrue($"the wreck at ({discovery.X}, {discovery.Y}) lies on the open sea");
                break;
            default:
                Assert.Fail($"unexpected discovery kind {discovery.Kind}");
                break;
        }
    }

    private static bool HasLandNeighbour(MapDef island, int x, int y)
    {
        TerrainId sea = GeneratedIslandFixture.Content.IdOf<TerrainId>("sea");
        TerrainId reef = GeneratedIslandFixture.Content.IdOf<TerrainId>("reef");
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if ((uint)nx < (uint)island.Width && (uint)ny < (uint)island.Height)
                {
                    TerrainId neighbour = island.Terrain[(ny * island.Width) + nx];
                    if (!neighbour.Equals(sea) && !neighbour.Equals(reef))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
