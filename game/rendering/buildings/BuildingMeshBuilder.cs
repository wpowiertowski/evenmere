using Evenmere.Game.Data.Visuals;

namespace Evenmere.Game.Rendering.Buildings;

/// <summary>
/// The colour slots a <see cref="BuildingMeshBuilder"/> assigns per vertex. The factory turns them into linear palette
/// colours from the mapping entry, so this builder stays free of Godot types and allocates nothing after construction.
/// </summary>
public enum MeshSlot : byte
{
    /// <summary>The wall or body colour from the mapping.</summary>
    Body = 0,

    /// <summary>The roof, darkened from the mapping colour.</summary>
    Roof = 1,

    /// <summary>Doors, chimneys and ridges, darker still.</summary>
    Trim = 2,

    /// <summary>The foundation plinth and field row stripes.</summary>
    Dark = 3,

    /// <summary>Farm field strips (the mapping colour).</summary>
    FieldA = 4,

    /// <summary>Farm field strips, alternating darker.</summary>
    FieldB = 5,

    /// <summary>Raw timber scaffolding.</summary>
    Scaffold = 6,
}

/// <summary>
/// Builds the low-poly placeholder meshes from a footprint, with no Godot types so the geometry stays deterministic and
/// unit-testable (M1-28 covers this pattern for terrain). Every face is its own vertices with a face normal computed from
/// the winding, which gives the flat-shaded faceted look of the terrain. Local space is centred on the footprint with the
/// base at y = 0, so the renderer only translates, rotates about Y and scales Y for sites.
/// </summary>
/// <remarks>
/// <para>
/// One builder instance is reused for every rebuild: the buffers are sized for the worst case once, and each
/// <c>Rebuild*</c> call only rewrites the prefix it uses, so rebuilding never allocates.
/// </para>
/// <para>
/// Winding convention (as in <c>TerrainSurface</c>): triangles (A, B, C) are front faces when viewed from outside in
/// clockwise order, and the normal is (C−A)×(B−A).
/// </para>
/// </remarks>
public sealed class BuildingMeshBuilder
{
    private const int MaxVerts = 1024;
    private const int MaxIndices = 2048;

    private readonly float[] _vertices = new float[MaxVerts * 3];
    private readonly float[] _normals = new float[MaxVerts * 3];
    private readonly byte[] _slots = new byte[MaxVerts];
    private readonly int[] _indices = new int[MaxIndices];

    /// <summary>Gets how many vertices the last rebuild wrote.</summary>
    public int VertexCount { get; private set; }

    /// <summary>Gets how many indices the last rebuild wrote (a multiple of 3).</summary>
    public int IndexCount { get; private set; }

    /// <summary>Gets the vertex positions written by the last rebuild: X, Y, Z triples, in tile units.</summary>
    public ReadOnlySpan<float> Vertices => _vertices.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex normals written by the last rebuild: X, Y, Z triples.</summary>
    public ReadOnlySpan<float> Normals => _normals.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex colour slots written by the last rebuild.</summary>
    public ReadOnlySpan<byte> Slots => _slots.AsSpan(0, VertexCount);

    /// <summary>Gets the triangle indices written by the last rebuild.</summary>
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>Builds the finished placeholder body for a shape, centred on the footprint with its base at y = 0.</summary>
    /// <param name="shape">The placeholder shape.</param>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="depth">The footprint depth in tiles.</param>
    public void RebuildBody(BuildingShape shape, float width, float depth)
    {
        Clear();
        switch (shape)
        {
            case BuildingShape.Hut:
                AddHouse(width, depth, 1.0f, 0.6f, false);
                break;
            case BuildingShape.House:
                AddHouse(width, depth, 1.3f, 0.8f, true);
                break;
            case BuildingShape.Hall:
                AddHall(width, depth);
                break;
            case BuildingShape.Farm:
                AddFarm(width, depth);
                break;
            case BuildingShape.Mill:
                AddMill(width, depth);
                break;
            case BuildingShape.Market:
                AddMarket(width, depth);
                break;
            case BuildingShape.Shrine:
                AddHouse(width, depth, 1.7f, 1.2f, false);
                AddDoor(width, 1.0f, 1.1f);
                break;
            case BuildingShape.Storehouse:
                AddHouse(width, depth, 1.5f, 0.9f, false);
                AddDoor(width * 1.4f, 1.0f, 1.1f);
                break;
            case BuildingShape.Road:
            case BuildingShape.Box:
            default:
                AddBox(-width / 2f, 0f, -depth / 2f, width / 2f, 1.2f, depth / 2f, MeshSlot.Body);
                break;
        }
    }

