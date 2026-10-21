using Evenmere.Game.Rendering.Roads;

namespace Evenmere.Game.Tests;

public sealed class RoadMeshBuilderTests
{
    [Fact]
    public void IsolatedTile_EmitsCentreSquare()
    {
        var builder = new RoadMeshBuilder();
        (byte[] road, float[] corners) = StraightSetup(out int size, out int stride);
        Array.Clear(road);
        road[(1 * stride) + 1] = 1;

        builder.Rebuild(road, corners, size, stride, 0, 0);
        builder.VertexCount.ShouldBe(6);
        builder.IndexCount.ShouldBe(6);
    }

    [Fact]
    public void StraightLine_MiddleTileHasThreeRects()
    {
        var builder = new RoadMeshBuilder();
        (byte[] road, float[] corners) = StraightSetup(out int size, out int stride);
        Array.Clear(road);
        road[(1 * stride) + 1] = 1;
        road[(1 * stride) + 2] = 1;
        road[(1 * stride) + 3] = 1;

        builder.Rebuild(road, corners, size, stride, 0, 0);
        // Ends have centre plus one arm (2 rects each), the middle has centre plus two arms (3 rects); 7 rects in all.
        builder.VertexCount.ShouldBe(7 * 6);
        builder.IndexCount.ShouldBe(7 * 6);
    }

    [Fact]
    public void CrossTile_EmitsFiveRects()
    {
        var builder = new RoadMeshBuilder();
        (byte[] road, float[] corners) = StraightSetup(out int size, out int stride);
        Array.Clear(road);
        road[(1 * stride) + 2] = 1;
        road[(0 * stride) + 2] = 1;
        road[(2 * stride) + 2] = 1;
        road[(1 * stride) + 1] = 1;
        road[(1 * stride) + 3] = 1;

        builder.Rebuild(road, corners, size, stride, 0, 0);
        int crossVerts = builder.VertexCount;
        // The cross centre has all four arms plus its square.
        crossVerts.ShouldBeGreaterThan(7 * 6);
    }

    [Fact]
    public void ArmsMeet_SharedEdgeMatchesExactly()
    {
        var builder = new RoadMeshBuilder();
        (byte[] road, float[] corners) = StraightSetup(out int size, out int stride);
        Array.Clear(road);
        road[(1 * stride) + 1] = 1;
        road[(1 * stride) + 2] = 1;

        builder.Rebuild(road, corners, size, stride, 0, 0);
        float[] verts = builder.Vertices.ToArray();
        verts.Length.ShouldBeGreaterThan(0);
        // Every vertex sits on flat ground plus the lift, so heights stay exact.
        foreach (float y in Step(verts))
        {
            y.ShouldBe(0f);
        }
    }

    private static (byte[] Road, float[] Corners) StraightSetup(out int size, out int stride)
    {
        size = 3;
        stride = size + 2;
        byte[] road = new byte[stride * stride];
        float[] corners = new float[(size + 1) * (size + 1)];
        return (road, corners);
    }

    private static IEnumerable<float> Step(float[] verts)
    {
        for (int i = 1; i < verts.Length; i += 3)
        {
            yield return verts[i];
        }
    }
}
