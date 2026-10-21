using Evenmere.Content.Definitions;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Map;

public sealed class TerrainAccessCheckTests
{
    [Fact]
    public void IsUsable_NoRequirement_IsAlwaysUsable()
    {
        TerrainAccessCheck.IsUsable((TerrainAccess?)null, new FakeProgressionQueries()).ShouldBeTrue();
    }

    [Fact]
    public void IsUsable_Requirement_FollowsTheAccessSet()
    {
        var access = new FakeProgressionQueries();

        TerrainAccessCheck.IsUsable(TerrainAccess.HillFarming, access).ShouldBeFalse();

        access.Open(TerrainAccess.HillFarming);

        TerrainAccessCheck.IsUsable(TerrainAccess.HillFarming, access).ShouldBeTrue();
        TerrainAccessCheck.IsUsable(TerrainAccess.Highlands, access).ShouldBeFalse();
    }

    [Theory]
    [InlineData(10, 10, null)]
    [InlineData(3, 10, TerrainAccess.MarshDrainage)]
    [InlineData(0, 0, TerrainAccess.DeepWater)]
    [InlineData(15, 14, TerrainAccess.Reefs)]
    [InlineData(11, 7, TerrainAccess.Highlands)]
    public void IsUsable_Tile_IsGatedByItsTerrainDefinition(int x, int y, TerrainAccess? gate)
    {
        var world = MapWorld.WithFixture("test_16");
        TileInfo tile = world.Queries.GetTile(x, y);
        var access = new FakeProgressionQueries();

        TerrainAccessCheck.IsUsable(tile, MapWorld.Content, access).ShouldBe(gate is null);

        if (gate is { } flag)
        {
            access.Open(flag);
            TerrainAccessCheck.IsUsable(tile, MapWorld.Content, access).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(RiverWidthClass.None, null)]
    [InlineData(RiverWidthClass.Narrow, TerrainAccess.NarrowRivers)]
    [InlineData(RiverWidthClass.Wide, TerrainAccess.WideRivers)]
    public void BridgeAccess_RiverWidth_NamesTheAccessABridgeNeeds(RiverWidthClass width, TerrainAccess? expected)
    {
        TerrainAccessCheck.BridgeAccess(width).ShouldBe(expected);
    }
}
