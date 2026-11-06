using Evenmere.Game.Rendering.Terrain;

namespace Evenmere.Game.Tests;

/// <summary>Reads one rebuilt <see cref="TerrainChunkBuilder"/> chunk back into per-corner heights, shared by the mesh
/// geometry tests so they all parse the same layout the builder emits: tiles in ascending (y, x) order with six
/// vertices each, each river tile's bank walls (six vertices per walled edge) right after its triangles.</summary>
internal static class ChunkMeshReader
{
    /// <summary>A bank wall quad: the bank edge endpoints with their bank (top) and bed (bottom) heights.</summary>
    /// <param name="Ax">The first endpoint column.</param>
    /// <param name="Ay">The first endpoint row.</param>
    /// <param name="Bx">The second endpoint column.</param>
    /// <param name="By">The second endpoint row.</param>
    /// <param name="TopA">The bank height at the first endpoint.</param>
    /// <param name="TopB">The bank height at the second endpoint.</param>
    /// <param name="BottomA">The bed height at the first endpoint.</param>
    /// <param name="BottomB">The bed height at the second endpoint.</param>
    internal sealed record WallFace(int Ax, int Ay, int Bx, int By, float TopA, float TopB, float BottomA, float BottomB);

    /// <summary>
    /// Reads the corners of one rebuilt chunk into the map-wide height tables: river tile corners into
    /// <paramref name="bedH"/>, every other tile's corners into <paramref name="landH"/>, bank walls appended to
    /// <paramref name="walls"/> (when non-null). Tables are indexed by <c>vy * (width + 1) + vx</c> and start as NaN.
    /// </summary>
    /// <param name="verts">The builder's vertices after <c>Rebuild</c>.</param>
    /// <param name="kinds">The <see cref="WaterKind"/> value per map tile.</param>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    /// <param name="firstX">The chunk's first tile column.</param>
    /// <param name="firstY">The chunk's first tile row.</param>
    /// <param name="landH">Receives the bank corners.</param>
    /// <param name="bedH">Receives the channel corners.</param>
    /// <param name="walls">Receives the bank walls, or null to skip them.</param>
    /// <param name="inconsistencies">Receives a message per structural mismatch.</param>
    /// <returns>How many vertices were consumed.</returns>
    internal static int ReadCorners(
        ReadOnlySpan<float> verts,
        byte[] kinds,
        int width,
        int height,
        int firstX,
        int firstY,
        float[] landH,
        float[] bedH,
        List<WallFace>? walls,
        List<string> inconsistencies)
    {
        int consumed = 0;
        for (int wy = 1; wy <= TerrainChunkBuilder.ChunkTiles; wy++)
        {
            for (int wx = 1; wx <= TerrainChunkBuilder.ChunkTiles; wx++)
            {
                int tileX = firstX + wx - 1;
                int tileY = firstY + wy - 1;
                if (tileX < 0 || tileY < 0 || tileX >= width || tileY >= height)
                {
                    continue;
                }

                bool river = kinds[(tileY * width) + tileX] == (byte)WaterKind.River;
                for (int c = 0; c < 4; c++)
                {
                    int vx = tileX + (c % 2);
                    int vy = tileY + (c / 2);
                    float h = CornerFromVerts(verts, consumed, tileX, tileY, vx, vy, inconsistencies);
                    if (float.IsNaN(h))
                    {
                        continue;
                    }

                    float[] target = river ? bedH : landH;
                    int vi = (vy * (width + 1)) + vx;
                    if (float.IsNaN(target[vi]))
                    {
                        target[vi] = h;
                    }
                    else if (Math.Abs(target[vi] - h) > 1e-4f)
                    {
                        inconsistencies.Add($"corner ({vx}, {vy}) drew {target[vi]} then {h}");
                    }
                }

                consumed += 6;
                if (river && walls is not null)
                {
                    consumed += ConsumeWalls(verts, consumed, tileX, tileY, width, height, kinds, walls, inconsistencies);
                }
                else if (river)
                {
                    consumed += SkipWalls(tileX, tileY, width, height, kinds);
                }
            }
        }

        return consumed;
    }

