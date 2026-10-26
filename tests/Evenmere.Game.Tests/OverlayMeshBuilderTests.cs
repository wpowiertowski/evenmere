using Evenmere.Game.Rendering.Overlays;

namespace Evenmere.Game.Tests;

/// <summary>M2-20: the sheet tracks the heights, indexes deterministically, and tints per tile.</summary>
public sealed class OverlayMeshBuilderTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void FlatMap_LiesFlatAtLift()
    {
        var builder = new OverlayMeshBuilder();
        builder.Rebuild(new float[16], 4, 4);

        builder.VertexCount.ShouldBe(16 * 6);
        for (int i = 0; i < builder.VertexCount; i++)
        {
            builder.Vertices[(i * 3) + 1].ShouldBe(OverlayMeshBuilder.Lift, Tolerance);
        }
    }

    [Fact]
    public void Triangles_FaceUpLikeTheTerrain()
    {
        var builder = new OverlayMeshBuilder();
        builder.Rebuild(new float[4], 2, 2);

        // First tile: TL, TR, BR, TL, BR, BL — counter-clockwise from above, the terrain's winding.
        float[] verts = builder.Vertices.ToArray();
        verts[..18].ShouldBe([0f, OverlayMeshBuilder.Lift, 0f, 1f, OverlayMeshBuilder.Lift, 0f, 1f, OverlayMeshBuilder.Lift, 1f, 0f, OverlayMeshBuilder.Lift, 0f, 1f, OverlayMeshBuilder.Lift, 1f, 0f, OverlayMeshBuilder.Lift, 1f], Tolerance);
    }

    [Fact]
    public void Slope_RampsAcrossCorners()
    {
        var builder = new OverlayMeshBuilder();
        builder.Rebuild([0f, 2f, 0f, 2f], 2, 2);

        // Tile (0, 0) spans corners (0, 0)..(1, 1): the west corners average 0 and the east corners average 2.
        float[] verts = builder.Vertices.ToArray();
        verts[1].ShouldBe(OverlayMeshBuilder.Lift, Tolerance);
        verts[4].ShouldBe(1f + OverlayMeshBuilder.Lift, Tolerance);
    }

    [Fact]
    public void Normals_PointUp()
    {
        var builder = new OverlayMeshBuilder();
        builder.Rebuild(new float[4], 2, 2);

        for (int i = 0; i < builder.VertexCount; i++)
        {
            builder.Normals[(i * 3) + 0].ShouldBe(0f);
            builder.Normals[(i * 3) + 1].ShouldBe(1f);
            builder.Normals[(i * 3) + 2].ShouldBe(0f);
        }
    }

    [Fact]
    public void Uvs_SpanEachTile()
    {
        var builder = new OverlayMeshBuilder();
        builder.Rebuild(new float[4], 2, 2);

        // Tile (0, 0) spans (0, 0)..(0.5, 0.5); the last tile's last corner is its bottom-left (0.5, 1).
        float[] uvs = builder.Uvs.ToArray();
        uvs.Length.ShouldBe(4 * 6 * 2);
        uvs[..4].ShouldBe([0f, 0f, 0.5f, 0f], Tolerance);
        uvs[^2..].ShouldBe([0.5f, 1f], Tolerance);
    }

    [Fact]
    public void Retint_ColoursEachTileFlat()
    {
        var builder = new OverlayMeshBuilder();
        builder.Rebuild(new float[4], 2, 2);
        builder.Retint([new OverlaySwatch(1f, 0f, 0f, 1f), OverlaySwatch.Clear, OverlaySwatch.Clear, OverlaySwatch.Clear]);

        float[] colours = builder.Colours.ToArray();
        colours.Length.ShouldBe(4 * 6 * 4);
        for (int corner = 0; corner < 6; corner++)
        {
            colours[(corner * 4) + 0].ShouldBe(1f);
            colours[(corner * 4) + 3].ShouldBe(1f);
        }

        for (int corner = 6; corner < 24; corner++)
        {
            colours[(corner * 4) + 3].ShouldBe(0f);
        }
    }

    [Fact]
    public void WrongSize_Throws()
    {
        var builder = new OverlayMeshBuilder();
        Should.Throw<ArgumentException>(() => builder.Rebuild(new float[3], 2, 2));
        builder.Rebuild(new float[4], 2, 2);
        Should.Throw<ArgumentException>(() => builder.Retint([OverlaySwatch.Clear]));
    }
}