    /// <summary>
    /// Builds the ruin variant: the body only at less than half height, with no roof. The factory colours it grey, so a
    /// missing content type reads as a broken shell (M1-14 turns those into ruins on load).
    /// </summary>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="depth">The footprint depth in tiles.</param>
    public void RebuildRuin(float width, float depth)
    {
        Clear();
        AddBox(-width / 2f, 0f, -depth / 2f, width / 2f, 0.45f, depth / 2f, MeshSlot.Body);
    }

    /// <summary>Builds the site scaffolding: thin corner posts with a top frame, at full building height.</summary>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="depth">The footprint depth in tiles.</param>
    /// <param name="height">The full building height in tile units.</param>
    public void RebuildScaffold(float width, float depth, float height)
    {
        Clear();
        float x0 = -width / 2f;
        float x1 = width / 2f;
        float z0 = -depth / 2f;
        float z1 = depth / 2f;
        float post = 0.09f;

        // Corner posts stand just outside the body so the scaled site body never clips them.
        AddBox(x0 - post, 0f, z0 - post, x0, height, z0, MeshSlot.Scaffold);
        AddBox(x1, 0f, z0 - post, x1 + post, height, z0, MeshSlot.Scaffold);
        AddBox(x0 - post, 0f, z1, x0, height, z1 + post, MeshSlot.Scaffold);
        AddBox(x1, 0f, z1, x1 + post, height, z1 + post, MeshSlot.Scaffold);

        // A top frame ties the posts together.
        AddBox(x0 - post, height - post, z0 - post, x1 + post, height, z0, MeshSlot.Scaffold);
        AddBox(x0 - post, height - post, z1, x1 + post, height, z1 + post, MeshSlot.Scaffold);
        AddBox(x0 - post, height - post, z0, x0, height, z1 + post, MeshSlot.Scaffold);
        AddBox(x1, height - post, z0, x1 + post, height, z1 + post, MeshSlot.Scaffold);
    }

    /// <summary>
    /// Builds the shared foundation skirt: a unit box spanning −0.5 to 0.5 in X and Z with its top at y = 0 reaching
    /// down to y = −1. The renderer instances it per building, scaled to the footprint and the pad-to-lowest drop.
    /// </summary>
    public void RebuildPlinthUnit()
    {
        Clear();
        AddBox(-0.5f, -1f, -0.5f, 0.5f, 0f, 0.5f, MeshSlot.Dark);
    }

    /// <summary>Gets the full building height a shape reaches, for scaffolding, from the footprint.</summary>
    /// <param name="shape">The placeholder shape.</param>
    /// <param name="width">The footprint width in tiles.</param>
    /// <param name="depth">The footprint depth in tiles.</param>
    /// <returns>The height in tile units.</returns>
    public static float HeightOf(BuildingShape shape, float width, float depth)
    {
        float span = Math.Min(width, depth);
        return shape switch
        {
            BuildingShape.Hut => 1.0f + (span * 0.3f),
            BuildingShape.House => 1.3f + (span * 0.4f),
            BuildingShape.Hall => 2.0f + (span * 0.25f),
            BuildingShape.Farm => 0.9f,
            BuildingShape.Mill => 3.3f,
            BuildingShape.Market => 2.2f,
            BuildingShape.Shrine => 1.7f + (span * 0.6f),
            BuildingShape.Storehouse => 1.5f + (span * 0.45f),
            _ => 1.2f,
        };
    }

    private void Clear()
    {
        VertexCount = 0;
        IndexCount = 0;
    }

    // A body with a gable roof along X, an optional chimney and a door on the +Z face.
    private void AddHouse(float width, float depth, float wall, float roof, bool chimney)
    {
        float x0 = -width / 2f;
        float x1 = width / 2f;
        float z0 = -depth / 2f;
        float z1 = depth / 2f;
        AddBox(x0, 0f, z0, x1, wall, z1, MeshSlot.Body);
        float overhang = 0.12f;
        AddGable(x0 - overhang, x1 + overhang, z0 - overhang, z1 + overhang, wall, wall + roof);
        AddDoor(0.5f, 0.8f, 0.9f);
        if (chimney)
        {
            AddBox((width / 4f) - 0.15f, wall + (roof * 0.3f), -0.15f, (width / 4f) + 0.15f, wall + roof + 0.25f, 0.15f, MeshSlot.Trim);
        }
    }

