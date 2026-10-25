namespace Evenmere.Game.Rendering.Buildings.Kit;

/// <summary>
/// Builds the stylized Era I kit meshes from per-building specs, with no Godot types so the geometry stays
/// deterministic and unit-testable (the same pattern as <see cref="BuildingMeshBuilder"/>). Every part is a pure
/// mesh routine with clean flat normals for M2-29's outlines and one palette slot per vertex from
/// <see cref="KitPalette"/>, so the finished mesh carries flat bible colours and the shader does the banding.
/// Local space is centred on the footprint with its base at y = 0, in tile units; the renderer only translates,
/// rotates about Y and scales Y for sites.
/// </summary>
/// <remarks>
/// <para>
/// One builder instance is reused for every rebuild: the buffers are sized for the largest kit mesh once, and each
/// part call only appends, so composing a mesh never allocates. Call <see cref="Begin"/> first, then the parts in
/// spec order, then read <see cref="VertexCount"/>, <see cref="Normals"/>, <see cref="Slots"/> and <see cref="Indices"/>.
/// </para>
/// <para>
/// Winding convention (as in <c>BuildingMeshBuilder</c>): triangles (A, B, C) are front faces when viewed from
/// outside in clockwise order, and the normal is (C−A)×(B−A).
/// </para>
/// </remarks>
public sealed class KitBuilder
{
    private const int MaxVerts = 4096;
    private const int MaxIndices = 12288;

    private readonly float[] _vertices = new float[MaxVerts * 3];
    private readonly float[] _normals = new float[MaxVerts * 3];
    private readonly byte[] _slots = new byte[MaxVerts];
    private readonly int[] _indices = new int[MaxIndices];

    /// <summary>Gets how many vertices the composed mesh uses.</summary>
    public int VertexCount { get; private set; }

    /// <summary>Gets how many indices the composed mesh uses (a multiple of 3).</summary>
    public int IndexCount { get; private set; }

    /// <summary>Gets the highest Y written since <see cref="Begin"/>, in tile units, for scaffold sizing.</summary>
    public float MaxY { get; private set; }

    /// <summary>Gets the vertex positions written so far: X, Y, Z triples, in tile units.</summary>
    public ReadOnlySpan<float> Vertices => _vertices.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex normals written so far: X, Y, Z triples.</summary>
    public ReadOnlySpan<float> Normals => _normals.AsSpan(0, VertexCount * 3);

    /// <summary>Gets the per-vertex kit palette slots written so far (indices into <see cref="KitPalette"/>).</summary>
    public ReadOnlySpan<byte> Slots => _slots.AsSpan(0, VertexCount);

    /// <summary>Gets the triangle indices written so far.</summary>
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>Clears the buffers before composing a mesh.</summary>
    public void Begin()
    {
        VertexCount = 0;
        IndexCount = 0;
        MaxY = 0f;
    }

    /// <summary>Adds the dry-stone footing course under a building.</summary>
    public void Footing(float x0, float z0, float x1, float z1, float h, byte slot) =>
        AddBox(x0, 0f, z0, x1, h, z1, slot);

    /// <summary>Adds a wall body with optional corner posts.</summary>
    public void Walls(float x0, float z0, float x1, float z1, float h, byte slot, bool posts, byte postSlot)
    {
        AddBox(x0, 0f, z0, x1, h, z1, slot);
        if (!posts)
        {
            return;
        }

        float w = 0.09f;
        AddBox(x0 - 0.02f, 0f, z0 - 0.02f, x0 + w, h, z0 + w, postSlot);
        AddBox(x1 - w, 0f, z0 - 0.02f, x1 + 0.02f, h, z0 + w, postSlot);
        AddBox(x0 - 0.02f, 0f, z1 - w, x0 + w, h, z1 + 0.02f, postSlot);
        AddBox(x1 - w, 0f, z1 - w, x1 + 0.02f, h, z1 + 0.02f, postSlot);
    }

    /// <summary>Adds a gable roof with the ridge along X: two slopes plus triangular gable ends.</summary>
    public void GableRoof(float x0, float x1, float z0, float z1, float wallTop, float ridgeTop, byte roof, byte gable)
    {
        float zc = (z0 + z1) / 2f;
        AddQuad(x0, ridgeTop, zc, x1, ridgeTop, zc, x1, wallTop, z1, x0, wallTop, z1, roof);
        AddQuad(x1, ridgeTop, zc, x0, ridgeTop, zc, x0, wallTop, z0, x1, wallTop, z0, roof);
        AddTriangle(x1, wallTop, z0, x1, wallTop, z1, x1, ridgeTop, zc, gable);
        AddTriangle(x0, wallTop, z1, x0, wallTop, z0, x0, ridgeTop, zc, gable);
    }

    /// <summary>Adds a hip roof: gable slopes with the ridge pulled in, closed by triangular hips.</summary>
    public void HipRoof(float x0, float x1, float z0, float z1, float wallTop, float ridgeTop, byte roof)
    {
        float zc = (z0 + z1) / 2f;
        float inset = Math.Max(0.2f, Math.Min((x1 - x0) * 0.22f, (z1 - z0) * 0.3f));
        float xr0 = x0 + inset;
        float xr1 = x1 - inset;
        AddQuad(xr0, ridgeTop, zc, xr1, ridgeTop, zc, x1, wallTop, z1, x0, wallTop, z1, roof);
        AddQuad(xr1, ridgeTop, zc, xr0, ridgeTop, zc, x0, wallTop, z0, x1, wallTop, z0, roof);
        AddTriangle(x1, wallTop, z0, x1, wallTop, z1, xr1, ridgeTop, zc, roof);
        AddTriangle(x0, wallTop, z1, x0, wallTop, z0, xr0, ridgeTop, zc, roof);
    }

    /// <summary>Adds a single-slope shed roof, high on the z0 edge and low on the z1 edge, with side cheeks.</summary>
    public void ShedRoof(float x0, float x1, float z0, float z1, float topHigh, float topLow, byte roof, byte cheek)
    {
        AddQuad(x0, topHigh, z0, x1, topHigh, z0, x1, topLow, z1, x0, topLow, z1, roof);
        AddTriangle(x1, topHigh, z0, x1, topLow, z1, x1, topLow, z0, cheek);
        AddTriangle(x0, topHigh, z0, x0, topLow, z0, x0, topLow, z1, cheek);
    }

