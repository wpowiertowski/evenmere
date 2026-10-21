namespace Evenmere.Game.Rendering.Roads;

/// <summary>
/// Builds one chunk of constant-width road strips from the road layer, with no Godot types so the auto-connection stays
/// deterministic and unit-testable. Each tile is the union of a centre square and one arm per connected orthogonal
/// neighbour (end, straight, corner, T and cross fall out of the neighbour mask); every edge is an axis-aligned straight
/// line with no diagonal notches. Strip heights come from bilinear interpolation of the tile's four faceted corner
/// heights, so the road follows slopes without inheriting the terrain's diagonal outline, and neighbouring arms abut
/// exactly at the shared tile edge, with no gaps and no overlap to z-fight.
/// </summary>
/// <remarks>
/// One builder instance is reused for every rebuild: the buffers are sized for the worst case once, and
/// <see cref="Rebuild"/> only rewrites the prefix it uses, so rebuilding never allocates. Winding matches the building
/// and terrain builders: clockwise viewed from above, with the normal as (C−A)×(B−A).
/// </remarks>
public sealed class RoadMeshBuilder
{
    /// <summary>How far above the faceted corner heights road strips ride, in world units.</summary>
    public const float Lift = 0.06f;

    /// <summary>The road width in tiles; strips are centred on the tile with this width.</summary>
    public const float Width = 0.6f;

    private const int MaxVertsPerTile = 30;
    private const int MaxChunkTiles = 32 * 32;
    private const int MaxVerts = MaxChunkTiles * MaxVertsPerTile;

    private readonly float[] _vertices = new float[MaxVerts * 3];
    private readonly float[] _normals = new float[MaxVerts * 3];
    private readonly byte[] _slots = new byte[MaxVerts];
    private readonly int[] _indices = new int[MaxVerts];

    /// <summary>Gets how many vertices the last rebuild wrote.</summary>
    public int VertexCount { get; private set; }

    /// <summary>Gets how many indices the last rebuild wrote (a multiple of 3).</summary>
    public int IndexCount { get; private set; }

