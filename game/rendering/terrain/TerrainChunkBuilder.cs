namespace Evenmere.Game.Rendering.Terrain;

/// <summary>
/// Builds one 32 × 32 chunk of the terrain mesh from tile data, with no Godot types so it stays deterministic and
/// unit-testable (M1-28 covers it). The land is a faceted heightfield: each tile corner takes the carved river bed
/// when a sharing tile is a river tile, else the average of the base heights of the sharing tiles, so flat areas
/// stay perfectly flat, slopes become facets, and river tiles are flat with banks sloping down over one tile.
/// Each tile is two triangles with their own face normals and no shared vertices across faces; the diagonal joins
/// the corner pair with the smaller height difference (ties go to a fixed diagonal), which avoids ridge artefacts.
/// Both triangles take the tile's terrain colour; the shader's lighting shades the facets.
/// </summary>
/// <remarks>
/// <para>
/// The caller fills three byte arrays for a 34 × 34 window around the chunk (the chunk's 32 × 32 tiles plus a
/// one-tile halo for the border corners), indexed by <c>wy * stride + wx</c>: elevations (0–15), terrain indices (the
/// content <c>TerrainId.Value</c>, used as the colour index) and <see cref="WaterKind"/> values. Only the inner
/// 32 × 32 tiles are emitted; the halo feeds the border corner heights, so chunk borders never crack.
/// </para>
/// <para>
/// One builder instance is reused for every rebuild: the buffers are sized for the worst case once, and
/// <see cref="Rebuild"/> only rewrites the prefix it uses, so rebuilding never allocates.
/// </para>
/// </remarks>
public sealed class TerrainChunkBuilder
{
    /// <summary>The tiles per chunk side, and the window stride (the chunk plus a one-tile halo).</summary>
    public const int ChunkTiles = 32;

    /// <summary>The window side in tiles: the chunk plus a one-tile halo on every side.</summary>
    public const int WindowTiles = ChunkTiles + 2;

    // Worst case per tile: two triangles with no shared vertices (6 verts, 6 indices).
    private const int MaxVertsPerTile = 6;
    private const int MaxIndicesPerTile = 6;
    private const int MaxTiles = ChunkTiles * ChunkTiles;
    private const int MaxVerts = MaxTiles * MaxVertsPerTile;

    private readonly float[] _vertices = new float[MaxVerts * 3];
    private readonly float[] _normals = new float[MaxVerts * 3];
    private readonly byte[] _colourIndices = new byte[MaxVerts];
    private readonly int[] _indices = new int[MaxTiles * MaxIndicesPerTile];

    /// <summary>Gets how many vertices the last rebuild wrote.</summary>
    public int VertexCount { get; private set; }

    /// <summary>Gets how many indices the last rebuild wrote (a multiple of 3).</summary>
    public int IndexCount { get; private set; }

    /// <summary>Gets the vertex positions written by the last rebuild: X, Y, Z triples, world units.</summary>
    public ReadOnlySpan<float> Vertices => _vertices.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex normals written by the last rebuild: X, Y, Z triples.</summary>
    public ReadOnlySpan<float> Normals => _normals.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex colour (terrain) indices written by the last rebuild.</summary>
    public ReadOnlySpan<byte> ColourIndices => _colourIndices.AsSpan(0, VertexCount);

