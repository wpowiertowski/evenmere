using Evenmere.Game.Data.Visuals;
using Godot;

namespace Evenmere.Game.Rendering.Buildings;

/// <summary>
/// Turns <see cref="BuildingMeshBuilder"/> geometry into cached <see cref="ArrayMesh"/> instances with palette colours.
/// One mesh is generated per (type, footprint, ruin variant) and reused by every instance, so a thousand houses share one
/// mesh. Real scenes from the mapping are ignored for now: <c>Scene</c> is reserved for M3-13 art replacement through
/// <c>game/data/visuals</c>, never through code changes here.
/// </summary>
public sealed class PlaceholderMeshFactory : IDisposable
{
    private static readonly string _mappingPath = "res://data/visuals/era1-slice.json";

    private static readonly Color _fallbackColour = new(0.54f, 0.54f, 0.54f);
    private static readonly Color _ruinColour = new(0.48f, 0.48f, 0.48f);
    private static readonly Color _scaffoldColour = new(0.6f, 0.48f, 0.32f);

    private readonly BuildingMeshBuilder _builder = new();
    private readonly Dictionary<string, ArrayMesh> _bodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArrayMesh> _ruins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArrayMesh> _scaffolds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (BuildingShape Shape, Color Colour)> _visuals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private readonly StandardMaterial3D _buildingMaterial;
    private readonly StandardMaterial3D _scaffoldMaterial;
    private ArrayMesh? _plinthUnit;

    private BuildingVisualMap _map = new(new Dictionary<string, BuildingVisual>());