    /// <summary>Adds a pyramid cap (mill ogee, shrine crown, hayrick cone).</summary>
    public void PyramidCap(float x0, float z0, float x1, float z1, float baseY, float apexY, byte slot)
    {
        float xc = (x0 + x1) / 2f;
        float zc = (z0 + z1) / 2f;
        AddTriangle(x0, baseY, z0, x1, baseY, z0, xc, apexY, zc, slot);
        AddTriangle(x1, baseY, z0, x1, baseY, z1, xc, apexY, zc, slot);
        AddTriangle(x1, baseY, z1, x0, baseY, z1, xc, apexY, zc, slot);
        AddTriangle(x0, baseY, z1, x0, baseY, z0, xc, apexY, zc, slot);
    }

    /// <summary>Adds a chimney stack with a cap slab.</summary>
    public void Chimney(float x, float z, float baseY, float topY, byte slot, byte capSlot)
    {
        float w = 0.075f;
        AddBox(x - w, baseY, z - w, x + w, topY, z + w, slot);
        AddBox(x - w - 0.03f, topY, z - w - 0.03f, x + w + 0.03f, topY + 0.05f, z + w + 0.03f, capSlot);
    }

    /// <summary>Adds a door slab proud of one face with a lintel beam. Face 0 = +Z, 1 = −Z, 2 = +X, 3 = −X.</summary>
    public void Door(float x, float z, float w, float h, byte face, byte slot, byte lintelSlot)
    {
        float proud = 0.05f;
        float half = w / 2f;
        switch (face)
        {
            case 1:
                AddBox(x - half, 0f, z - proud, x + half, h, z + 0.02f, slot);
                AddBox(x - half - 0.04f, h, z - proud, x + half + 0.04f, h + 0.06f, z + 0.02f, lintelSlot);
                break;
            case 2:
                AddBox(x - 0.02f, 0f, z - half, x + proud, h, z + half, slot);
                AddBox(x - 0.02f, h, z - half - 0.04f, x + proud, h + 0.06f, z + half + 0.04f, lintelSlot);
                break;
            case 3:
                AddBox(x - proud, 0f, z - half, x + 0.02f, h, z + half, slot);
                AddBox(x - proud, h, z - half - 0.04f, x + 0.02f, h + 0.06f, z + half + 0.04f, lintelSlot);
                break;
            default:
                AddBox(x - half, 0f, z - 0.02f, x + half, h, z + proud, slot);
                AddBox(x - half - 0.04f, h, z - 0.02f, x + half + 0.04f, h + 0.06f, z + proud, lintelSlot);
                break;
        }
    }

    /// <summary>Adds a window: dark inset, frame, optional shutters and flower box. Face follows <see cref="Door"/>.</summary>
    public void Window(float x, float y, float z, float w, float h, byte face, bool shutters, bool flowers, byte frameSlot, byte shutterSlot)
    {
        float proud = 0.04f;
        float half = w / 2f;
        float halfH = h / 2f;
        KitPalette.TryGetSlot("ink.strong", out byte dark); // The unlit interior.
        switch (face)
        {
            case 1:
                AddQuad(x - half, y - halfH, z - proud, x + half, y - halfH, z - proud, x + half, y + halfH, z - proud, x - half, y + halfH, z - proud, dark);
                AddBox(x - half - 0.03f, y - halfH - 0.03f, z - proud, x + half + 0.03f, y - halfH + 0.02f, z - proud + 0.03f, frameSlot);
                AddBox(x - half - 0.03f, y + halfH - 0.02f, z - proud, x + half + 0.03f, y + halfH + 0.03f, z - proud + 0.03f, frameSlot);
                if (shutters)
                {
                    AddBox(x - half - 0.16f, y - halfH, z - proud, x - half - 0.03f, y + halfH, z - proud + 0.02f, shutterSlot);
                    AddBox(x + half + 0.03f, y - halfH, z - proud, x + half + 0.16f, y + halfH, z - proud + 0.02f, shutterSlot);
                }

                if (flowers)
                {
                    FlowerBox(x, y - halfH - 0.08f, z - proud - 0.03f, w + 0.1f, 1);
                }

                break;
            case 2:
                AddQuad(x + proud, y - halfH, z - half, x + proud, y - halfH, z + half, x + proud, y + halfH, z + half, x + proud, y + halfH, z - half, dark);
                AddBox(x - proud + 0.03f, y - halfH - 0.03f, z - half - 0.03f, x + proud, y - halfH + 0.02f, z + half + 0.03f, frameSlot);
                AddBox(x - proud + 0.03f, y + halfH - 0.02f, z - half - 0.03f, x + proud, y + halfH + 0.03f, z + half + 0.03f, frameSlot);
                if (shutters)
                {
                    AddBox(x - proud, y - halfH, z - half - 0.16f, x + proud - 0.02f, y + halfH, z - half - 0.03f, shutterSlot);
                    AddBox(x - proud, y - halfH, z + half + 0.03f, x + proud - 0.02f, y + halfH, z + half + 0.16f, shutterSlot);
                }

                if (flowers)
                {
                    FlowerBox(x + proud + 0.03f, y - halfH - 0.08f, z, w + 0.1f, 2);
                }

                break;
            case 3:
                AddQuad(x - proud, y - halfH, z + half, x - proud, y - halfH, z - half, x - proud, y + halfH, z - half, x - proud, y + halfH, z + half, dark);
                AddBox(x - proud, y - halfH - 0.03f, z - half - 0.03f, x - proud + 0.03f, y - halfH + 0.02f, z + half + 0.03f, frameSlot);
                AddBox(x - proud, y + halfH - 0.02f, z - half - 0.03f, x - proud + 0.03f, y + halfH + 0.03f, z + half + 0.03f, frameSlot);
                if (shutters)
                {
                    AddBox(x + proud - 0.02f, y - halfH, z - half - 0.16f, x - proud, y + halfH, z - half - 0.03f, shutterSlot);
                    AddBox(x + proud - 0.02f, y - halfH, z + half + 0.03f, x - proud, y + halfH, z + half + 0.16f, shutterSlot);
                }

                if (flowers)
                {
                    FlowerBox(x - proud - 0.03f, y - halfH - 0.08f, z, w + 0.1f, 3);
                }

                break;
            default:
                AddQuad(x + half, y - halfH, z + proud, x - half, y - halfH, z + proud, x - half, y + halfH, z + proud, x + half, y + halfH, z + proud, dark);
                AddBox(x - half - 0.03f, y - halfH - 0.03f, z + proud - 0.03f, x + half + 0.03f, y - halfH + 0.02f, z + proud, frameSlot);
                AddBox(x - half - 0.03f, y + halfH - 0.02f, z + proud - 0.03f, x + half + 0.03f, y + halfH + 0.03f, z + proud, frameSlot);
                if (shutters)
                {
                    AddBox(x - half - 0.16f, y - halfH, z + proud - 0.02f, x - half - 0.03f, y + halfH, z + proud, shutterSlot);
                    AddBox(x + half + 0.03f, y - halfH, z + proud - 0.02f, x + half + 0.16f, y + halfH, z + proud, shutterSlot);
                }

                if (flowers)
                {
                    FlowerBox(x, y - halfH - 0.08f, z + proud + 0.03f, w + 0.1f, 0);
                }

                break;
        }
    }