    // A large body with a hip roof and a wide door.
    private void AddHall(float width, float depth)
    {
        float x0 = -width / 2f;
        float x1 = width / 2f;
        float z0 = -depth / 2f;
        float z1 = depth / 2f;
        AddBox(x0, 0f, z0, x1, 2.0f, z1, MeshSlot.Body);
        float overhang = 0.15f;
        AddHip(x0 - overhang, x1 + overhang, z0 - overhang, z1 + overhang, 2.0f, 3.0f);
        AddDoor(0.9f, 1.2f, 1.0f);
    }

    // Low field strips across the footprint with darker row ridges, plus a small shed in one corner.
    private void AddFarm(float width, float depth)
    {
        int strips = Math.Clamp((int)Math.Round(depth), 2, 6);
        float stripDepth = depth / strips;
        for (int i = 0; i < strips; i++)
        {
            float z0 = (-depth / 2f) + (i * stripDepth) + 0.03f;
            float z1 = (-depth / 2f) + ((i + 1) * stripDepth) - 0.03f;
            MeshSlot slot = (i & 1) == 0 ? MeshSlot.FieldA : MeshSlot.FieldB;
            AddBox(-width / 2f, 0f, z0, width / 2f, 0.16f, z1, slot);
            AddBox(-width / 2f, 0.16f, (z0 + z1) / 2f - 0.05f, width / 2f, 0.24f, (z0 + z1) / 2f + 0.05f, MeshSlot.Dark);
        }

        float shed = Math.Min(1.2f, Math.Min(width, depth) * 0.4f);
        float sx = (width / 2f) - shed - 0.1f;
        AddBox(sx, 0f, (-depth / 2f) + 0.1f, sx + shed, 0.8f, (-depth / 2f) + 0.1f + shed, MeshSlot.Body);
        AddGable(sx - 0.08f, sx + shed + 0.08f, (-depth / 2f) + 0.02f, (-depth / 2f) + 0.18f + shed, 0.8f, 1.15f);
    }

    // A tower with a pyramid cap and a four-blade sail cross on the +Z face.
    private void AddMill(float width, float depth)
    {
        float towerW = Math.Max(0.8f, width * 0.45f);
        float towerD = Math.Max(0.8f, depth * 0.45f);
        AddBox(-towerW / 2f, 0f, -towerD / 2f, towerW / 2f, 2.6f, towerD / 2f, MeshSlot.Body);
        AddPyramid(-towerW / 2f - 0.1f, -towerD / 2f - 0.1f, towerW / 2f + 0.1f, towerD / 2f + 0.1f, 2.6f, 3.3f, MeshSlot.Roof);

        float hubY = 2.5f;
        float hubZ = (towerD / 2f) + 0.2f;
        AddBox(-0.12f, hubY - 0.12f, hubZ - 0.1f, 0.12f, hubY + 0.12f, hubZ + 0.1f, MeshSlot.Trim);
        float blade = Math.Min(width, depth) * 0.75f;
        AddBox(-0.14f, hubY - blade, hubZ - 0.03f, 0.14f, hubY + blade, hubZ + 0.03f, MeshSlot.Trim);
        AddBox(-blade, hubY - 0.14f, hubZ - 0.03f, blade, hubY + 0.14f, hubZ + 0.03f, MeshSlot.Trim);
    }

    // A slab with corner posts carrying a hip canopy.
    private void AddMarket(float width, float depth)
    {
        float x0 = -width / 2f;
        float x1 = width / 2f;
        float z0 = -depth / 2f;
        float z1 = depth / 2f;
        AddBox(x0, 0f, z0, x1, 0.2f, z1, MeshSlot.Dark);
        float post = 0.14f;
        AddBox(x0 + 0.1f, 0.2f, z0 + 0.1f, x0 + 0.1f + post, 1.7f, z0 + 0.1f + post, MeshSlot.Trim);
        AddBox(x1 - 0.1f - post, 0.2f, z0 + 0.1f, x1 - 0.1f, 1.7f, z0 + 0.1f + post, MeshSlot.Trim);
        AddBox(x0 + 0.1f, 0.2f, z1 - 0.1f - post, x0 + 0.1f + post, 1.7f, z1 - 0.1f, MeshSlot.Trim);
        AddBox(x1 - 0.1f - post, 0.2f, z1 - 0.1f - post, x1 - 0.1f, 1.7f, z1 - 0.1f, MeshSlot.Trim);
        AddHip(x0 - 0.15f, x1 + 0.15f, z0 - 0.15f, z1 + 0.15f, 1.7f, 2.2f);
    }

