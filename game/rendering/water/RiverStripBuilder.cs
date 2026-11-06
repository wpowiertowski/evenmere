using Evenmere.Game.Rendering.Terrain;

namespace Evenmere.Game.Rendering.Water;

/// <summary>
/// Builds the river strip mesh from tile data, with no Godot types so it stays deterministic and unit-testable (the
/// <see cref="TerrainChunkBuilder"/> precedent). One flat-ish quad per river tile rides the carved corners, clamped
/// below the land mesh heights at the bank edge, so the water sits in its channel with the near bank occluding its
/// edge. The caller owns the layout; this class owns the triangulation, so the view and the geometric tests read
/// the same surface.
/// </summary>
/// <remarks>
/// <para>
/// One builder instance is reused per build: <see cref="Rebuild"/> counts the river tiles and bank edges first and
/// sizes the buffers exactly, then writes each tile in ascending (y, x) order with a fixed diagonal rule, so equal
/// inputs give equal outputs on every machine.
/// </para>
/// </remarks>
public sealed class RiverStripBuilder
{
    /// <summary>Gets how many vertices the last rebuild wrote.</summary>
    public int VertexCount { get; private set; }

    /// <summary>Gets how many indices the last rebuild wrote (a multiple of 3).</summary>
    public int IndexCount { get; private set; }

    /// <summary>Gets the vertex positions written by the last rebuild: X, Y, Z triples, world units.</summary>
    public float[] Vertices { get; private set; } = [];

    /// <summary>Gets the per-vertex normals written by the last rebuild: X, Y, Z triples.</summary>
    public float[] Normals { get; private set; } = [];

    /// <summary>Gets the per-vertex UVs written by the last rebuild: world X, Z pairs, so the flow pattern stays continuous across tiles.</summary>
    public float[] Uvs { get; private set; } = [];

    /// <summary>Gets the per-vertex bank mask written by the last rebuild (the tile's 4-bit bank mask over 15).</summary>
    public float[] Bank { get; private set; } = [];

    /// <summary>Gets the triangle indices written by the last rebuild.</summary>
    public int[] Indices { get; private set; } = [];