    /// <summary>Adds four corner posts with top plates: the open shed behind the sawpit and market stalls.</summary>
    public void Posts(float x0, float z0, float x1, float z1, float h, byte slot)
    {
        float w = 0.07f;
        AddBox(x0, 0f, z0, x0 + w, h, z0 + w, slot);
        AddBox(x1 - w, 0f, z0, x1, h, z0 + w, slot);
        AddBox(x0, 0f, z1 - w, x0 + w, h, z1, slot);
        AddBox(x1 - w, 0f, z1 - w, x1, h, z1, slot);
        AddBox(x0, h - w, z0, x1, h, z0 + w, slot);
        AddBox(x0, h - w, z1 - w, x1, h, z1, slot);
        AddBox(x0, h - w, z0, x0 + w, h, z1, slot);
        AddBox(x1 - w, h - w, z0, x1, h, z1, slot);
    }

    /// <summary>Adds a crossed log pile: three logs at the base, two above, one on top, with pale end grain.</summary>
    /// <param name="axis">0 runs the logs along X, 1 along Z.</param>
    public void LogPile(float x, float z, float len, byte axis, byte bark, byte ends)
    {
        float r = 0.09f;
        float half = len / 2f;
        for (int row = 0; row < 3; row++)
        {
            int count = 3 - row;
            float y = (row * r * 1.7f) + r;
            float spread = (2 - count) * r;
            for (int i = 0; i < count; i++)
            {
                float off = (i * r * 2f) - spread;
                if (axis == 1)
                {
                    AddBox(x - r + off, y - r, z - half, x + r + off, y + r, z + half, bark);
                    AddQuad(x + r + off, y - r, z + half + 0.005f, x - r + off, y - r, z + half + 0.005f, x - r + off, y + r, z + half + 0.005f, x + r + off, y + r, z + half + 0.005f, ends);
                }
                else
                {
                    AddBox(x - half, y - r, z - r + off, x + half, y + r, z + r + off, bark);
                    AddQuad(x + half + 0.005f, y - r, z - r + off, x + half + 0.005f, y - r, z + r + off, x + half + 0.005f, y + r, z + r + off, x + half + 0.005f, y + r, z - r + off, ends);
                }
            }
        }
    }

    /// <summary>Adds a leaning plank stack: pale boards against a low rack.</summary>
    public void PlankStack(float x, float z, float w, float h, byte axis, byte slot)
    {
        float lean = 0.12f;
        int boards = 5;
        for (int i = 0; i < boards; i++)
        {
            float t = (i / (float)(boards - 1)) - 0.5f;
            if (axis == 1)
            {
                AddBox((x + (t * w)) - 0.05f, 0f, z - lean, (x + (t * w)) + 0.05f, h, z + lean, slot);
            }
            else
            {
                AddBox(x - lean, 0f, (z + (t * w)) - 0.05f, x + lean, h, (z + (t * w)) + 0.05f, slot);
            }
        }
    }

    /// <summary>Adds one crate cube with a darker lid seam, sitting on <paramref name="y0"/>.</summary>
    public void Crate(float x, float z, float y0, float s, byte slot, byte seamSlot)
    {
        AddBox(x - (s / 2f), y0, z - (s / 2f), x + (s / 2f), y0 + s, z + (s / 2f), slot);
        AddBox(x - (s / 2f), y0 + (s * 0.82f), z - (s / 2f) - 0.005f, x + (s / 2f), y0 + (s * 0.9f), z + (s / 2f) + 0.005f, seamSlot);
    }

    /// <summary>Adds a grain sack: a soft box with a tied neck.</summary>
    public void Sack(float x, float z, float s, byte slot)
    {
        AddBox(x - (s / 2f), 0f, z - (s / 2f), x + (s / 2f), s * 0.8f, z + (s / 2f), slot);
        AddBox(x - (s * 0.18f), s * 0.8f, z - (s * 0.18f), x + (s * 0.18f), s * 1.05f, z + (s * 0.18f), slot);
    }

    /// <summary>Adds a barrel: a stout body with two dark hoop bands.</summary>
    public void Barrel(float x, float z, byte slot, byte hoopSlot)
    {
        float r = 0.11f;
        AddBox(x - r, 0f, z - r, x + r, 0.3f, z + r, slot);
        AddBox(x - r - 0.008f, 0.06f, z - r - 0.008f, x + r + 0.008f, 0.1f, z + r + 0.008f, hoopSlot);
        AddBox(x - r - 0.008f, 0.2f, z - r - 0.008f, x + r + 0.008f, 0.24f, z + r + 0.008f, hoopSlot);
    }

    /// <summary>Adds a hand cart: a bed, two disc wheels and two pull handles. Rot 0 faces +X, 1 faces +Z.</summary>
    public void Cart(float x, float z, byte rot, byte bedSlot, byte wheelSlot)
    {
        if (rot == 1)
        {
            AddBox(x - 0.14f, 0.16f, z - 0.3f, x + 0.14f, 0.26f, z + 0.2f, bedSlot);
            AddBox(x - 0.2f, 0f, z - 0.12f, x - 0.14f, 0.2f, z + 0.08f, wheelSlot);
            AddBox(x + 0.14f, 0f, z - 0.12f, x + 0.2f, 0.2f, z + 0.08f, wheelSlot);
            AddBox(x - 0.05f, 0.16f, z + 0.2f, x - 0.02f, 0.2f, z + 0.48f, bedSlot);
            AddBox(x + 0.02f, 0.16f, z + 0.2f, x + 0.05f, 0.2f, z + 0.48f, bedSlot);
        }
        else
        {
            AddBox(x - 0.3f, 0.16f, z - 0.14f, x + 0.2f, 0.26f, z + 0.14f, bedSlot);
            AddBox(x - 0.12f, 0f, z - 0.2f, x + 0.08f, 0.2f, z - 0.14f, wheelSlot);
            AddBox(x - 0.12f, 0f, z + 0.14f, x + 0.08f, 0.2f, z + 0.2f, wheelSlot);
            AddBox(x + 0.2f, 0.16f, z - 0.05f, x + 0.48f, 0.2f, z - 0.02f, bedSlot);
            AddBox(x + 0.2f, 0.16f, z + 0.02f, x + 0.48f, 0.2f, z + 0.05f, bedSlot);
        }
    }