    // A door slab proud of the +Z face, centred in X.
    private void AddDoor(float doorWidth, float doorHeight, float wallDepth)
    {
        AddBox(-doorWidth / 2f, 0f, (wallDepth / 2f) - 0.02f, doorWidth / 2f, doorHeight, (wallDepth / 2f) + 0.06f, MeshSlot.Trim);
    }

    // A gable roof along X: two slopes plus triangular ends, with the ridge at zc.
    private void AddGable(float x0, float x1, float z0, float z1, float wallTop, float ridgeTop)
    {
        float zc = (z0 + z1) / 2f;
        AddQuad(x0, ridgeTop, zc, x1, ridgeTop, zc, x1, wallTop, z1, x0, wallTop, z1, MeshSlot.Roof);
        AddQuad(x1, ridgeTop, zc, x0, ridgeTop, zc, x0, wallTop, z0, x1, wallTop, z0, MeshSlot.Roof);
        AddTriangle(x1, wallTop, z0, x1, wallTop, z1, x1, ridgeTop, zc, MeshSlot.Body);
        AddTriangle(x0, wallTop, z1, x0, wallTop, z0, x0, ridgeTop, zc, MeshSlot.Body);
    }

    // A hip roof: gable slopes with the ridge pulled in, closed by triangular hips at both ends.
    private void AddHip(float x0, float x1, float z0, float z1, float wallTop, float ridgeTop)
    {
        float zc = (z0 + z1) / 2f;
        float inset = Math.Max(0.2f, Math.Min((x1 - x0) * 0.22f, (z1 - z0) * 0.3f));
        float xr0 = x0 + inset;
        float xr1 = x1 - inset;
        AddQuad(xr0, ridgeTop, zc, xr1, ridgeTop, zc, x1, wallTop, z1, x0, wallTop, z1, MeshSlot.Roof);
        AddQuad(xr1, ridgeTop, zc, xr0, ridgeTop, zc, x0, wallTop, z0, x1, wallTop, z0, MeshSlot.Roof);
        AddTriangle(x1, wallTop, z0, x1, wallTop, z1, xr1, ridgeTop, zc, MeshSlot.Roof);
        AddTriangle(x0, wallTop, z1, x0, wallTop, z0, xr0, ridgeTop, zc, MeshSlot.Roof);
    }

    // A pyramid cap: one triangle per base edge, edges walked clockwise viewed from above.
    private void AddPyramid(float x0, float z0, float x1, float z1, float baseY, float apexY, MeshSlot slot)
    {
        float xc = (x0 + x1) / 2f;
        float zc = (z0 + z1) / 2f;
        AddTriangle(x0, baseY, z0, x1, baseY, z0, xc, apexY, zc, slot);
        AddTriangle(x1, baseY, z0, x1, baseY, z1, xc, apexY, zc, slot);
        AddTriangle(x1, baseY, z1, x0, baseY, z1, xc, apexY, zc, slot);
        AddTriangle(x0, baseY, z1, x0, baseY, z0, xc, apexY, zc, slot);
    }

    private void AddBox(float x0, float y0, float z0, float x1, float y1, float z1, MeshSlot slot)
    {
        AddQuad(x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1, slot);
        AddQuad(x0, y0, z0, x0, y0, z1, x1, y0, z1, x1, y0, z0, slot);
        AddQuad(x1, y0, z0, x1, y0, z1, x1, y1, z1, x1, y1, z0, slot);
        AddQuad(x0, y0, z1, x0, y0, z0, x0, y1, z0, x0, y1, z1, slot);
        AddQuad(x1, y0, z1, x0, y0, z1, x0, y1, z1, x1, y1, z1, slot);
        AddQuad(x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0, slot);
    }

    private void AddQuad(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        float dx, float dy, float dz,
        MeshSlot slot)
    {
        AddTriangle(ax, ay, az, bx, by, bz, cx, cy, cz, slot);
        AddTriangle(ax, ay, az, cx, cy, cz, dx, dy, dz, slot);
    }

    private void AddTriangle(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        MeshSlot slot)
    {
        if (VertexCount + 3 > MaxVerts || IndexCount + 3 > MaxIndices)
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

    private void AddVertex(float x, float y, float z, float nx, float ny, float nz, MeshSlot slot)
    {
        int v = VertexCount++;
        _vertices[(v * 3) + 0] = x;
        _vertices[(v * 3) + 1] = y;
        _vertices[(v * 3) + 2] = z;
        _normals[(v * 3) + 0] = nx;
        _normals[(v * 3) + 1] = ny;
        _normals[(v * 3) + 2] = nz;
        _slots[v] = (byte)slot;
        _indices[IndexCount++] = v;
    }
}