    /// <summary>Gets the triangle indices written by the last rebuild.</summary>
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>
    /// Rebuilds the buffers for one chunk. Tiles are visited in ascending (y, x) order with a fixed diagonal rule,
    /// so equal inputs give equal outputs on every machine.
    /// </summary>
    /// <param name="elevations">The elevation per window tile (0–15).</param>
    /// <param name="terrainIndex">The colour index per window tile (the content terrain ID value).</param>
    /// <param name="waterKind">The <see cref="WaterKind"/> value per window tile.</param>
    /// <param name="mapWidth">The map width in tiles; emit tiles past it are skipped.</param>
    /// <param name="mapHeight">The map height in tiles; emit tiles past it are skipped.</param>
    /// <param name="originX">The world tile column of the window's top-left tile (one halo tile before the chunk).</param>
    /// <param name="originY">The world tile row of the window's top-left tile.</param>
    public void Rebuild(
        byte[] elevations,
        byte[] terrainIndex,
        byte[] waterKind,
        int mapWidth,
        int mapHeight,
        int originX,
        int originY)
    {
        VertexCount = 0;
        IndexCount = 0;

        for (int wy = 1; wy <= ChunkTiles; wy++)
        {
            for (int wx = 1; wx <= ChunkTiles; wx++)
            {
                int x = originX + wx;
                int y = originY + wy;
                if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight)
                {
                    continue;
                }

                float h00 = CornerAt(elevations, waterKind, mapWidth, mapHeight, originX, originY, wx, wy);
                float h10 = CornerAt(elevations, waterKind, mapWidth, mapHeight, originX, originY, wx + 1, wy);
                float h01 = CornerAt(elevations, waterKind, mapWidth, mapHeight, originX, originY, wx, wy + 1);
                float h11 = CornerAt(elevations, waterKind, mapWidth, mapHeight, originX, originY, wx + 1, wy + 1);
                byte colour = terrainIndex[(wy * WindowTiles) + wx];

                if (Math.Abs(h00 - h11) <= Math.Abs(h10 - h01))
                {
                    // Fixed diagonal on ties: c00–c11.
                    EmitTriangle(x, h00, y, x + 1, h10, y, x + 1, h11, y + 1, colour);
                    EmitTriangle(x, h00, y, x + 1, h11, y + 1, x, h01, y + 1, colour);
                }
                else
                {
                    EmitTriangle(x, h00, y, x + 1, h10, y, x, h01, y + 1, colour);
                    EmitTriangle(x + 1, h10, y, x + 1, h11, y + 1, x, h01, y + 1, colour);
                }
            }
        }
    }

    // One window vertex: resolved through the shared corner rule (river carve or average), so every chunk agrees
    // on border heights. Every vertex the emit range touches lies inside the window (the halo covers the border),
    // so no index is out of range; off-map tiles are skipped by the caller.
    private static float CornerAt(
        byte[] elevations,
        byte[] waterKind,
        int mapWidth,
        int mapHeight,
        int originX,
        int originY,
        int vx,
        int vy)
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
                int worldX = originX + vx + dx;
                int worldY = originY + vy + dy;
                if (worldX >= 0 && worldY >= 0 && worldX < mapWidth && worldY < mapHeight)
                {
                    int i = ((vy + dy) * WindowTiles) + vx + dx;
                    float baseHeight = TerrainSurface.MeshTopHeight(elevations[i], (WaterKind)waterKind[i]);
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

                    if (waterKind[i] == (byte)WaterKind.River)
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

    // One triangle with its own face normal: clockwise viewed from above (Godot front faces), which the normal matches.
    private void EmitTriangle(
        float ax,
        float ay,
        float az,
        float bx,
        float by,
        float bz,
        float cx,
        float cy,
        float cz,
        byte colour)
    {
        TerrainSurface.FaceNormal(ax, ay, az, bx, by, bz, cx, cy, cz, out float nx, out float ny, out float nz);
        AddVertex(ax, ay, az, nx, ny, nz, colour);
        AddVertex(bx, by, bz, nx, ny, nz, colour);
        AddVertex(cx, cy, cz, nx, ny, nz, colour);
    }

    private void AddVertex(float x, float y, float z, float nx, float ny, float nz, byte colour)
    {
        int v = VertexCount++;
        _vertices[(v * 3) + 0] = x;
        _vertices[(v * 3) + 1] = y;
        _vertices[(v * 3) + 2] = z;
        _normals[(v * 3) + 0] = nx;
        _normals[(v * 3) + 1] = ny;
        _normals[(v * 3) + 2] = nz;
        _colourIndices[v] = colour;
        _indices[IndexCount++] = v;
    }
}