    /// <summary>Adds a net-drying frame: two poles with a sagging net hung between them, facing +Z.</summary>
    public void NetFrame(float x, float z, float w, float h, byte poleSlot, byte netSlot)
    {
        float half = w / 2f;
        AddBox(x - half, 0f, z - 0.03f, x - half + 0.06f, h, z + 0.03f, poleSlot);
        AddBox(x + half - 0.06f, 0f, z - 0.03f, x + half, h, z + 0.03f, poleSlot);
        float sag = 0.12f;
        AddQuad(x - half, h, z, x, h - sag, z, x, h - sag + 0.28f, z, x - half, h - 0.28f, z, netSlot);
        AddQuad(x, h - sag, z, x + half, h, z, x + half, h - 0.28f, z, x, h - sag + 0.28f, z, netSlot);
    }

    /// <summary>Adds an A-frame drying rack with a row of blanks (clay tiles, pots, parchment).</summary>
    /// <param name="axis">0 runs the rack along X, 1 along Z.</param>
    public void DryingRack(float x, float z, float len, byte axis, byte frameSlot, byte blankSlot)
    {
        float half = len / 2f;
        float h = 0.32f;
        if (axis == 1)
        {
            AddBox(x - 0.05f, 0f, z - half, x + 0.05f, h, z + half, frameSlot);
            for (int i = 0; i < 5; i++)
            {
                float t = z - half + 0.1f + (i * ((len - 0.2f) / 4f));
                AddBox(x - 0.12f, h, t - 0.05f, x + 0.12f, h + 0.09f, t + 0.05f, blankSlot);
            }
        }
        else
        {
            AddBox(x - half, 0f, z - 0.05f, x + half, h, z + 0.05f, frameSlot);
            for (int i = 0; i < 5; i++)
            {
                float t = x - half + 0.1f + (i * ((len - 0.2f) / 4f));
                AddBox(t - 0.05f, h, z - 0.12f, t + 0.05f, h + 0.09f, z + 0.12f, blankSlot);
            }
        }
    }

    /// <summary>Adds a post-and-two-rail fence around a rectangle. Gap 0 = none, 1 = N, 2 = S, 3 = W, 4 = E.</summary>
    public void Fence(float x0, float z0, float x1, float z1, byte gap, byte postSlot, byte railSlot)
    {
        float step = 0.85f;
        float postW = 0.07f;
        float h = 0.28f;
        FenceRun(x0, z0, x1, z0, gap == 1, step, postW, h, postSlot, railSlot);
        FenceRun(x0, z1, x1, z1, gap == 2, step, postW, h, postSlot, railSlot);
        FenceRun(x0, z0, x0, z1, gap == 3, step, postW, h, postSlot, railSlot);
        FenceRun(x1, z0, x1, z1, gap == 4, step, postW, h, postSlot, railSlot);
    }

    /// <summary>Adds crop rows across a field: alternating crop and soil stripes with a ridge down each crop stripe.</summary>
    public void CropRows(float x0, float z0, float x1, float z1, int rows, byte crop, byte soil, byte ridge)
    {
        float depth = (z1 - z0) / rows;
        for (int i = 0; i < rows; i++)
        {
            float za = z0 + (i * depth) + 0.015f;
            float zb = z0 + ((i + 1) * depth) - 0.015f;
            byte slot = (i & 1) == 0 ? crop : soil;
            AddBox(x0, 0f, za, x1, 0.05f, zb, slot);
            if (slot == crop)
            {
                AddBox(x0, 0.05f, (za + zb) / 2f - 0.03f, x1, 0.1f, (za + zb) / 2f + 0.03f, ridge);
            }
        }
    }

    /// <summary>Adds a four-sail windmill cross facing +Z: stocks with lattice frames and canvas on one side each.</summary>
    public void Sails(float x, float y, float z, float radius, byte stockSlot, byte canvasSlot)
    {
        AddBox(x - 0.06f, y - 0.06f, z - 0.06f, x + 0.06f, y + 0.06f, z + 0.06f, stockSlot);
        AddBox(x - 0.035f, y - radius, z - 0.02f, x + 0.035f, y + radius, z + 0.02f, stockSlot);
        AddBox(x - radius, y - 0.035f, z - 0.02f, x + radius, y + 0.035f, z + 0.02f, stockSlot);
        float pw = 0.16f;
        AddBox(x + 0.035f, y, z, x + pw, y + radius * 0.92f, z + 0.015f, canvasSlot);
        AddBox(x - pw, y - radius * 0.92f, z, x - 0.035f, y, z + 0.015f, canvasSlot);
        AddBox(x - radius * 0.92f, y + 0.035f, z, x, y + pw, z + 0.015f, canvasSlot);
        AddBox(x, y - pw, z, x + radius * 0.92f, y - 0.035f, z + 0.015f, canvasSlot);
    }

    /// <summary>Adds a beehive oven dome: stacked shrinking courses with a dark mouth facing +Z.</summary>
    public void Oven(float x, float z, float r, byte slot, byte mouthSlot)
    {
        AddBox(x - r, 0f, z - r, x + r, 0.18f, z + r, slot);
        AddBox(x - r * 0.8f, 0.18f, z - r * 0.8f, x + r * 0.8f, 0.34f, z + r * 0.8f, slot);
        AddBox(x - r * 0.55f, 0.34f, z - r * 0.55f, x + r * 0.55f, 0.46f, z + r * 0.55f, slot);
        AddBox(x - 0.09f, 0.06f, z + r - 0.02f, x + 0.09f, 0.24f, z + r + 0.04f, mouthSlot);
    }

    /// <summary>Adds a bottle-kiln stack: a round-based tapered tower of brick with fire mouths.</summary>
    public void KilnStack(float x, float z, byte slot, byte mouthSlot)
    {
        AddBox(x - 0.22f, 0f, z - 0.22f, x + 0.22f, 0.3f, z + 0.22f, slot);
        AddBox(x - 0.19f, 0.3f, z - 0.19f, x + 0.19f, 0.6f, z + 0.19f, slot);
        AddBox(x - 0.15f, 0.6f, z - 0.15f, x + 0.15f, 0.85f, z + 0.15f, slot);
        AddBox(x - 0.11f, 0.85f, z - 0.11f, x + 0.11f, 1.05f, z + 0.11f, slot);
        AddBox(x - 0.08f, 0.1f, z + 0.2f, x + 0.08f, 0.26f, z + 0.26f, mouthSlot);
    }

