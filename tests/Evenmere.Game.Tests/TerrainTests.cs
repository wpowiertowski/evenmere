using Evenmere.Game.Rendering.Terrain;

namespace Evenmere.Game.Tests;

public sealed class TerrainTests
{
    [Fact]
    public void GroundHeight_ScalesWithElevationStep()
    {
        TerrainSurface.GroundHeight(0).ShouldBe(0f);
        TerrainSurface.GroundHeight(2).ShouldBe(2 * TerrainSurface.ElevationStep);
    }

    [Fact]
    public void ResolveCorner_AveragesLand()
    {
        float corner = TerrainSurface.ResolveCorner(1f, 3f, 3f, 1f, 0, 4);
        corner.ShouldBe(2f);
    }

    [Fact]
    public void ResolveCorner_RiverCarvesToLowestBed()
    {
        float corner = TerrainSurface.ResolveCorner(1f, 0.1f, 1f, 1f, 0b0010, 4);
        corner.ShouldBe(0.1f);
    }

    [Fact]
    public void MeshTopHeight_RiverSitsBelowGround()
    {
        float ground = TerrainSurface.MeshTopHeight(4, WaterKind.Land);
        float bed = TerrainSurface.MeshTopHeight(4, WaterKind.River);
        bed.ShouldBeLessThan(ground);
    }

    [Fact]
    public void ChunkBuilder_FlatChunk_EmitsSixVertsPerTile()
    {
        var builder = new TerrainChunkBuilder();
        int window = TerrainChunkBuilder.WindowTiles;
        byte[] elevations = new byte[window * window];
        byte[] terrain = new byte[window * window];
        byte[] water = new byte[window * window];
        Array.Fill(elevations, (byte)4);

        builder.Rebuild(elevations, terrain, water, 96, 96, -1, -1);
        builder.VertexCount.ShouldBe(32 * 32 * 6);
        builder.IndexCount.ShouldBe(32 * 32 * 6);
    }

    [Fact]
    public void ChunkBuilder_RiverTile_LowersItsCorners()
    {
        var flat = new TerrainChunkBuilder();
        var river = new TerrainChunkBuilder();
        int window = TerrainChunkBuilder.WindowTiles;
        byte[] elevations = new byte[window * window];
        byte[] terrain = new byte[window * window];
        byte[] flatWater = new byte[window * window];
        byte[] riverWater = new byte[window * window];
        Array.Fill(elevations, (byte)4);
        riverWater[(17 * window) + 17] = (byte)WaterKind.River;

        flat.Rebuild(elevations, terrain, flatWater, 96, 96, -1, -1);
        river.Rebuild(elevations, terrain, riverWater, 96, 96, -1, -1);
        river.VertexCount.ShouldBe(flat.VertexCount);

        float flatMin = MinY(flat);
        float riverMin = MinY(river);
        riverMin.ShouldBeLessThan(flatMin);
    }

    private static float MinY(TerrainChunkBuilder builder)
    {
        System.ReadOnlySpan<float> verts = builder.Vertices;
        float min = float.MaxValue;
        for (int i = 1; i < verts.Length; i += 3)
        {
            if (verts[i] < min)
            {
                min = verts[i];
            }
        }

        return min;
    }
}