    private static float CornerFromVerts(
        ReadOnlySpan<float> verts, int firstVertex, int tileX, int tileY, int vx, int vy, List<string> inconsistencies)
    {
        for (int k = 0; k < 6; k++)
        {
            int v = firstVertex + k;
            if (Math.Abs(verts[(v * 3) + 0] - vx) < 1e-6f && Math.Abs(verts[(v * 3) + 2] - vy) < 1e-6f)
            {
                return verts[(v * 3) + 1];
            }
        }

        inconsistencies.Add($"tile ({tileX}, {tileY}) emits no vertex at corner ({vx}, {vy})");
        return float.NaN;
    }

    // One river tile's bank walls: six vertices per edge that does not border another river tile, in the builder's
    // order (north, east, south, west). Returns how many vertices were consumed.
    private static int ConsumeWalls(
        ReadOnlySpan<float> verts, int firstVertex, int tileX, int tileY, int width, int height, byte[] kinds,
        List<WallFace> walls, List<string> inconsistencies)
    {
        int consumed = 0;
        consumed += ConsumeWall(verts, firstVertex + consumed, tileX, tileY, tileX + 1, tileY, tileX, tileY - 1, width, height, kinds, walls, inconsistencies);
        consumed += ConsumeWall(verts, firstVertex + consumed, tileX + 1, tileY, tileX + 1, tileY + 1, tileX + 1, tileY, width, height, kinds, walls, inconsistencies);
        consumed += ConsumeWall(verts, firstVertex + consumed, tileX, tileY + 1, tileX + 1, tileY + 1, tileX, tileY + 1, width, height, kinds, walls, inconsistencies);
        consumed += ConsumeWall(verts, firstVertex + consumed, tileX, tileY, tileX, tileY + 1, tileX - 1, tileY, width, height, kinds, walls, inconsistencies);
        return consumed;
    }

    private static int SkipWalls(int tileX, int tileY, int width, int height, byte[] kinds)
    {
        int walls = 0;
        if (!IsRiverAt(tileX, tileY - 1, width, height, kinds))
        {
            walls++;
        }

        if (!IsRiverAt(tileX + 1, tileY, width, height, kinds))
        {
            walls++;
        }

        if (!IsRiverAt(tileX, tileY + 1, width, height, kinds))
        {
            walls++;
        }

        if (!IsRiverAt(tileX - 1, tileY, width, height, kinds))
        {
            walls++;
        }

        return walls * 6;
    }

    private static bool IsRiverAt(int x, int y, int width, int height, byte[] kinds) =>
        x >= 0 && y >= 0 && x < width && y < height && kinds[(y * width) + x] == (byte)WaterKind.River;

    private static int ConsumeWall(
        ReadOnlySpan<float> verts, int firstVertex,
        int ax, int ay, int bx, int by, int nx, int ny,
        int width, int height, byte[] kinds, List<WallFace> walls, List<string> inconsistencies)
    {
        if (IsRiverAt(nx, ny, width, height, kinds))
        {
            return 0;
        }

        // Either winding: (top0, bottom0, bottom1, top0, bottom1, top1) or (top0, bottom1, bottom0, top0, top1, bottom1).
        // Per endpoint the highest vertex is the bank edge and the lowest is the bed edge.
        float topA = float.NegativeInfinity;
        float topB = float.NegativeInfinity;
        float bottomA = float.PositiveInfinity;
        float bottomB = float.PositiveInfinity;
        for (int k = 0; k < 6; k++)
        {
            int v = firstVertex + k;
            int vx = (int)MathF.Round(verts[(v * 3) + 0]);
            int vz = (int)MathF.Round(verts[(v * 3) + 2]);
            float vy = verts[(v * 3) + 1];
            if (vx == ax && vz == ay)
            {
                topA = Math.Max(topA, vy);
                bottomA = Math.Min(bottomA, vy);
            }
            else if (vx == bx && vz == by)
            {
                topB = Math.Max(topB, vy);
                bottomB = Math.Min(bottomB, vy);
            }
            else
            {
                inconsistencies.Add($"wall ({ax}, {ay})-({bx}, {by}) emits an off-edge vertex ({vx}, {vz})");
                return 6;
            }
        }

        walls.Add(new WallFace(ax, ay, bx, by, topA, topB, bottomA, bottomB));
        return 6;
    }
}