    /// <summary>Adds a banner pole with a triangular pennant streaming +X.</summary>
    public void BannerPole(float x, float z, float h, byte poleSlot, byte clothSlot)
    {
        AddBox(x - 0.03f, 0f, z - 0.03f, x + 0.03f, h, z + 0.03f, poleSlot);
        AddTriangle(x, h, z, x, h - 0.22f, z, x + 0.42f, h - 0.11f, z, clothSlot);
    }

    /// <summary>Adds a muster-bell frame: two posts, a beam and a dark bell.</summary>
    public void BellFrame(float x, float z, byte frameSlot, byte bellSlot)
    {
        AddBox(x - 0.16f, 0f, z - 0.04f, x - 0.1f, 0.55f, z + 0.04f, frameSlot);
        AddBox(x + 0.1f, 0f, z - 0.04f, x + 0.16f, 0.55f, z + 0.04f, frameSlot);
        AddBox(x - 0.19f, 0.55f, z - 0.04f, x + 0.19f, 0.61f, z + 0.04f, frameSlot);
        AddBox(x - 0.06f, 0.42f, z - 0.05f, x + 0.06f, 0.55f, z + 0.05f, bellSlot);
    }

    /// <summary>Adds three offering bowls in a row, with flame dots when lit.</summary>
    public void OfferingBowls(float x, float z, bool lit, byte bowlSlot, byte flameSlot)
    {
        for (int i = -1; i <= 1; i++)
        {
            float bx = x + (i * 0.22f);
            AddBox(bx - 0.07f, 0f, z - 0.07f, bx + 0.07f, 0.09f, z + 0.07f, bowlSlot);
            if (lit)
            {
                AddBox(bx - 0.025f, 0.09f, z - 0.025f, bx + 0.025f, 0.16f, z + 0.025f, flameSlot);
            }
        }
    }

    /// <summary>Adds a quarry derrick: a mast, a jib, a hanging rope and a stone block on the hook.</summary>
    public void Derrick(float x, float z, float h, byte mastSlot, byte ropeSlot, byte blockSlot)
    {
        AddBox(x - 0.06f, 0f, z - 0.06f, x + 0.06f, h, z + 0.06f, mastSlot);
        AddBox(x - 0.05f, h - 0.32f, z - 0.05f, x + 0.05f, h - 0.24f, z + 0.62f, mastSlot);
        AddBox(x - 0.015f, h - 0.62f, z + 0.55f, x + 0.015f, h - 0.28f, z + 0.58f, ropeSlot);
        AddBox(x - 0.09f, h - 0.78f, z + 0.47f, x + 0.09f, h - 0.62f, z + 0.65f, blockSlot);
    }

    /// <summary>Adds a row of dressed stone blocks. Axis 0 runs along X, 1 along Z.</summary>
    public void StoneBlocks(float x, float z, int count, byte axis, byte slot)
    {
        for (int i = 0; i < count; i++)
        {
            float t = (i - ((count - 1) / 2f)) * 0.24f;
            float bx = axis == 1 ? x : x + t;
            float bz = axis == 1 ? z + t : z;
            AddBox(bx - 0.1f, 0f, bz - 0.1f, bx + 0.1f, 0.18f, bz + 0.1f, slot);
        }
    }

    /// <summary>Adds a spoil heap: a flattened pyramid of rough stone.</summary>
    public void SpoilHeap(float x, float z, float r, float h, byte slot) =>
        PyramidCap(x - r, z - r, x + r, z + r, 0f, h, slot);

    /// <summary>Adds an orchard tree: a trunk, a faceted canopy and optional apple dots.</summary>
    public void Tree(float x, float z, byte trunkSlot, byte canopySlot, bool fruit, byte fruitSlot)
    {
        AddBox(x - 0.05f, 0f, z - 0.05f, x + 0.05f, 0.35f, z + 0.05f, trunkSlot);
        AddBox(x - 0.22f, 0.3f, z - 0.22f, x + 0.22f, 0.62f, z + 0.22f, canopySlot);
        PyramidCap(x - 0.22f, z - 0.22f, x + 0.22f, z + 0.22f, 0.62f, 0.82f, canopySlot);
        if (!fruit)
        {
            return;
        }

        AddBox(x - 0.24f, 0.42f, z + 0.05f, x - 0.18f, 0.48f, z + 0.11f, fruitSlot);
        AddBox(x + 0.18f, 0.5f, z - 0.1f, x + 0.24f, 0.56f, z - 0.04f, fruitSlot);
        AddBox(x - 0.02f, 0.38f, z - 0.24f, x + 0.04f, 0.44f, z - 0.18f, fruitSlot);
    }

    /// <summary>Adds a sheep: a woolly body with a dark head. Rot 0 faces +X, 1 faces +Z.</summary>
    public void Sheep(float x, float z, byte rot, byte woolSlot)
    {
        KitPalette.TryGetSlot("ink.strong", out byte dark); // The head and legs read.
        if (rot == 1)
        {
            AddBox(x - 0.09f, 0.08f, z - 0.14f, x + 0.09f, 0.26f, z + 0.1f, woolSlot);
            AddBox(x - 0.05f, 0.18f, z + 0.1f, x + 0.05f, 0.3f, z + 0.2f, dark);
        }
        else
        {
            AddBox(x - 0.14f, 0.08f, z - 0.09f, x + 0.1f, 0.26f, z + 0.09f, woolSlot);
            AddBox(x + 0.1f, 0.18f, z - 0.05f, x + 0.2f, 0.3f, z + 0.05f, dark);
        }
    }

    /// <summary>Adds a hayrick: a thatch cone on a low round base.</summary>
    public void Hayrick(float x, float z, float r, byte slot)
    {
        AddBox(x - r, 0f, z - r, x + r, 0.14f, z + r, slot);
        PyramidCap(x - r, z - r, x + r, z + r, 0.14f, 0.14f + (r * 1.4f), slot);
    }

    /// <summary>Adds a harvest stook: a small upright sheaf cone in grain gold.</summary>
    public void Stook(float x, float z, byte slot) =>
        PyramidCap(x - 0.09f, z - 0.09f, x + 0.09f, z + 0.09f, 0f, 0.3f, slot);

