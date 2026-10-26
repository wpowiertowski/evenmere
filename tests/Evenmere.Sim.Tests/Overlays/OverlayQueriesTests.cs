using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Overlays;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Roads;

namespace Evenmere.Sim.Tests.Overlays;

/// <summary>
/// The overlay layers on a tiny fixture world: a real road network over fake map tiles, read through the real
/// <see cref="OverlayQueries"/>. Reach lights the storehouse road network, coverage matches
/// <see cref="IRoadsQueries.IsCovered"/> tile for tile, and the tile layers expose the map's own masks.
/// </summary>
public sealed class OverlayQueriesTests
{
    private static OverlayHarness Harness(int size = 32) => new(size);

    [Fact]
    public void Reach_LightsRoadsWithinReachAndTheirNeighbours()
    {
        OverlayHarness h = Harness();
        h.Add("depot", 10, 10);
        h.DirtLine((11, 10), (18, 10));

        byte[] layer = h.Overlays.StorehouseReach();

        // Dirt costs 1000 a tile and the depot reaches 5 from its access tile: tiles 11..16 are in, 17..18 are out.
        // The halo past the last in-reach road is still supplied: a building at (17, 10) touches (16, 10).
        layer[h.Index(11, 10)].ShouldBe((byte)1);
        layer[h.Index(15, 10)].ShouldBe((byte)1);
        layer[h.Index(16, 10)].ShouldBe((byte)1);
        layer[h.Index(17, 10)].ShouldBe((byte)1);
        layer[h.Index(18, 10)].ShouldBe((byte)0);

        // Every tile matches the footprint rule the placement preview reads, through the same network.
        for (int y = 0; y < h.Size; y++)
        {
            for (int x = 0; x < h.Size; x++)
            {
                layer[h.Index(x, y)].ShouldBe(h.Network.IsFootprintInStorehouseReach(x, y, 1, 1) ? (byte)1 : (byte)0, $"tile ({x}, {y})");
            }
        }

        // Neighbours of reached roads read as supplied: above the road and the depot beside its access tile.
        layer[h.Index(11, 9)].ShouldBe((byte)1);
        layer[h.Index(10, 10)].ShouldBe((byte)1);

        // Far away nothing is supplied.
        layer[h.Index(0, 0)].ShouldBe((byte)0);
        layer[h.Index(31, 31)].ShouldBe((byte)0);
    }

    [Fact]
    public void Reach_ReturnsCopiesSoCallersCannotPoisonTheCache()
    {
        OverlayHarness h = Harness();
        h.Add("depot", 10, 10);
        h.DirtLine((11, 10), (14, 10));

        byte[] first = h.Overlays.StorehouseReach();
        first[h.Index(11, 10)].ShouldBe((byte)1);
        first[h.Index(11, 10)] = 0;

        h.Overlays.StorehouseReach()[h.Index(11, 10)].ShouldBe((byte)1);
    }

    [Fact]
    public void MarketCoverage_MatchesIsCoveredOnEveryNonRoadTile()
    {
        OverlayHarness h = Harness();
        h.Add("depot", 13, 12);
        h.Add("stall", 10, 10);
        h.DirtLine((5, 11), (13, 11));
        h.DirtLine((11, 8), (11, 11));

        byte[] layer = h.Overlays.MarketCoverage();

        // Covered roads read 1 too: a house on one would be covered through its neighbour, even though no house
        // may stand on a road. The layer answers the footprint question, not the occupancy one.
        layer[h.Index(12, 9)].ShouldBe((byte)1);
        layer[h.Index(12, 10)].ShouldBe((byte)1);
        layer[h.Index(11, 8)].ShouldBe((byte)1);
        layer[h.Index(0, 0)].ShouldBe((byte)0);

        for (int y = 0; y < h.Size; y++)
        {
            for (int x = 0; x < h.Size; x++)
            {
                BuildingInfo hutBuilding = h.Sim.Queries.Map.AddBuilding(new EntityId(9000 + (y * h.Size) + x), RoadContent.Building("hut"), x, y);
                EntityId hut = hutBuilding.Id;
                try
                {
                    layer[h.Index(x, y)].ShouldBe(h.Network.IsCovered(hut, ServiceKind.Market) ? (byte)1 : (byte)0, $"tile ({x}, {y})");
                }
                finally
                {
                    h.Sim.Queries.Map.RemoveBuilding(hut);
                }
            }
        }
    }

