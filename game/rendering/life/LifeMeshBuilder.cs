using Godot;

namespace Evenmere.Game.Rendering.Life;

/// <summary>
/// Builds the tiny low-poly meshes for carts and walkers: a cart body, a load block tinted per good, and a walker
/// figure. All geometry is vertex-coloured boxes with flat facet normals (one quad per face), in the placeholder
/// style of the other renderers. The meshes are built once at startup; per-instance variation comes from MultiMesh
/// instance colours, never from more meshes.
/// </summary>
internal static class LifeMeshBuilder
{
    /// <summary>Builds the cart body: a bed with side rails on two wheel boxes, facing -Z.</summary>
    /// <returns>A vertex-coloured mesh about 0.55 units long.</returns>
    public static ArrayMesh BuildCartBody()
    {
        var quads = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Color Tint)>
        {
            // Bed.
            Quad(new Vector3(-0.27f, 0.22f, -0.17f), new Vector3(0.27f, 0.22f, -0.17f), new Vector3(0.27f, 0.32f, -0.17f), new Vector3(-0.27f, 0.32f, -0.17f), Wood()),
            // Side rails.
            Quad(new Vector3(-0.27f, 0.32f, -0.17f), new Vector3(0.27f, 0.32f, -0.17f), new Vector3(0.27f, 0.40f, -0.17f), new Vector3(-0.27f, 0.40f, -0.17f), Rail()),
            // Wheel boxes (one per side).
            Quad(new Vector3(-0.20f, 0.02f, -0.20f), new Vector3(0.20f, 0.02f, -0.20f), new Vector3(0.20f, 0.22f, -0.20f), new Vector3(-0.20f, 0.22f, -0.20f), Wheel()),
            // Draw bar toward the front.
            Quad(new Vector3(-0.03f, 0.24f, -0.17f), new Vector3(0.03f, 0.24f, -0.17f), new Vector3(0.03f, 0.28f, -0.62f), new Vector3(-0.03f, 0.28f, -0.62f), Rail()),
        };
        return ToMesh(quads, extrudeDepth: 0.34f);
    }

    /// <summary>Builds the load block that rides on a carrying cart, in plain white: the instance colour paints the good.</summary>
    /// <returns>A small block sized to sit inside the cart's rails.</returns>
    public static ArrayMesh BuildCartLoad()
    {
        var quads = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Color Tint)>
        {
            Quad(new Vector3(-0.20f, 0.32f, -0.11f), new Vector3(0.20f, 0.32f, -0.11f), new Vector3(0.20f, 0.50f, -0.11f), new Vector3(-0.20f, 0.50f, -0.11f), Color.Color8(255, 255, 255)),
        };
        return ToMesh(quads, extrudeDepth: 0.22f);
    }

    /// <summary>Builds a walker: a body block that takes the instance (tunic) colour, with a head, facing -Z.</summary>
    /// <returns>A figure about 0.45 units tall.</returns>
    public static ArrayMesh BuildWalker()
    {
        var quads = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Color Tint)>
        {
            // Body (white: the per-walker instance colour is the tunic).
            Quad(new Vector3(-0.08f, 0.10f, -0.06f), new Vector3(0.08f, 0.10f, -0.06f), new Vector3(0.08f, 0.34f, -0.06f), new Vector3(-0.08f, 0.34f, -0.06f), Color.Color8(255, 255, 255)),
            // Head (skin, slightly darker so the instance tint still reads as skin).
            Quad(new Vector3(-0.055f, 0.34f, -0.055f), new Vector3(0.055f, 0.34f, -0.055f), new Vector3(0.055f, 0.45f, -0.055f), new Vector3(-0.055f, 0.45f, -0.055f), Color.Color8(232, 190, 150)),
        };
        return ToMesh(quads, extrudeDepth: 0.12f);
    }

    private static (Vector3 A, Vector3 B, Vector3 C, Vector3 D, Color Tint) Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint) => (a, b, c, d, tint);

    private static Color Wood() => Color.Color8(122, 88, 52);

    private static Color Rail() => Color.Color8(96, 68, 40);

    private static Color Wheel() => Color.Color8(58, 42, 26);

    // Turns front-face quads (facing -Z) into closed boxes by extruding them along +Z, with flat facet normals and no
    // shared vertices, so the facets read as low-poly. Winding matches the other builders: clockwise viewed from above.
    private static ArrayMesh ToMesh(List<(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Color Tint)> fronts, float extrudeDepth)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        var indices = new List<int>();

        foreach ((Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint) in fronts)
        {
            Vector3 depth = new(0f, 0f, extrudeDepth);
            Vector3 a2 = a + depth;
            Vector3 b2 = b + depth;
            Vector3 c2 = c + depth;
            Vector3 d2 = d + depth;

            // Front (-Z), back (+Z), left, right, top and bottom.
            AddQuad(vertices, normals, colours, indices, a, b, c, d, tint);
            AddQuad(vertices, normals, colours, indices, b2, a2, d2, c2, tint);
            AddQuad(vertices, normals, colours, indices, a2, a, d, d2, tint);
            AddQuad(vertices, normals, colours, indices, b, b2, c2, c, tint);
            AddQuad(vertices, normals, colours, indices, d, c, c2, d2, tint);
            AddQuad(vertices, normals, colours, indices, a2, b2, b, a, tint);
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            Metallic = 0f,
        };
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private static void AddQuad(
        List<Vector3> vertices, List<Vector3> normals, List<Color> colours, List<int> indices,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint)
    {
        int baseIndex = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 normal = edge2.Cross(edge1).Normalized();
        if (normal.Y < 0f)
        {
            normal = -normal;
        }

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        colours.Add(tint);
        colours.Add(tint);
        colours.Add(tint);
        colours.Add(tint);

        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);
    }
}