    /// <summary>Adds a scarecrow: a post, a crossbar with straw sleeves and a sack head.</summary>
    public void Scarecrow(float x, float z, byte postSlot, byte strawSlot)
    {
        AddBox(x - 0.03f, 0f, z - 0.03f, x + 0.03f, 0.5f, z + 0.03f, postSlot);
        AddBox(x - 0.22f, 0.36f, z - 0.03f, x + 0.22f, 0.42f, z + 0.03f, strawSlot);
        AddBox(x - 0.07f, 0.5f, z - 0.07f, x + 0.07f, 0.62f, z + 0.07f, strawSlot);
    }

    /// <summary>Adds a carved story post with three dark ring bands.</summary>
    public void StoryPost(float x, float z, float h, byte postSlot)
    {
        KitPalette.TryGetSlot("ink.strong", out byte dark); // The carved rings.
        AddBox(x - 0.07f, 0f, z - 0.07f, x + 0.07f, h, z + 0.07f, postSlot);
        AddBox(x - 0.075f, h * 0.35f, z - 0.075f, x + 0.075f, h * 0.4f, z + 0.075f, dark);
        AddBox(x - 0.075f, h * 0.6f, z - 0.075f, x + 0.075f, h * 0.65f, z + 0.075f, dark);
        AddBox(x - 0.09f, h, z - 0.09f, x + 0.09f, h + 0.05f, z + 0.09f, postSlot);
    }

    /// <summary>Adds a log bench. Axis 0 runs along X, 1 along Z.</summary>
    public void BenchLog(float x, float z, float len, byte axis, byte slot)
    {
        float half = len / 2f;
        if (axis == 1)
        {
            AddBox(x - 0.09f, 0.08f, z - half, x + 0.09f, 0.24f, z + half, slot);
        }
        else
        {
            AddBox(x - half, 0.08f, z - 0.09f, x + half, 0.24f, z + 0.09f, slot);
        }
    }

    /// <summary>Adds a fire pit: a stone ring, crossed logs and a flame when lit.</summary>
    public void FirePit(float x, float z, float r, bool lit, byte stoneSlot, byte logSlot, byte flameSlot)
    {
        for (int i = 0; i < 8; i++)
        {
            float angle = (i / 8f) * MathF.PI * 2f;
            float sx = x + (MathF.Cos(angle) * r);
            float sz = z + (MathF.Sin(angle) * r);
            AddBox(sx - 0.06f, 0f, sz - 0.06f, sx + 0.06f, 0.1f, sz + 0.06f, stoneSlot);
        }

        AddBox(x - 0.16f, 0.06f, z - 0.04f, x + 0.16f, 0.12f, z + 0.04f, logSlot);
        AddBox(x - 0.04f, 0.06f, z - 0.16f, x + 0.04f, 0.12f, z + 0.16f, logSlot);
        if (lit)
        {
            PyramidCap(x - 0.07f, z - 0.07f, x + 0.07f, z + 0.07f, 0.1f, 0.3f, flameSlot);
        }
    }

    /// <summary>Adds a harvest maypole: a crowned pole with bunting strands down to four corner posts.</summary>
    public void Maypole(float x, float z, float h, float x0, float z0, float x1, float z1, float postH, byte poleSlot, byte crownSlot, byte buntA, byte buntB)
    {
        AddBox(x - 0.04f, 0f, z - 0.04f, x + 0.04f, h, z + 0.04f, poleSlot);
        AddBox(x - 0.1f, h, z - 0.1f, x + 0.1f, h + 0.12f, z + 0.1f, crownSlot);
        (float cx, float cz)[] corners = [(x0, z0), (x1, z0), (x0, z1), (x1, z1)];
        for (int i = 0; i < corners.Length; i++)
        {
            (float px, float pz) = corners[i];
            AddBox(px - 0.035f, 0f, pz - 0.035f, px + 0.035f, postH, pz + 0.035f, poleSlot);
            byte cloth = (i & 1) == 0 ? buntA : buntB;
            AddTriangle(x, h - 0.05f, z, px, postH, pz, px, postH - 0.14f, pz, cloth);
        }
    }

    /// <summary>Adds a plaza medallion: a brick ring with a sun disc, laid flat on the pad.</summary>
    public void Medallion(float x, float z, float r, byte ringSlot, byte discSlot)
    {
        AddBox(x - r, 0.02f, z - r, x + r, 0.06f, z + r, ringSlot);
        AddBox(x - r * 0.55f, 0.06f, z - r * 0.55f, x + r * 0.55f, 0.1f, z + r * 0.55f, discSlot);
    }

    /// <summary>Adds a stone bench slab on two feet. Axis 0 runs along X, 1 along Z.</summary>
    public void BenchSlab(float x, float z, float len, byte axis, byte slot)
    {
        float half = len / 2f;
        if (axis == 1)
        {
            AddBox(x - 0.12f, 0.1f, z - half, x + 0.12f, 0.18f, z + half, slot);
            AddBox(x - 0.1f, 0f, z - half + 0.05f, x + 0.1f, 0.1f, z - half + 0.25f, slot);
            AddBox(x - 0.1f, 0f, z + half - 0.25f, x + 0.1f, 0.1f, z + half - 0.05f, slot);
        }
        else
        {
            AddBox(x - half, 0.1f, z - 0.12f, x + half, 0.18f, z + 0.12f, slot);
            AddBox(x - half + 0.05f, 0f, z - 0.1f, x - half + 0.25f, 0.1f, z + 0.1f, slot);
            AddBox(x + half - 0.25f, 0f, z - 0.1f, x + half - 0.05f, 0.1f, z + 0.1f, slot);
        }
    }

    /// <summary>Adds a planter: a timber box with leaf fill and flower dots.</summary>
    public void Planter(float x, float z, byte boxSlot, byte leafSlot, byte flowerSlot)
    {
        AddBox(x - 0.16f, 0f, z - 0.16f, x + 0.16f, 0.18f, z + 0.16f, boxSlot);
        AddBox(x - 0.13f, 0.18f, z - 0.13f, x + 0.13f, 0.3f, z + 0.13f, leafSlot);
        AddBox(x - 0.1f, 0.3f, z - 0.02f, x - 0.04f, 0.36f, z + 0.04f, flowerSlot);
        AddBox(x + 0.04f, 0.3f, z - 0.04f, x + 0.1f, 0.36f, z + 0.02f, flowerSlot);
    }