    /// <summary>Gets the vertex positions written by the last rebuild: X, Y, Z triples, in world units.</summary>
    public ReadOnlySpan<float> Vertices => _vertices.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex normals written by the last rebuild: X, Y, Z triples.</summary>
    public ReadOnlySpan<float> Normals => _normals.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex palette indices written by the last rebuild (1-based into the caller's palette).</summary>
    public ReadOnlySpan<byte> Slots => _slots.AsSpan(0, VertexCount);

    /// <summary>Gets the triangle indices written by the last rebuild.</summary>
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>
    /// Rebuilds the buffers for the tiles of one chunk. Tiles are visited in ascending (y, x) order with a fixed
    /// neighbour rule, so equal inputs give equal outputs on every machine.
    /// </summary>
    /// <param name="road">Whether each window tile has a road, indexed by <c>wy * stride + wx</c> over the chunk's tiles
    /// plus a one-tile halo; 0 is no road, otherwise the 1-based palette index.</param>
    /// <param name="corners">The corner heights plus <see cref="Lift"/> per chunk vertex, <c>(size + 1)</c> squared.</param>
    /// <param name="size">The chunk side in tiles (32, fewer only past the map edge).</param>
    /// <param name="stride">The window side in tiles (<c>size + 2</c>).</param>
    /// <param name="originX">The world tile column of the chunk's top-left tile.</param>
    /// <param name="originY">The world tile row of the chunk's top-left tile.</param>
    public void Rebuild(byte[] road, float[] corners, int size, int stride, int originX, int originY)
    {
        VertexCount = 0;
        IndexCount = 0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                byte slot = road[((y + 1) * stride) + x + 1];
                if (slot == 0)
                {
                    continue;
                }

                bool north = road[(y * stride) + x + 1] != 0;
                bool south = road[((y + 2) * stride) + x + 1] != 0;
                bool west = road[((y + 1) * stride) + x] != 0;
                bool east = road[((y + 1) * stride) + x + 2] != 0;

                float h = Width / 2f;
                float lo = 0.5f - h;
                float hi = 0.5f + h;

                // The centre square is always drawn, so even an isolated tile reads as a road blob.
                AddRect(originX + x, originY + y, corners, size, x, y, lo, lo, hi, hi, slot);
                if (north)
                {
                    AddRect(originX + x, originY + y, corners, size, x, y, lo, 0f, hi, lo, slot);
                }

                if (south)
                {
                    AddRect(originX + x, originY + y, corners, size, x, y, lo, hi, hi, 1f, slot);
                }

                if (west)
                {
                    AddRect(originX + x, originY + y, corners, size, x, y, 0f, lo, lo, hi, slot);
                }

                if (east)
                {
                    AddRect(originX + x, originY + y, corners, size, x, y, hi, lo, 1f, hi, slot);
                }
            }
        }
    }

    // One axis-aligned rectangle in tile-local (u, v) space, with heights from bilinear interpolation of the tile's
    // four corners. Neighbouring arms sample the same shared corners, so their meeting edge matches exactly.
    private void AddRect(float tileX, float tileY, float[] corners, int size, int x, int y, float u0, float v0, float u1, float v1, byte slot)
    {
        int row = size + 1;
        float c00 = corners[(y * row) + x];
        float c10 = corners[(y * row) + x + 1];
        float c01 = corners[((y + 1) * row) + x];
        float c11 = corners[((y + 1) * row) + x + 1];
        AddQuad(
            tileX + u0, Sample(c00, c10, c01, c11, u0, v0), tileY + v0,
            tileX + u1, Sample(c00, c10, c01, c11, u1, v0), tileY + v0,
            tileX + u1, Sample(c00, c10, c01, c11, u1, v1), tileY + v1,
            tileX + u0, Sample(c00, c10, c01, c11, u0, v1), tileY + v1,
            slot);
    }

    private static float Sample(float c00, float c10, float c01, float c11, float u, float v)
    {
        float north = (c00 * (1f - u)) + (c10 * u);
        float south = (c01 * (1f - u)) + (c11 * u);
        return (north * (1f - v)) + (south * v);
    }

    private void AddQuad(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        float dx, float dy, float dz,
        byte slot)
    {
        AddTriangle(ax, ay, az, bx, by, bz, cx, cy, cz, slot);
        AddTriangle(ax, ay, az, cx, cy, cz, dx, dy, dz, slot);
    }

    private void AddTriangle(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        byte slot)
    {
        if (VertexCount + 3 > MaxVerts || IndexCount + 3 > MaxVerts)
        {
            return;
        }

        float e1x = bx - ax;
        float e1y = by - ay;
        float e1z = bz - az;
        float e2x = cx - ax;
        float e2y = cy - ay;
        float e2z = cz - az;
        float nx = (e2y * e1z) - (e2z * e1y);
        float ny = (e2z * e1x) - (e2x * e1z);
        float nz = (e2x * e1y) - (e2y * e1x);
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (length > 1e-9f)
        {
            nx /= length;
            ny /= length;
            nz /= length;
        }
        else
        {
            nx = 0f;
            ny = 1f;
            nz = 0f;
        }

        AddVertex(ax, ay, az, nx, ny, nz, slot);
        AddVertex(bx, by, bz, nx, ny, nz, slot);
        AddVertex(cx, cy, cz, nx, ny, nz, slot);
    }

    private void AddVertex(float x, float y, float z, float nx, float ny, float nz, byte slot)
    {
        int v = VertexCount++;
        _vertices[(v * 3) + 0] = x;
        _vertices[(v * 3) + 1] = y;
        _vertices[(v * 3) + 2] = z;
        _normals[(v * 3) + 0] = nx;
        _normals[(v * 3) + 1] = ny;
        _normals[(v * 3) + 2] = nz;
        _slots[v] = slot;
        _indices[IndexCount++] = v;
    }
}
