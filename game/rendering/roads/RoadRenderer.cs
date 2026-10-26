using Evenmere.Core.Ids;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Look;
using Evenmere.Sim;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Roads;
using Godot;

namespace Evenmere.Game.Rendering.Roads;

/// <summary>
/// Draws the road layer as one flat-strip mesh per 32 × 32 chunk, in the road type's mapped colour. Strips ride the
/// faceted corner heights plus <see cref="RoadMeshBuilder.Lift"/>, so they hug slopes and never z-fight with the terrain
/// (the small lift wins the depth test at every zoom; the opaque pass keeps the default render order).
/// </summary>
/// <remarks>
/// Updates are event-driven and scoped: a <see cref="RoadsChanged"/> event rebuilds only the road chunks its Min/Max box
/// covers. Terrain never changes in M1, so road meshes never need a rebuild for any other reason.
/// </remarks>
public sealed partial class RoadRenderer : Node3D
{
    private static readonly string[] _mappingFiles =
    [
        "res://data/visuals/era1-slice.json",
        "res://data/visuals/era1-production.json",
        "res://data/visuals/era1-trees.json",
    ];
    private static readonly Color _fallbackRoad = new(0.5f, 0.5f, 0.5f);

    private readonly RoadMeshBuilder _builder = new();
    private readonly Dictionary<ChunkCoord, MeshInstance3D> _chunks = new();
    private readonly Dictionary<string, Color> _roadColours = new(StringComparer.Ordinal);
    private readonly StandardMaterial3D _roadMaterial;
    private readonly StandardMaterial3D _roadMaterialHand;

    private SimHost? _host;
    private bool _initialised;