    /// <summary>Adds a canoe on a rack: two trestles with a timber hull above.</summary>
    /// <param name="axis">0 runs the hull along X, 1 along Z.</param>
    public void Canoe(float x, float z, float len, byte axis, byte rackSlot, byte hullSlot)
    {
        float half = len / 2f;
        if (axis == 1)
        {
            AddBox(x - 0.16f, 0f, z - half, x - 0.1f, 0.22f, z - half + 0.06f, rackSlot);
            AddBox(x + 0.1f, 0f, z + half - 0.06f, x + 0.16f, 0.22f, z + half, rackSlot);
            AddBox(x - 0.1f, 0.22f, z - half, x + 0.1f, 0.34f, z + half, hullSlot);
        }
        else
        {
            AddBox(x - half, 0f, z - 0.16f, x - half + 0.06f, 0.22f, z - 0.1f, rackSlot);
            AddBox(x + half - 0.06f, 0f, z + 0.1f, x + half, 0.22f, z + 0.16f, rackSlot);
            AddBox(x - half, 0.22f, z - 0.1f, x + half, 0.34f, z + 0.1f, hullSlot);
        }
    }

    /// <summary>Adds a parchment drying line: two poles, a line and three pale sheets.</summary>
    public void ParchmentLine(float x0, float z0, float x1, float z1, byte poleSlot, byte sheetSlot)
    {
        float h = 0.42f;
        AddBox(x0 - 0.03f, 0f, z0 - 0.03f, x0 + 0.03f, h, z0 + 0.03f, poleSlot);
        AddBox(x1 - 0.03f, 0f, z1 - 0.03f, x1 + 0.03f, h, z1 + 0.03f, poleSlot);
        AddBox(Math.Min(x0, x1), h - 0.02f, Math.Min(z0, z1) - 0.01f, Math.Max(x0, x1), h, Math.Max(z0, z1) + 0.01f, poleSlot);
        for (int i = 1; i <= 3; i++)
        {
            float t = i / 4f;
            float sx = x0 + ((x1 - x0) * t);
            float sz = z0 + ((z1 - z0) * t);
            AddBox(sx - 0.07f, h - 0.24f, sz - 0.01f, sx + 0.07f, h - 0.02f, sz + 0.01f, sheetSlot);
        }
    }

    /// <summary>Adds a jetty finger: a plank deck on pile pairs.</summary>
    public void Jetty(float x0, float z0, float x1, float z1, byte deckSlot, byte pileSlot)
    {
        AddBox(x0, 0.1f, z0, x1, 0.18f, z1, deckSlot);
        float dx = x1 - x0;
        float dz = z1 - z0;
        int steps = Math.Max(2, (int)Math.Round(Math.Max(Math.Abs(dx), Math.Abs(dz)) / 0.5f) + 1);
        for (int i = 0; i < steps; i++)
        {
            float t = steps == 1 ? 0f : i / (float)(steps - 1);
            float px = x0 + (dx * t);
            float pz = z0 + (dz * t);
            AddBox(px - 0.05f, -0.25f, pz - 0.05f, px + 0.05f, 0.12f, pz + 0.05f, pileSlot);
        }
    }

    /// <summary>Adds an overturned coracle: a low timber bowl, keel up.</summary>
    public void Coracle(float x, float z, byte slot)
    {
        AddBox(x - 0.16f, 0f, z - 0.14f, x + 0.16f, 0.1f, z + 0.14f, slot);
        AddBox(x - 0.12f, 0.1f, z - 0.1f, x + 0.12f, 0.2f, z + 0.1f, slot);
    }

    /// <summary>Adds a shallow puddle quad: the clay pit's wet floor.</summary>
    public void Puddle(float x, float z, float r, byte slot) =>
        AddQuad(x - r, 0.03f, z - r, x + r, 0.03f, z - r, x + r, 0.03f, z + r, x - r, 0.03f, z + r, slot);

    /// <summary>Adds a turf mound: a flattened pyramid for the standing stones.</summary>
    public void Mound(float x0, float z0, float x1, float z1, float h, byte slot) =>
        PyramidCap(x0, z0, x1, z1, 0f, h, slot);

    /// <summary>Adds one rough standing stone: a tall upright with a smaller offset cap course.</summary>
    public void StandingStone(float x, float z, float w, float h, float d, byte slot, byte capSlot)
    {
        AddBox(x - (w / 2f), 0f, z - (d / 2f), x + (w / 2f), h, z + (d / 2f), slot);
        AddBox(x - (w / 2f) + 0.02f, h, z - (d / 2f) + 0.02f, x + (w / 2f) - 0.03f, h + 0.08f, z + (d / 2f) - 0.02f, capSlot);
    }

    /// <summary>Adds a trilithon: two uprights carrying a lintel stone.</summary>
    public void Trilithon(float x, float z, byte slot, byte lintelSlot)
    {
        AddBox(x - 0.32f, 0f, z - 0.1f, x - 0.12f, 0.75f, z + 0.1f, slot);
        AddBox(x + 0.12f, 0f, z - 0.1f, x + 0.32f, 0.75f, z + 0.1f, slot);
        AddBox(x - 0.4f, 0.75f, z - 0.12f, x + 0.4f, 0.9f, z + 0.12f, lintelSlot);
    }

    /// <summary>Adds a flat ground quad: worn pasture, fallow discs, sett paving.</summary>
    public void Ground(float x0, float z0, float x1, float z1, float y, byte slot) =>
        AddQuad(x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z1, slot);

    /// <summary>Adds a market stall table with five goods dots in one goods token.</summary>
    public void StallTable(float x, float z, byte rot, byte tableSlot, byte goodsSlot)
    {
        if (rot == 1)
        {
            AddBox(x - 0.2f, 0.14f, z - 0.1f, x + 0.2f, 0.2f, z + 0.1f, tableSlot);
            AddBox(x - 0.18f, 0f, z - 0.08f, x - 0.12f, 0.14f, z + 0.08f, tableSlot);
            AddBox(x + 0.12f, 0f, z - 0.08f, x + 0.18f, 0.14f, z + 0.08f, tableSlot);
            for (int i = 0; i < 5; i++)
            {
                float t = x - 0.14f + (i * 0.07f);
                AddBox(t - 0.025f, 0.2f, z - 0.04f, t + 0.025f, 0.27f, z + 0.04f, goodsSlot);
            }
        }
        else
        {
            AddBox(x - 0.1f, 0.14f, z - 0.2f, x + 0.1f, 0.2f, z + 0.2f, tableSlot);
            AddBox(x - 0.08f, 0f, z - 0.18f, x + 0.08f, 0.14f, z - 0.12f, tableSlot);
            AddBox(x - 0.08f, 0f, z + 0.12f, x + 0.08f, 0.14f, z + 0.18f, tableSlot);
            for (int i = 0; i < 5; i++)
            {
                float t = z - 0.14f + (i * 0.07f);
                AddBox(x - 0.04f, 0.2f, t - 0.025f, x + 0.04f, 0.27f, t + 0.025f, goodsSlot);
            }
        }
    }