    [Fact]
    public void MarketCoverage_IsDarkWhenTheMarketHasNoStorehouseReach()
    {
        OverlayHarness h = Harness();

        // A stall with roads but no storehouse anywhere: ADR-0005 cuts its coverage.
        h.Add("stall", 10, 10);
        h.DirtLine((11, 10), (14, 10));

        h.Overlays.MarketCoverage().ShouldAllBe(static b => b == 0);
    }

    [Fact]
    public void ServiceCoverage_UsesEachServicesOwnSources()
    {
        OverlayHarness h = Harness();
        h.Add("depot", 4, 4);
        h.Add("shrine", 10, 10);
        h.DirtLine((5, 4), (11, 4));
        h.DirtLine((11, 5), (11, 10));

        byte[] shrine = h.Overlays.ServiceCoverage(ServiceKind.Shrine);
        byte[] market = h.Overlays.ServiceCoverage(ServiceKind.Market);

        // The shrine (coverage 14) reaches the road beside it; its neighbour is lit.
        // No market exists, so that layer is dark.
        shrine[h.Index(10, 9)].ShouldBe((byte)1);
        shrine[h.Index(0, 0)].ShouldBe((byte)0);
        market.ShouldAllBe(static b => b == 0);
    }

    [Fact]
    public void Fertility_ExposesTheTilesOwnMaskPerCrop()
    {
        OverlayHarness h = Harness();
        uint grain = TileInfo.FertilityBit(CropKind.Grain);
        h.SetTile(5, 5, OverlayHarness.Tile("plains", fertility: grain));
        h.SetTile(6, 6, OverlayHarness.Tile("plains", fertility: grain | TileInfo.FertilityBit(CropKind.Apples)));

        byte[] grainLayer = h.Overlays.Fertility(CropKind.Grain);
        grainLayer[h.Index(5, 5)].ShouldBe((byte)1);
        grainLayer[h.Index(6, 6)].ShouldBe((byte)1);
        grainLayer[h.Index(7, 7)].ShouldBe((byte)0);

        byte[] applesLayer = h.Overlays.Fertility(CropKind.Apples);
        applesLayer[h.Index(5, 5)].ShouldBe((byte)0);
        applesLayer[h.Index(6, 6)].ShouldBe((byte)1);
    }

    [Fact]
    public void Deposits_EncodeTheKindPlusOne()
    {
        OverlayHarness h = Harness();
        h.SetTile(5, 5, OverlayHarness.Tile("plains", deposit: DepositKind.Stone));

        byte[] layer = h.Overlays.Deposits();
        layer[h.Index(5, 5)].ShouldBe((byte)((int)DepositKind.Stone + 1));
        layer[h.Index(6, 6)].ShouldBe((byte)0);
    }

    [Fact]
    public void Buildable_NeedsTerrainSlopeAndAccess()
    {
        OverlayHarness h = Harness();
        h.SetTile(5, 5, OverlayHarness.Tile("sea"));
        h.SetTile(6, 6, OverlayHarness.Tile("plains", slope: SlopeClass.Steep));
        h.SetTile(7, 7, OverlayHarness.Tile("highlands"));

        byte[] layer = h.Overlays.BuildableWithAccess();

        // Plains are open; sea is not buildable, steep is too steep, highlands need their access.
        layer[h.Index(4, 4)].ShouldBe((byte)1);
        layer[h.Index(5, 5)].ShouldBe((byte)0);
        layer[h.Index(6, 6)].ShouldBe((byte)0);
        layer[h.Index(7, 7)].ShouldBe((byte)0);

        h.Sim.Queries.Progression.Open(TerrainAccess.Highlands);

        // The buildable layer is cached by tick: research lands on a tick boundary in production, so the test steps one.
        h.Sim.SetTick(1);
        h.Overlays.BuildableWithAccess()[h.Index(7, 7)].ShouldBe((byte)1);
    }

    [Fact]
    public void UnknownKinds_Throw()
    {
        OverlayHarness h = Harness();
        Should.Throw<ArgumentOutOfRangeException>(() => h.Overlays.ServiceCoverage((ServiceKind)99));
        Should.Throw<ArgumentOutOfRangeException>(() => h.Overlays.Fertility((CropKind)99));
    }

    [Fact]
    public void WidthAndHeight_FollowTheMap()
    {
        OverlayHarness h = Harness(16);
        h.Overlays.Width.ShouldBe(16);
        h.Overlays.Height.ShouldBe(16);
        h.Overlays.StorehouseReach().Length.ShouldBe(256);
    }
}
