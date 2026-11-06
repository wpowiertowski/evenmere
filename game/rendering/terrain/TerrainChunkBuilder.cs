namespace Evenmere.Game.Rendering.Terrain;

/// <summary>
/// Builds one 32 × 32 chunk of the terrain mesh from tile data, with no Godot types so it stays deterministic and
/// unit-testable (M1-28 covers it). The land is a faceted heightfield: each land corner takes the average of the base
/// heights of the sharing non-river tiles, so flat areas stay perfectly flat, slopes become facets, and banks keep
/// their own height at the river edge. Each river tile is a channel: its corners take the carved river bed (the
/// lowest smoothed effective bed among the sharing river tiles), and a bank wall in the neighbour's land colour
/// spans every river-to-land edge from the bank edge down to the bed, so no crack opens between the two surfaces.
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

    // Worst case per tile: two triangles with no shared vertices (6 verts, 6 indices), plus a bank wall on every
    // edge (4 walls of 2 triangles each: 24 verts, 24 indices). Only river tiles emit walls, but the buffers are
    // sized for the worst case once.
    private const int MaxVertsPerTile = 30;
    private const int MaxIndicesPerTile = 30;
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
    /// <param name="bedEff">The effective riverbed per window tile (<see cref="TerrainSurface.EffectiveRiverBed"/>); read for river tiles, ignored otherwise.</param>
    /// <param name="mapWidth">The map width in tiles; emit tiles past it are skipped.</param>
    /// <param name="mapHeight">The map height in tiles; emit tiles past it are skipped.</param>
    /// <param name="originX">The world tile column of the window's top-left tile (one halo tile before the chunk).</param>
    /// <param name="originY">The world tile row of the window's top-left tile.</param>
    public void Rebuild(
        byte[] elevations,
        byte[] terrainIndex,
        byte[] waterKind,
        float[] bedEff,
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

                float h00;
                float h10;
                float h01;
                float h11;
                bool isRiver = waterKind[(wy * WindowTiles) + wx] == (byte)WaterKind.River;
                if (isRiver)
                {
                    // The channel: carved bed corners, shared with the neighbouring river tiles.
                    h00 = CornerAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx, wy);
                    h10 = CornerAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx + 1, wy);
                    h01 = CornerAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx, wy + 1);
                    h11 = CornerAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx + 1, wy + 1);
                }
                else
                {
                    // The bank: its own height, never pulled down to the bed next door.
                    h00 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx, wy);
                    h10 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx + 1, wy);
                    h01 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx, wy + 1);
                    h11 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx + 1, wy + 1);
                }

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

                if (isRiver)
                {
                    // The bank walls: from the neighbour's bank edge down to the channel bed, in the neighbour's
                    // land colour, so the water reads as lying in a channel with the near bank occluding its edge.
                    // Corners are window-relative: the tile is (wx, wy), and the halo covers the neighbours.
                    float l00 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx, wy);
                    float l10 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx + 1, wy);
                    float l01 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx, wy + 1);
                    float l11 = BankAt(elevations, waterKind, bedEff, mapWidth, mapHeight, originX, originY, wx + 1, wy + 1);
                    EmitWalls(wx, wy, h00, h10, h01, h11, l00, l10, l01, l11, colour, waterKind, terrainIndex, mapWidth, mapHeight, originX, originY);
                }
            }
        }
    }

    // One window vertex resolved through the carved-bed rule (the channel surface), so every chunk agrees
    // on border heights. Every vertex the emit range touches lies inside the window (the halo covers the border),
    // so no index is out of range; off-map tiles are skipped by the caller.
    private static float CornerAt(
        byte[] elevations,
        byte[] waterKind,
        float[] bedEff,
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
                    // A river tile contributes its bank-aware effective bed (carved below nearby land), so
                    // perched channels drop into their valleys; every other tile contributes its base height.
                    float baseHeight = waterKind[i] == (byte)WaterKind.River
                        ? bedEff[i]
                        : TerrainSurface.MeshTopHeight(elevations[i], (WaterKind)waterKind[i]);
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

    // One window vertex resolved through the bank rule (the average of the sharing non-river base heights), so the
    // banks keep their own height at the river edge and every chunk agrees on border heights. River tiles are
    // skipped by the mask, so the bed next door never pulls the bank down.
    private static float BankAt(
        byte[] elevations,
        byte[] waterKind,
        float[] bedEff,
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
        int existMask = 0;
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
                    // A river tile contributes its bank-aware effective bed (only read when no bank shares the
                    // corner, as the fallback); every other tile contributes its base height.
                    float baseHeight = waterKind[i] == (byte)WaterKind.River
                        ? bedEff[i]
                        : TerrainSurface.MeshTopHeight(elevations[i], (WaterKind)waterKind[i]);
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
                    if (waterKind[i] == (byte)WaterKind.River)
                    {
                        riverMask |= bit;
                    }
                }

                bit <<= 1;
            }
        }

        return TerrainSurface.LandCornerHeight(h0, h1, h2, h3, existMask, riverMask);
    }

    // The bank walls of one river tile: on every edge that borders a non-river tile (or the map edge), a quad
    // from the bank edge (the neighbour's heights) down to the channel bed, in the neighbour's colour, facing
    // outward. Interior edges (river on both sides) are continuous channel and need none.
    private void EmitWalls(
        int wx, int wy,
        float b00, float b10, float b01, float b11,
        float l00, float l10, float l01, float l11,
        byte ownColour,
        byte[] waterKind,
        byte[] terrainIndex,
        int mapWidth,
        int mapHeight,
        int originX,
        int originY)
    {
        int x = originX + wx;
        int y = originY + wy;

        // North edge (z = y): the neighbour is (x, y - 1). Outward is (0, 0, -1).
        if (!IsRiverAt(wx, wy - 1, waterKind, mapWidth, mapHeight, originX, originY))
        {
            EmitWall(x, l00, y, x + 1, l10, y, b00, b10, 0f, 0f, -1f, WallColour(wx, wy - 1, ownColour, waterKind, terrainIndex, mapWidth, mapHeight, originX, originY));
        }

        // East edge (x + 1): the neighbour is (x + 1, y). Outward is (1, 0, 0).
        if (!IsRiverAt(wx + 1, wy, waterKind, mapWidth, mapHeight, originX, originY))
        {
            EmitWall(x + 1, l10, y, x + 1, l11, y + 1, b10, b11, 1f, 0f, 0f, WallColour(wx + 1, wy, ownColour, waterKind, terrainIndex, mapWidth, mapHeight, originX, originY));
        }

        // South edge (z = y + 1): the neighbour is (x, y + 1). Outward is (0, 0, 1).
        if (!IsRiverAt(wx, wy + 1, waterKind, mapWidth, mapHeight, originX, originY))
        {
            EmitWall(x, l01, y + 1, x + 1, l11, y + 1, b01, b11, 0f, 0f, 1f, WallColour(wx, wy + 1, ownColour, waterKind, terrainIndex, mapWidth, mapHeight, originX, originY));
        }

        // West edge (x): the neighbour is (x - 1, y). Outward is (-1, 0, 0).
        if (!IsRiverAt(wx - 1, wy, waterKind, mapWidth, mapHeight, originX, originY))
        {
            EmitWall(x, l00, y, x, l01, y + 1, b00, b01, -1f, 0f, 0f, WallColour(wx - 1, wy, ownColour, waterKind, terrainIndex, mapWidth, mapHeight, originX, originY));
        }
    }

    // Whether the window tile (wx, wy) is an in-map river tile: interior edges (river on both sides) are
    // continuous channel and take no wall. Off-map counts as non-river, so the map edge gets a wall.
    private static bool IsRiverAt(
        int wx, int wy,
        byte[] waterKind,
        int mapWidth,
        int mapHeight,
        int originX,
        int originY)
    {
        int worldX = originX + wx;
        int worldY = originY + wy;
        return worldX >= 0 && worldY >= 0 && worldX < mapWidth && worldY < mapHeight
            && waterKind[(wy * WindowTiles) + wx] == (byte)WaterKind.River;
    }

    // Which colour a bank wall takes: the neighbour's terrain colour on in-map non-river neighbours, else the
    // river tile's own colour (a river-to-river edge never asks: it has no wall).
    private static byte WallColour(
        int wx, int wy,
        byte ownColour,
        byte[] waterKind,
        byte[] terrainIndex,
        int mapWidth,
        int mapHeight,
        int originX,
        int originY)
    {
        int worldX = originX + wx;
        int worldY = originY + wy;
        if (worldX < 0 || worldY < 0 || worldX >= mapWidth || worldY >= mapHeight)
        {
            return ownColour;
        }

        int i = (wy * WindowTiles) + wx;
        return waterKind[i] == (byte)WaterKind.River ? ownColour : terrainIndex[i];
    }

    // One wall quad from the bank edge (top0, top1) down to the channel bed (bottoms), facing outward with a flat
    // outward normal. The triangle order is checked against the outward normal at runtime, so the face can never
    // come out culled: axis-aligned walls give an exact zero cross product component, never a wrong flip.
    private void EmitWall(
        float x0, float yt0, float z0,
        float x1, float yt1, float z1,
        float yb0, float yb1,
        float ox, float oy, float oz,
        byte colour)
    {
        TerrainSurface.FaceNormal(x0, yt0, z0, x0, yb0, z0, x1, yb1, z1, out float nx, out float ny, out float nz);
        bool flip = (nx * ox) + (ny * oy) + (nz * oz) < 0f;
        int wallBase = VertexCount;
        if (!flip)
        {
            AddWallVertex(x0, yt0, z0, ox, oy, oz, colour);
            AddWallVertex(x0, yb0, z0, ox, oy, oz, colour);
            AddWallVertex(x1, yb1, z1, ox, oy, oz, colour);
            AddWallVertex(x0, yt0, z0, ox, oy, oz, colour);
            AddWallVertex(x1, yb1, z1, ox, oy, oz, colour);
            AddWallVertex(x1, yt1, z1, ox, oy, oz, colour);
        }
        else
        {
            AddWallVertex(x0, yt0, z0, ox, oy, oz, colour);
            AddWallVertex(x1, yb1, z1, ox, oy, oz, colour);
            AddWallVertex(x0, yb0, z0, ox, oy, oz, colour);
            AddWallVertex(x0, yt0, z0, ox, oy, oz, colour);
            AddWallVertex(x1, yt1, z1, ox, oy, oz, colour);
            AddWallVertex(x1, yb1, z1, ox, oy, oz, colour);
        }

        for (int k = 0; k < 6; k++)
        {
            _indices[IndexCount++] = wallBase + k;
        }
    }

    private void AddWallVertex(float x, float y, float z, float nx, float ny, float nz, byte colour)
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