    /// <summary>
    /// Builds the shared construction-site scaffold at full height: thin corner posts, two plank levels, rope
    /// lashings, and the standard material stack (timber pile, stone cubes, barrel) with a canvas tarp beside it.
    /// </summary>
    public void Scaffold(float width, float depth, float height, byte postSlot, byte plankSlot, byte ropeSlot, byte timberSlot, byte stoneSlot, byte tarpSlot)
    {
        float x0 = -width / 2f;
        float x1 = width / 2f;
        float z0 = -depth / 2f;
        float z1 = depth / 2f;
        float post = 0.06f;

        AddBox(x0 - post, 0f, z0 - post, x0, height, z0, postSlot);
        AddBox(x1, 0f, z0 - post, x1 + post, height, z0, postSlot);
        AddBox(x0 - post, 0f, z1, x0, height, z1 + post, postSlot);
        AddBox(x1, 0f, z1, x1 + post, height, z1 + post, postSlot);

        AddBox(x0 - post, height - post, z0 - post, x1 + post, height, z0, postSlot);
        AddBox(x0 - post, height - post, z1, x1 + post, height, z1 + post, postSlot);

        float mid = height * 0.5f;
        AddBox(x0 - post, mid, z0 - post, x1 + post, mid + 0.05f, z1 + post, plankSlot);
        AddBox(x0 - post, height - 0.35f, z0 - post, x1 + post, height - 0.3f, z1 + post, plankSlot);

        // Rope lashings where the mid level meets the posts.
        AddBox(x0 - post - 0.01f, mid - 0.03f, z0 - post - 0.01f, x0 + 0.01f, mid + 0.08f, z0 + 0.01f, ropeSlot);
        AddBox(x1 - 0.01f, mid - 0.03f, z0 - post - 0.01f, x1 + post + 0.01f, mid + 0.08f, z0 + 0.01f, ropeSlot);

        // The material stack beside the frame: timber pile, stone cubes and a barrel under a tarp lean-to.
        float sx = x1 + 0.35f;
        LogPile(sx, z1 - 0.3f, 0.5f, 0, timberSlot, timberSlot);
        StoneBlocks(sx, z0 + 0.4f, 3, 0, stoneSlot);
        Barrel(sx + 0.05f, (z0 + z1) / 2f, timberSlot, postSlot);
        AddQuad(sx - 0.3f, 0.55f, z0 - 0.1f, sx + 0.3f, 0.55f, z0 - 0.1f, sx + 0.3f, 0.05f, z0 + 0.45f, sx - 0.3f, 0.05f, z0 + 0.45f, tarpSlot);
    }

    private void FlowerBox(float x, float y, float z, float len, byte axis)
    {
        KitPalette.TryGetSlot("wood.beam.dark", out byte beam);
        KitPalette.TryGetSlot("ui.accent", out byte red); // The flower dots.
        if (axis is 2 or 3)
        {
            AddBox(x - 0.03f, y - 0.05f, z - (len / 2f), x + 0.03f, y + 0.02f, z + (len / 2f), beam);
            AddBox(x - 0.02f, y + 0.02f, z - (len / 4f), x + 0.02f, y + 0.07f, z - (len / 4f) + 0.04f, red);
            AddBox(x - 0.02f, y + 0.02f, z + (len / 4f) - 0.04f, x + 0.02f, y + 0.07f, z + (len / 4f), red);
        }
        else
        {
            AddBox(x - (len / 2f), y - 0.05f, z - 0.03f, x + (len / 2f), y + 0.02f, z + 0.03f, beam);
            AddBox(x - (len / 4f), y + 0.02f, z - 0.02f, x - (len / 4f) + 0.04f, y + 0.07f, z + 0.02f, red);
            AddBox(x + (len / 4f) - 0.04f, y + 0.02f, z - 0.02f, x + (len / 4f), y + 0.07f, z + 0.02f, red);
        }
    }

    private void FenceRun(float ax, float az, float bx, float bz, bool gap, float step, float postW, float h, byte postSlot, byte railSlot)
    {
        float dx = bx - ax;
        float dz = bz - az;
        float len = MathF.Sqrt((dx * dx) + (dz * dz));
        if (len < 0.05f)
        {
            return;
        }

        float half = postW / 2f;
        int posts = Math.Max(2, (int)Math.Round(len / step) + 1);

        bool Rail(float y0, float y1)
        {
            if (gap)
            {
                return false;
            }

            AddBox(Math.Min(ax, bx) - half, y0, Math.Min(az, bz) - half, Math.Max(ax, bx) + half, y1, Math.Max(az, bz) + half, railSlot);
            return true;
        }

        Rail(h * 0.35f, h * 0.5f);
        Rail(h * 0.7f, h * 0.85f);

        for (int i = 0; i < posts; i++)
        {
            float t = posts == 1 ? 0f : i / (float)(posts - 1);
            if (gap && t > 0.25f && t < 0.75f)
            {
                continue;
            }

            float px = ax + (dx * t);
            float pz = az + (dz * t);
            AddBox(px - half, 0f, pz - half, px + half, h, pz + half, postSlot);
        }
    }

    private void AddBox(float x0, float y0, float z0, float x1, float y1, float z1, byte slot)
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
        float nX = (e2y * e1z) - (e2z * e1y);
        float nY = (e2z * e1x) - (e2x * e1z);
        float nZ = (e2x * e1y) - (e2y * e1x);
        float length = MathF.Sqrt((nX * nX) + (nY * nY) + (nZ * nZ));
        if (length > 1e-9f)
        {
            nX /= length;
            nY /= length;
            nZ /= length;
        }
        else
        {
            nX = 0f;
            nY = 1f;
            nZ = 0f;
        }

        AddVertex(ax, ay, az, nX, nY, nZ, slot);
        AddVertex(bx, by, bz, nX, nY, nZ, slot);
        AddVertex(cx, cy, cz, nX, nY, nZ, slot);
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
        if (y > MaxY)
        {
            MaxY = y;
        }
    }
}
