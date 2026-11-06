using Evenmere.Content;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Look;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Rendering.Water;

/// <summary>
/// The island's water: one large animated sea plane at <see cref="TerrainSurface.WaterSurfaceY"/> with shoreline foam
/// from a baked distance-to-land texture, plus flat flowing strips over river tiles. The distance field is a CPU
/// breadth-first bake uploaded once as an <c>ImageTexture</c>; reef and deep water are tinted by a mask in the same
/// texture. River strips never change in M1 (no bridges yet), so they are built once at load.
/// </summary>
public sealed partial class WaterRenderer : Node3D
{
    private static readonly string _seaShaderPath = "res://rendering/water/sea.gdshader";
    private static readonly string _seaHandDrawnShaderPath = "res://rendering/water/sea_hd.gdshader";
    private static readonly string _riverShaderPath = "res://rendering/water/river.gdshader";
    private static readonly string _riverHandDrawnShaderPath = "res://rendering/water/river_hd.gdshader";
    private static readonly string _palettePath = "res://data/visuals/palette.tres";
    private static readonly string _ripplePath = "res://assets/textures/water/ripples.png";

    private SimHost? _host;
    private bool _initialised;

    /// <inheritdoc />
    public override void _Ready()
    {
        _host = GetTree().CurrentScene?.GetNodeOrNull<SimHost>("SimHost");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_initialised && _host is { IsRunning: true })
        {
            Initialise();
        }
    }

    private void Initialise()
    {
        if (_host is null)
        {
            return;
        }

        ContentDb content = _host.Content;
        IMapQueries map = _host.Query.Map;
        if (map.Width <= 0 || map.Height <= 0)
        {
            return;
        }

        // Island and river tiles are land for the foam bake; only open water takes a distance.
        bool IsLand(int x, int y)
        {
            TileInfo tile = map.GetTile(x, y);
            string name = content.Terrains.Names[tile.Terrain.Value];
            WaterKind kind = TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
            return kind is not (WaterKind.Sea or WaterKind.Deep or WaterKind.Reef);
        }

        byte[] distance = ShoreDistanceField.Compute(map.Width, map.Height, IsLand);

        // Half-float pixels stay linear on the GPU (an 8-bit texture would be sRGB-decoded on sample and warp the
        // distances), and mipmaps keep the minified island view free of shimmer.
        var image = Image.CreateEmpty(map.Width, map.Height, true, Image.Format.Rgbah);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                TileInfo tile = map.GetTile(x, y);
                string name = _host.Content.Terrains.Names[tile.Terrain.Value];
                WaterKind kind = TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
                float waterType = kind switch
                {
                    WaterKind.Reef => 0.6f,
                    WaterKind.Deep => 1f,
                    WaterKind.Sea => 0.25f,
                    _ => 0f,
                };
                float land = kind is WaterKind.Sea or WaterKind.Deep or WaterKind.Reef ? 0f : 1f;
                float d = kind is WaterKind.Sea or WaterKind.Deep or WaterKind.Reef
                    ? Math.Min((int)distance[(y * map.Width) + x], ShoreDistanceField.MaxDistance) / (float)ShoreDistanceField.MaxDistance
                    : 0f;
                image.SetPixel(x, y, new Color(d, waterType, land, 1f));
            }
        }

        image.GenerateMipmaps();

        // The hand-drawn look renders water opaque (the look post pass would erase transparent water);
        // classic keeps the M1 translucent shaders. The two variants share the same distance-field maths.
        bool handDrawn = LookSettings.ActiveLook == LookMode.HandDrawn;
        Shader? seaShader = GD.Load<Shader>(handDrawn ? _seaHandDrawnShaderPath : _seaShaderPath);
        Shader? riverShader = GD.Load<Shader>(handDrawn ? _riverHandDrawnShaderPath : _riverShaderPath);
        if (seaShader is null || riverShader is null)
        {
            GD.PushError("[Evenmere] Water: missing sea or river shader.");
            return;
        }

        var seaMaterial = new ShaderMaterial { Shader = seaShader };
        seaMaterial.SetShaderParameter("shore_tex", ImageTexture.CreateFromImage(image));
        seaMaterial.SetShaderParameter("map_size", new Vector2(map.Width, map.Height));
        seaMaterial.SetShaderParameter("ambient_color", SceneAmbient());
        seaMaterial.SetShaderParameter("look_amount", LookSettings.ActiveLook == LookMode.HandDrawn ? 1f : 0f);
        if (LookSettings.ActiveLook == LookMode.HandDrawn)
        {
            // The hand-drawn sea is palette-calibrated; classic keeps the M1 colours from the shader defaults.
            // Uniforms take sRGB colours (the engine linearises source_color slots on upload); only vertex
            // buffers take pre-linearised values.
            if (GD.Load<Resource>(_palettePath) is LookPalette palette)
            {
                seaMaterial.SetShaderParameter("shallow_color", palette.SrgbOf("water.shallow"));
                seaMaterial.SetShaderParameter("reef_color", palette.SrgbOf("terrain.reef"));
                seaMaterial.SetShaderParameter("deep_color", palette.SrgbOf("water.deep"));
                seaMaterial.SetShaderParameter("foam_color", palette.SrgbOf("water.foam"));
                seaMaterial.SetShaderParameter("ripple_color", palette.SrgbOf("ink.water"));
            }
            else
            {
                GD.PushWarning("[Evenmere] Water: missing palette; the sea keeps its M1 colours.");
            }
        }

        // ART-04 painted ripples over the procedural strokes; missing files warn once and keep the fallback.
        seaMaterial.SetShaderParameter("has_ripple_tex", BindRipple(seaMaterial));

        // The hand-drawn sea plane runs well past the island (four and a half map sizes across, centred the
        // same way), so the island sits in open sea at overview zoom with the plane edge off-screen. Past the
        // baked distance field the hand-drawn shader holds the deep-water tone, and the background clear colour
        // matches it, so there is no seam. Classic keeps the M1 map-sized plane. Vertex waves stay gentle
        // over the larger quads.
        float seaSpan = handDrawn ? 4.5f : 1.0f;
        var seaMesh = new PlaneMesh
        {
            Size = new Vector2(map.Width * seaSpan, map.Height * seaSpan),
            SubdivideWidth = Math.Min(127, map.Width),
            SubdivideDepth = Math.Min(127, map.Height),
        };
        MeshInstance3D sea = new() { Name = "Sea", Mesh = seaMesh, MaterialOverride = seaMaterial };

        sea.Position = new Vector3(map.Width * 0.5f, TerrainSurface.WaterSurfaceY, map.Height * 0.5f);
        sea.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        AddChild(sea);

        // The smoothed bank-aware beds, baked once from the shared rule: the same surface the terrain mesh
        // draws, so strip and bed never part. Rivers never change in M1 (no bridges), so this runs once.
        float[] beds = new float[map.Width * map.Height];
        TerrainSurface.BakeRiverBeds(new MapBedTiles(map, _host.Content), beds);
        BuildRiverStrips(riverShader, beds);

        _initialised = true;
        GD.Print($"[Evenmere] Water: sea plane and river strips built ({map.Width}x{map.Height} tiles)");
    }

    // Loads the painted ripple mask for a water material, reporting whether it bound. A missing file warns once
    // and leaves the slot on its procedural fallback.
    private static float BindRipple(ShaderMaterial material)
    {
        if (GD.Load<Texture2D>(_ripplePath) is not { } texture)
        {
            GD.PushWarning($"[Evenmere] Water: missing texture '{_ripplePath}'; using procedural strokes.");
            return 0f;
        }

        material.SetShaderParameter("ripple_tex", texture);
        return 1f;
    }

    // The scene ambient that lifts every toon band (Main.tscn WorldEnvironment): colour x energy, falling back
    // to the authored values when the scene has no environment yet.
    private Color SceneAmbient()
    {
        WorldEnvironment? holder = GetTree().CurrentScene?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        Godot.Environment? environment = holder?.Environment;
        if (environment is null)
        {
            return new Color(0.51f, 0.53f, 0.55f);
        }

        return environment.AmbientLightColor * environment.AmbientLightEnergy;
    }

    // One flat-ish quad per river tile, riding the carved corners, clamped below the land mesh heights at the bank
    // edge: above the bed by construction, below the bank tops, and continuous tile to tile (the shared corners
    // agree, so the water runs unbroken from source to mouth). The bank walls in the terrain mesh cover the sides,
    // so the grazing street-zoom view meets a clean waterline with no water-coloured skirt faces to show.
    // The triangulation lives in <see cref="RiverStripBuilder"/> (no Godot types, shared with the geometric tests); this method only fills its
    // inputs and uploads its buffers. Rivers never change in M1 (no bridges), so this runs once.
    private void BuildRiverStrips(Shader riverShader, float[] beds)
    {
        if (_host is null)
        {
            return;
        }

        IMapQueries stripMap = _host.Query.Map;
        byte[] elevations = new byte[stripMap.Width * stripMap.Height];
        byte[] kinds = new byte[stripMap.Width * stripMap.Height];
        for (int y = 0; y < stripMap.Height; y++)
        {
            for (int x = 0; x < stripMap.Width; x++)
            {
                TileInfo tile = stripMap.GetTile(x, y);
                string tileName = _host.Content.Terrains.Names[tile.Terrain.Value];
                elevations[(y * stripMap.Width) + x] = (byte)tile.Elevation;
                kinds[(y * stripMap.Width) + x] = (byte)TerrainSurface.Classify(tileName, (tile.Features & TileFeatures.River) != 0);
            }
        }

        var stripBuilder = new RiverStripBuilder();
        stripBuilder.Rebuild(elevations, kinds, beds, stripMap.Width, stripMap.Height);
        if (stripBuilder.VertexCount == 0)
        {
            return;
        }

        var vertices = new Vector3[stripBuilder.VertexCount];
        var stripNormals = new Vector3[stripBuilder.VertexCount];
        var uvs = new Vector2[stripBuilder.VertexCount];
        var banks = new Color[stripBuilder.VertexCount];
        for (int i = 0; i < stripBuilder.VertexCount; i++)
        {
            vertices[i] = new Vector3(stripBuilder.Vertices[(i * 3) + 0], stripBuilder.Vertices[(i * 3) + 1], stripBuilder.Vertices[(i * 3) + 2]);
            stripNormals[i] = new Vector3(stripBuilder.Normals[(i * 3) + 0], stripBuilder.Normals[(i * 3) + 1], stripBuilder.Normals[(i * 3) + 2]);
            uvs[i] = new Vector2(stripBuilder.Uvs[(i * 2) + 0], stripBuilder.Uvs[(i * 2) + 1]);
            float bank = stripBuilder.Bank[i];
            banks[i] = new Color(bank, bank, bank, 1f);
        }

        int[] indices = new int[stripBuilder.IndexCount];
        Array.Copy(stripBuilder.Indices, indices, stripBuilder.IndexCount);

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = stripNormals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = banks;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var stripMaterial = new ShaderMaterial { Shader = riverShader };
        stripMaterial.SetShaderParameter("ambient_color", SceneAmbient());
        stripMaterial.SetShaderParameter("has_ripple_tex", BindRipple(stripMaterial));
        stripMaterial.SetShaderParameter("look_amount", LookSettings.ActiveLook == LookMode.HandDrawn ? 1f : 0f);
        if (LookSettings.ActiveLook == LookMode.HandDrawn)
        {
            // The hand-drawn river is water-blue; classic keeps the M1 strip colours from the shader defaults.
            // Uniforms take sRGB colours (see the sea material above).
            if (GD.Load<Resource>(_palettePath) is LookPalette stripPalette)
            {
                stripMaterial.SetShaderParameter("river_color", stripPalette.SrgbOf("water.river"));
                stripMaterial.SetShaderParameter("streak_color", stripPalette.SrgbOf("water.foam"));
            }
            else
            {
                GD.PushWarning("[Evenmere] Water: missing palette; the river keeps its M1 colours.");
            }
        }
        MeshInstance3D strips = new()
        {
            Name = "RiverStrips",
            Mesh = mesh,
            MaterialOverride = stripMaterial,
        };
        strips.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(strips);
    }

    // The tile grid as the shared riverbed rule reads it: base heights with land/river buckets, so the bake
    // stays the single source and is never copied here.
    private readonly struct MapBedTiles : TerrainSurface.IRiverBedTiles
    {
        private readonly IMapQueries _map;
        private readonly ContentDb _content;

        public MapBedTiles(IMapQueries map, ContentDb content)
        {
            _map = map;
            _content = content;
        }

        public int Width => _map.Width;

        public int Height => _map.Height;

        public bool IsRiver(int x, int y) => KindOf(x, y) == WaterKind.River;

        public bool IsLand(int x, int y) => KindOf(x, y) == WaterKind.Land;

        public float BaseHeight(int x, int y)
        {
            TileInfo tile = _map.GetTile(x, y);
            return TerrainSurface.MeshTopHeight(tile.Elevation, KindOf(x, y));
        }

        private WaterKind KindOf(int x, int y)
        {
            TileInfo tile = _map.GetTile(x, y);
            string name = _content.Terrains.Names[tile.Terrain.Value];
            return TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
        }
    }
}
