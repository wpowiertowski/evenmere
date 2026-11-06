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

        builder.Rebuild(elevations, terrain, water, new float[window * window], 96, 96, -1, -1);
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
        float[] riverBeds = new float[window * window];
        riverBeds[(17 * window) + 17] = TerrainSurface.EffectiveRiverBed(
            TerrainSurface.MeshTopHeight(4, WaterKind.River),
            TerrainSurface.GroundHeight(4),
            TerrainSurface.GroundHeight(4));

        flat.Rebuild(elevations, terrain, flatWater, new float[window * window], 96, 96, -1, -1);
        river.Rebuild(elevations, terrain, riverWater, riverBeds, 96, 96, -1, -1);
        // The river tile keeps its two triangles and gains one bank wall per land edge (four edges of two
        // triangles here), so the channel meets the banks without a crack.
        river.VertexCount.ShouldBe(flat.VertexCount + 24);

        float flatMin = MinY(flat);
        float riverMin = MinY(river);
        riverMin.ShouldBeLessThan(flatMin);
    }

    [Fact]
    public void EffectiveRiverBed_WithoutNearbyLand_KeepsOwnBed()
    {
        TerrainSurface.EffectiveRiverBed(1.1f, float.PositiveInfinity, float.PositiveInfinity).ShouldBe(1.1f);
    }

    [Fact]
    public void EffectiveRiverBed_WithHighBanks_KeepsOwnBed()
    {
        // Valley river: the banks stand well above the lowered bed, so nothing changes.
        TerrainSurface.EffectiveRiverBed(1.1f, 2.0f, 2.0f).ShouldBe(1.1f);
    }

    [Fact]
    public void EffectiveRiverBed_WithLowBanks_CarvesBelowThem()
    {
        // Perched channel: the bed drops to the channel depth below the lowest nearby land.
        TerrainSurface.EffectiveRiverBed(2.1f, 1.0f, 1.0f).ShouldBe(1.0f - TerrainSurface.RiverChannelDepth);
    }

    [Fact]
    public void EffectiveRiverBed_OnSlope_CapsTheCutBelowHighBank()
    {
        // Hillside: the downhill grade sits far below, but the cut stops a bank below the uphill land
        // instead of plunging to the downhill grade.
        float bed = TerrainSurface.EffectiveRiverBed(2.1f, 0.5f, 3.0f);
        bed.ShouldBe(3.0f - TerrainSurface.RiverCarveCapDepth);
        bed.ShouldBeGreaterThan(0.5f);
    }

    [Fact]
    public void EffectiveRiverBed_SymmetricBanks_IgnoresCap()
    {
        // Banks that agree never feel the cap: the round-1 valley carve is untouched.
        TerrainSurface.EffectiveRiverBed(2.1f, 1.0f, 1.2f).ShouldBe(1.0f - TerrainSurface.RiverChannelDepth);
    }

    [Fact]
    public void StripCornerHeight_WithoutBank_RidesOverBed()
    {
        TerrainSurface.StripCornerHeight(0.4f, float.PositiveInfinity).ShouldBe(0.4f + TerrainSurface.RiverStripLift);
    }

    [Fact]
    public void StripCornerHeight_WithHighBank_RidesBelowIt()
    {
        // The water rides one freeboard under the bank, far above the bed.
        TerrainSurface.StripCornerHeight(0.4f, 2.0f).ShouldBe(2.0f - TerrainSurface.RiverBankFreeboard);
    }

    [Fact]
    public void StripCornerHeight_WithLowBank_StaysAboveBed()
    {
        // Degenerate (bank below the bed): the bank rule normally prevents this corner, but the strip must never
        // dive into the bed, so it rides at its minimum depth instead of honouring the freeboard.
        TerrainSurface.StripCornerHeight(1.6f, 1.0f).ShouldBe(1.6f + TerrainSurface.RiverWaterAboveBedMin);
    }

    [Fact]
    public void ChunkBuilder_PerchedRiver_CarvesBelowBanks()
    {
        // A river tile far above its neighbours (the starter's floating panels): every corner of the river
        // tile drops to the channel depth below the surrounding land, and the strip rides below the bank tops.
        var builder = new TerrainChunkBuilder();
        int window = TerrainChunkBuilder.WindowTiles;
        byte[] elevations = new byte[window * window];
        byte[] terrain = new byte[window * window];
        byte[] water = new byte[window * window];
        Array.Fill(elevations, (byte)2);
        water[(17 * window) + 17] = (byte)WaterKind.River;
        float[] beds = new float[window * window];
        beds[(17 * window) + 17] = TerrainSurface.EffectiveRiverBed(
            TerrainSurface.MeshTopHeight(6, WaterKind.River),
            TerrainSurface.GroundHeight(2),
            TerrainSurface.GroundHeight(2));

        builder.Rebuild(elevations, terrain, water, beds, 96, 96, -1, -1);

        float carved = TerrainSurface.GroundHeight(2) - TerrainSurface.RiverChannelDepth;
        MinY(builder).ShouldBe(carved, 1e-5);
        float strip = TerrainSurface.StripCornerHeight(carved, TerrainSurface.GroundHeight(2));
        strip.ShouldBeLessThan(TerrainSurface.GroundHeight(2));
        strip.ShouldBeGreaterThan(carved);
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