    /// <summary>Initializes a new instance of the <see cref="RoadRenderer"/> class.</summary>
    public RoadRenderer()
    {
        // Classic keeps the M1 smooth shading; the hand-drawn material bands the key light like the terrain pass.
        // Vertex colours stay linear in both, since the builder linearises the palette fills before writing them.
        _roadMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1f,
            Metallic = 0f,
        };
        _roadMaterialHand = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
            Roughness = 1f,
            Metallic = 0f,
        };
    }

    /// <summary>Gets how many road chunks the last <see cref="RoadsChanged"/> event rebuilt, for the debug evidence.</summary>
    public int RebuiltLastBatch { get; private set; }

    /// <summary>Gets how many road chunks have been rebuilt in all, for the debug evidence.</summary>
    public int TotalRebuilt { get; private set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        _host = GetTree().CurrentScene?.GetNodeOrNull<SimHost>("SimHost");
        if (_host is not null)
        {
            _host.EventsDrained += OnEvents;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_host is not null)
        {
            _host.EventsDrained -= OnEvents;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_initialised && _host is { IsRunning: true })
        {
            LoadRoadColours();
            FullBuild();
            _initialised = true;
        }
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        if (!_initialised || _host is null)
        {
            return;
        }

        foreach (SimEvent ev in events)
        {
            if (ev is not RoadsChanged changed)
            {
                continue;
            }

            int minCx = changed.Min.X / ChunkCoord.TilesPerSide;
            int minCy = changed.Min.Y / ChunkCoord.TilesPerSide;
            int maxCx = changed.Max.X / ChunkCoord.TilesPerSide;
            int maxCy = changed.Max.Y / ChunkCoord.TilesPerSide;

            int rebuilt = 0;
            var parts = new List<string>();
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    RebuildChunk(cx, cy);
                    rebuilt++;
                    parts.Add($"Chunk({cx}, {cy})");
                }
            }

            RebuiltLastBatch = rebuilt;
            TotalRebuilt += rebuilt;
            GD.Print($"[Evenmere] Roads: RoadsChanged ({changed.Min})..({changed.Max}) rebuilt {rebuilt} chunk(s) in [{string.Join(", ", parts)}] (total {TotalRebuilt})");
        }
    }

    private void LoadRoadColours()
    {
        if (_host is null)
        {
            return;
        }

        // Roads live across the visuals files (dirt in the slice, cobbled and timber in era1-production),
        // so every mapping is merged the way the research panel merges its glyphs: the first file naming a
        // road wins, and a road named nowhere falls back to grey with one warning.
        var merged = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (string file in _mappingFiles)
        {
            if (!Godot.FileAccess.FileExists(file))
            {
                continue;
            }

            string json = Godot.FileAccess.GetFileAsString(file);
            if (string.IsNullOrEmpty(json))
            {
                continue;
            }

            var map = BuildingVisualMap.Load(json, static text => GD.PushWarning($"[Evenmere] Roads: {text}"));
            foreach (string name in _host.Content.Buildings.Names)
            {
                if (!merged.ContainsKey(name) && map.TryGet(name, out BuildingVisual? visual))
                {
                    merged[name] = Color.FromHtml(visual.Colour).SrgbToLinear();
                }
            }
        }

        foreach (string name in _host.Content.Buildings.Names)
        {
            BuildingTypeId id = _host.Content.Buildings.IdOf(name);
            if (!_host.Content.Buildings.Get(id).RoadCost.HasValue)
            {
                continue;
            }

            if (merged.TryGetValue(name, out Color colour))
            {
                _roadColours[name] = colour;
            }
            else
            {
                GD.PushWarning($"[Evenmere] Roads: no placeholder mapping for road '{name}'; using grey.");
                _roadColours[name] = _fallbackRoad.SrgbToLinear();
            }
        }
    }

    private void FullBuild()
    {
        if (_host is null)
        {
            return;
        }

        int chunksX = (_host.Query.Map.Width + ChunkCoord.TilesPerSide - 1) / ChunkCoord.TilesPerSide;
        int chunksY = (_host.Query.Map.Height + ChunkCoord.TilesPerSide - 1) / ChunkCoord.TilesPerSide;
        int built = 0;
        for (int cy = 0; cy < chunksY; cy++)
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                if (RebuildChunk(cx, cy))
                {
                    built++;
                }
            }
        }

        GD.Print($"[Evenmere] Roads: built {built} road chunk(s) on load");
    }

    // Rebuilds one road chunk from the road layer and the faceted corner heights. Returns whether it holds any road.
    private bool RebuildChunk(int chunkX, int chunkY)
    {
        if (_host is null)
        {
            return false;
        }

        IMapQueries map = _host.Query.Map;
        int size = ChunkCoord.TilesPerSide;
        int firstX = chunkX * size;
        int firstY = chunkY * size;
        if (firstX >= map.Width || firstY >= map.Height)
        {
            return false;
        }

        int stride = size + 2;
        byte[] road = new byte[stride * stride];
        var palette = new List<Color>();
        var paletteIndex = new Dictionary<string, byte>(StringComparer.Ordinal);

        bool any = false;
        for (int wy = 0; wy < stride; wy++)
        {
            for (int wx = 0; wx < stride; wx++)
            {
                int tileX = firstX + wx - 1;
                int tileY = firstY + wy - 1;
                if (tileX < 0 || tileY < 0 || tileX >= map.Width || tileY >= map.Height)
                {
                    continue;
                }

                BuildingTypeId? roadType = _host.Query.Roads.RoadAt(tileX, tileY);
                if (roadType is not { } type)
                {
                    continue;
                }

                string name = _host.Content.Buildings.NameOf(type);
                if (!paletteIndex.TryGetValue(name, out byte index))
                {
                    palette.Add(_roadColours.TryGetValue(name, out Color colour) ? colour : _fallbackRoad.SrgbToLinear());
                    index = (byte)palette.Count;
                    paletteIndex[name] = index;
                }

                road[(wy * stride) + wx] = index;
                any = true;
            }
        }

        ChunkCoord chunk = new(chunkX, chunkY);
        if (!any)
        {
            if (_chunks.TryGetValue(chunk, out MeshInstance3D? empty))
            {
                empty.QueueFree();
                _chunks.Remove(chunk);
            }

            return false;
        }

        float[] corners = new float[(size + 1) * (size + 1)];
        for (int vy = 0; vy <= size; vy++)
        {
            for (int vx = 0; vx <= size; vx++)
            {
                corners[(vy * (size + 1)) + vx] =
                    GroundSampler.CornerHeight(map, _host.Content, firstX + vx, firstY + vy) + RoadMeshBuilder.Lift;
            }
        }

        _builder.Rebuild(road, corners, size, stride, firstX, firstY);
        if (_builder.VertexCount == 0)
        {
            return false;
        }

        // Past the map edge the window holds no roads, so only the in-map prefix carries strips; the empty tail is skipped.
        ArrayMesh mesh = ToMesh(_builder, palette);
        if (!_chunks.TryGetValue(chunk, out MeshInstance3D? node))
        {
            node = new MeshInstance3D { Name = $"Road_{chunkX}_{chunkY}", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node);
            _chunks[chunk] = node;
        }

        node.Mesh = mesh;
        return true;
    }

    private ArrayMesh ToMesh(RoadMeshBuilder builder, List<Color> palette)
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
            int index = slots[i] - 1;
            colours[i] = index >= 0 && index < palette.Count ? palette[index] : _fallbackRoad;
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
        mesh.SurfaceSetMaterial(0, LookSettings.ActiveLook == LookMode.HandDrawn ? _roadMaterialHand : _roadMaterial);
        return mesh;
    }
}