    /// <summary>
    /// Rebuilds the buffers for one map. Tiles are visited in ascending (y, x) order with a fixed diagonal rule,
    /// so equal inputs give equal outputs on every machine.
    /// </summary>
    /// <param name="elevations">The elevation per tile (0–15).</param>
    /// <param name="kinds">The <see cref="WaterKind"/> value per tile.</param>
    /// <param name="beds">The smoothed effective riverbed per tile (<see cref="TerrainSurface.BakeRiverBeds"/>); read for river tiles, ignored otherwise.</param>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    public void Rebuild(byte[] elevations, byte[] kinds, float[] beds, int width, int height)
    {
        int rivers = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (kinds[(y * width) + x] != (byte)WaterKind.River)
                {
                    continue;
                }

                rivers++;
            }
        }

        int verts = rivers * 6;
        int indices = rivers * 6;
        if (Vertices.Length < verts * 3)
        {
            Vertices = new float[verts * 3];
            Normals = new float[verts * 3];
            Uvs = new float[verts * 2];
            Bank = new float[verts];
            Indices = new int[indices];
        }

        VertexCount = 0;
        IndexCount = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (kinds[(y * width) + x] != (byte)WaterKind.River)
                {
                    continue;
                }

                float h00 = StripCornerHeight(elevations, kinds, beds, width, height, x, y);
                float h10 = StripCornerHeight(elevations, kinds, beds, width, height, x + 1, y);
                float h01 = StripCornerHeight(elevations, kinds, beds, width, height, x, y + 1);
                float h11 = StripCornerHeight(elevations, kinds, beds, width, height, x + 1, y + 1);

                // The bank mask per tile edge follows the land around it: bit 0 north, 1 east, 2 south, 3 west.
                // The shader inks exactly the edges that border land, so the line runs continuously along both
                // banks and never covers the middle, however narrow the river.
                float bankMask = TileBankMask(kinds, width, height, x, y);

                // The same diagonal rule as the terrain tile, so strip and bed never cross.
                if (Math.Abs(h00 - h11) <= Math.Abs(h10 - h01))
                {
                    EmitStripTriangle(x, h00, y, x + 1, h10, y, x + 1, h11, y + 1, bankMask);
                    EmitStripTriangle(x, h00, y, x + 1, h11, y + 1, x, h01, y + 1, bankMask);
                }
                else
                {
                    EmitStripTriangle(x, h00, y, x + 1, h10, y, x, h01, y + 1, bankMask);
                    EmitStripTriangle(x + 1, h10, y, x + 1, h11, y + 1, x, h01, y + 1, bankMask);
                }
            }
        }
    }

    // One strip triangle with its own face normal (clockwise viewed from above), matching the terrain's split so
    // the two never cross. UVs ride the world position, so the flow pattern stays continuous across tiles.
    private void EmitStripTriangle(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        float bankMask)
    {
        TerrainSurface.FaceNormal(ax, ay, az, bx, by, bz, cx, cy, cz, out float nx, out float ny, out float nz);
        int stripBase = VertexCount;
        AddStripVertex(ax, ay, az, nx, ny, nz, bankMask);
        AddStripVertex(bx, by, bz, nx, ny, nz, bankMask);
        AddStripVertex(cx, cy, cz, nx, ny, nz, bankMask);
        for (int k = 0; k < 3; k++)
        {
            Indices[IndexCount++] = stripBase + k;
        }
    }

    private void AddStripVertex(float x, float y, float z, float nx, float ny, float nz, float bankMask)
    {
        int v = VertexCount++;
        Vertices[(v * 3) + 0] = x;
        Vertices[(v * 3) + 1] = y;
        Vertices[(v * 3) + 2] = z;
        Normals[(v * 3) + 0] = nx;
        Normals[(v * 3) + 1] = ny;
        Normals[(v * 3) + 2] = nz;
        Uvs[(v * 2) + 0] = x;
        Uvs[(v * 2) + 1] = z;
        Bank[v] = bankMask / 15f;
    }

    // The bank mask of a river tile: bit 0 north, 1 east, 2 south, 3 west, set when the neighbour across that
    // edge is not a river tile. Off-map counts as bank, so a river meets the map edge (and the sea) with a line.
    private static float TileBankMask(byte[] kinds, int width, int height, int x, int y)
    {
        int mask = 0;
        if (!IsRiverTile(kinds, width, height, x, y - 1))
        {
            mask |= 1;
        }

        if (!IsRiverTile(kinds, width, height, x + 1, y))
        {
            mask |= 2;
        }

        if (!IsRiverTile(kinds, width, height, x, y + 1))
        {
            mask |= 4;
        }

        if (!IsRiverTile(kinds, width, height, x - 1, y))
        {
            mask |= 8;
        }

        return mask;
    }

    private static bool IsRiverTile(byte[] kinds, int width, int height, int x, int y)
    {
        return x >= 0 && y >= 0 && x < width && y < height && kinds[(y * width) + x] == (byte)WaterKind.River;
    }

    // The height of the tile corner shared by the tiles around vertex (vx, vy): the smoothed carved corner
    // (bank-aware, so perched channels drop into their valleys) plus the lift, clamped below the land mesh height
    // at the corner while staying above the bed, so no water edge can rise above the bank it shares the corner
    // with. The corners stay shared, so the water runs unbroken from source to mouth.
    private static float StripCornerHeight(byte[] elevations, byte[] kinds, float[] beds, int width, int height, int vx, int vy) =>
        TerrainSurface.StripCornerHeight(BedCornerHeight(elevations, kinds, beds, width, height, vx, vy), LandCornerHeight(elevations, kinds, beds, width, height, vx, vy));

    // The carved bed at one shared corner: the smoothed bank-aware surface the terrain mesh draws.
    private static float BedCornerHeight(byte[] elevations, byte[] kinds, float[] beds, int width, int height, int vx, int vy)
    {
        float h0 = 0f;
        float h1 = 0f;
        float h2 = 0f;
        float h3 = 0f;
        int riverMask = 0;
        int count = 0;
        int bit = 1;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int tileX = vx + dx;
                int tileY = vy + dy;
                if (tileX >= 0 && tileY >= 0 && tileX < width && tileY < height)
                {
                    int i = (tileY * width) + tileX;
                    float baseHeight = kinds[i] == (byte)WaterKind.River
                        ? beds[i]
                        : TerrainSurface.MeshTopHeight(elevations[i], (WaterKind)kinds[i]);
                    if (bit == 1)
                    {
                        h0 = baseHeight;
                    }
                    else if (bit == 2)
                    {
                        h1 = baseHeight;
                    }
                    else if (bit == 4)
                    {
                        h2 = baseHeight;
                    }
                    else
                    {
                        h3 = baseHeight;
                    }

                    if (kinds[i] == (byte)WaterKind.River)
                    {
                        riverMask |= bit;
                    }

                    count++;
                }

                bit <<= 1;
            }
        }

        return TerrainSurface.ResolveCorner(h0, h1, h2, h3, riverMask, count);
    }

    // The land mesh height at one shared corner: the average of the sharing non-river base heights (the same bank
    // surface the terrain mesh draws), or the carved bed when no bank shares the corner. The strip tucks under it.
    private static float LandCornerHeight(byte[] elevations, byte[] kinds, float[] beds, int width, int height, int vx, int vy)
    {
        float h0 = 0f;
        float h1 = 0f;
        float h2 = 0f;
        float h3 = 0f;
        int riverMask = 0;
        int existMask = 0;
        int bit = 1;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int tileX = vx + dx;
                int tileY = vy + dy;
                if (tileX >= 0 && tileY >= 0 && tileX < width && tileY < height)
                {
                    int i = (tileY * width) + tileX;
                    float baseHeight = kinds[i] == (byte)WaterKind.River
                        ? beds[i]
                        : TerrainSurface.MeshTopHeight(elevations[i], (WaterKind)kinds[i]);
                    if (bit == 1)
                    {
                        h0 = baseHeight;
                    }
                    else if (bit == 2)
                    {
                        h1 = baseHeight;
                    }
                    else if (bit == 4)
                    {
                        h2 = baseHeight;
                    }
                    else
                    {
                        h3 = baseHeight;
                    }

                    existMask |= bit;
                    if (kinds[i] == (byte)WaterKind.River)
                    {
                        riverMask |= bit;
                    }
                }

                bit <<= 1;
            }
        }

        return TerrainSurface.LandCornerHeight(h0, h1, h2, h3, existMask, riverMask);
    }
}