    /// <summary>Initializes a new instance of the <see cref="PlaceholderMeshFactory"/> class.</summary>
    public PlaceholderMeshFactory()
    {
        // Matte low-poly look in the terrain's style: palette colours as albedo, no metallic shine.
        _buildingMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            Metallic = 0f,
        };
        _scaffoldMaterial = new StandardMaterial3D
        {
            AlbedoColor = _scaffoldColour.SrgbToLinear(),
            Roughness = 1f,
            Metallic = 0f,
        };
    }

    /// <summary>
    /// Loads the placeholder mapping from <c>game/data/visuals/era1-slice.json</c>. Missing files or bad entries warn
    /// and fall back to the grey box, so rendering never fails on load (headless-safe: plain file IO, no GPU).
    /// </summary>
    public void LoadMapping()
    {
        string json = Godot.FileAccess.GetFileAsString(_mappingPath);
        if (string.IsNullOrEmpty(json))
        {
            GD.PushWarning($"[Evenmere] Buildings: missing mapping '{_mappingPath}'; every building falls back to grey.");
            _map = new BuildingVisualMap(new Dictionary<string, BuildingVisual>());
        }
        else
        {
            _map = BuildingVisualMap.Load(json, static text => GD.PushWarning($"[Evenmere] Buildings: {text}"));
        }
    }

    /// <summary>Gets the resolved look of a building type: its shape and linear base colour, falling back to grey.</summary>
    /// <param name="typeName">The content building ID.</param>
    /// <returns>The shape and linear base colour.</returns>
    public (BuildingShape Shape, Color Colour) VisualFor(string typeName)
    {
        if (_visuals.TryGetValue(typeName, out (BuildingShape Shape, Color Colour) cached))
        {
            return cached;
        }

        (BuildingShape Shape, Color Colour) resolved;
        if (_map.TryGet(typeName, out BuildingVisual? visual))
        {
            resolved = (visual.Shape, Color.FromHtml(visual.Colour).SrgbToLinear());
        }
        else
        {
            if (_warned.Add(typeName))
            {
                GD.PushWarning($"[Evenmere] Buildings: no placeholder mapping for '{typeName}'; using the grey fallback box.");
            }

            resolved = (BuildingShape.Box, _fallbackColour.SrgbToLinear());
        }

        _visuals[typeName] = resolved;
        return resolved;
    }

    /// <summary>Gets the linear colour of a road type from the mapping, falling back to grey.</summary>
    /// <param name="typeName">The content road building ID, such as <c>dirt_road</c>.</param>
    /// <returns>The linear road colour.</returns>
    public Color RoadColourFor(string typeName) => VisualFor(typeName).Colour;

    /// <summary>Gets the cached finished body mesh for a building type and footprint, building it once.</summary>
    /// <param name="typeName">The content building ID.</param>
    /// <param name="width">The unrotated footprint width in tiles.</param>
    /// <param name="depth">The unrotated footprint depth in tiles.</param>
    /// <returns>The shared body mesh.</returns>
    public ArrayMesh BodyFor(string typeName, float width, float depth)
    {
        string key = $"{typeName} {width}x{depth}";
        if (_bodies.TryGetValue(key, out ArrayMesh? mesh))
        {
            return mesh;
        }

        (BuildingShape shape, Color colour) = VisualFor(typeName);
        _builder.RebuildBody(shape, width, depth);
        mesh = ToMesh(_builder, _buildingMaterial, slot => SlotColour(shape, colour, slot));
        _bodies[key] = mesh;
        return mesh;
    }

    /// <summary>Gets the cached ruin mesh for a footprint: the body only, low and grey, with no roof.</summary>
    /// <param name="typeName">The content building ID.</param>
    /// <param name="width">The unrotated footprint width in tiles.</param>
    /// <param name="depth">The unrotated footprint depth in tiles.</param>
    /// <returns>The shared ruin mesh.</returns>
    public ArrayMesh RuinFor(string typeName, float width, float depth)
    {
        string key = $"{typeName} {width}x{depth}";
        if (_ruins.TryGetValue(key, out ArrayMesh? mesh))
        {
            return mesh;
        }

        _builder.RebuildRuin(width, depth);
        mesh = ToMesh(_builder, _buildingMaterial, static slot => Shade(_ruinColour, slot));
        _ruins[key] = mesh;
        return mesh;
    }

    /// <summary>Gets the cached scaffolding mesh for a footprint at full height.</summary>
    /// <param name="typeName">The content building ID.</param>
    /// <param name="width">The unrotated footprint width in tiles.</param>
    /// <param name="depth">The unrotated footprint depth in tiles.</param>
    /// <param name="height">The full building height in tile units.</param>
    /// <returns>The shared scaffold mesh.</returns>
    public ArrayMesh ScaffoldFor(string typeName, float width, float depth, float height)
    {
        string key = $"{typeName} {width}x{depth}";
        if (_scaffolds.TryGetValue(key, out ArrayMesh? mesh))
        {
            return mesh;
        }

        _builder.RebuildScaffold(width, depth, height);
        mesh = ToMesh(_builder, _scaffoldMaterial, static _ => _scaffoldColour.SrgbToLinear());
        _scaffolds[key] = mesh;
        return mesh;
    }

    /// <summary>Gets the shared unit foundation skirt, scaled per building by the renderer.</summary>
    /// <returns>The unit plinth mesh.</returns>
    public ArrayMesh PlinthUnit()
    {
        if (_plinthUnit is not null)
        {
            return _plinthUnit;
        }

        _builder.RebuildPlinthUnit();
        _plinthUnit = ToMesh(_builder, _buildingMaterial, static _ => new Color(0.3f, 0.26f, 0.2f));
        return _plinthUnit;
    }

    /// <summary>Releases the shared materials. Called when the scene exits; meshes are freed with their nodes.</summary>
    public void Dispose()
    {
        _buildingMaterial.Dispose();
        _scaffoldMaterial.Dispose();
    }

    private static Color SlotColour(BuildingShape shape, Color body, MeshSlot slot) => slot switch
    {
        MeshSlot.Body => body,
        MeshSlot.Roof => Shade(body, slot),
        MeshSlot.Trim => new Color(body.R * 0.5f, body.G * 0.5f, body.B * 0.5f),
        MeshSlot.Dark => new Color(body.R * 0.38f, body.G * 0.38f, body.B * 0.38f),
        MeshSlot.FieldA => body,
        MeshSlot.FieldB => new Color(body.R * 0.78f, body.G * 0.78f, body.B * 0.78f),
        MeshSlot.Scaffold => _scaffoldColour.SrgbToLinear(),
        _ => body,
    };

    private static Color Shade(Color body, MeshSlot slot) => slot switch
    {
        MeshSlot.Roof => new Color(body.R * 0.72f, body.G * 0.72f, body.B * 0.72f),
        _ => new Color(body.R * 0.5f, body.G * 0.5f, body.B * 0.5f),
    };

    private static ArrayMesh ToMesh(BuildingMeshBuilder builder, Material material, Func<MeshSlot, Color> colourOf)
    {
        var vertices = new Vector3[builder.VertexCount];
        ReadOnlySpan<float> positions = builder.Vertices;
        for (int i = 0; i < builder.VertexCount; i++)
        {
            vertices[i] = new Vector3(positions[(i * 3) + 0], positions[(i * 3) + 1], positions[(i * 3) + 2]);
        }

        var normals = new Vector3[builder.VertexCount];
        ReadOnlySpan<float> normalData = builder.Normals;
        for (int i = 0; i < builder.VertexCount; i++)
        {
            normals[i] = new Vector3(normalData[(i * 3) + 0], normalData[(i * 3) + 1], normalData[(i * 3) + 2]);
        }

        var colours = new Color[builder.VertexCount];
        ReadOnlySpan<byte> slots = builder.Slots;
        for (int i = 0; i < builder.VertexCount; i++)
        {
            colours[i] = colourOf((MeshSlot)slots[i]);
        }

        int[] indices = new int[builder.IndexCount];
        ReadOnlySpan<int> indexData = builder.Indices;
        for (int i = 0; i < builder.IndexCount; i++)
        {
            indices[i] = indexData[i];
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }
}
