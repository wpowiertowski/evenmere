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

        BuildRiverStrips(riverShader);

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

    // One flat-ish quad per river tile, its vertices riding the carved corners plus the lift: above the flat bed by
    // construction, below the banks, and continuous tile to tile (the shared corners agree, so the water runs
    // unbroken from source to mouth). Rivers never change in M1, so this runs once.
    private void BuildRiverStrips(Shader riverShader)
    {
        if (_host is null)
        {
            return;
        }

        IMapQueries stripMap = _host.Query.Map;
        List<Vector3> vertexList = new();
        List<Vector3> normalList = new();
        List<Vector2> uvList = new();
        List<Color> bankList = new();
        List<int> indexList = new();

        for (int y = 0; y < stripMap.Height; y++)
        {
            for (int x = 0; x < stripMap.Width; x++)
            {
                TileInfo tile = stripMap.GetTile(x, y);
                string name = _host.Content.Terrains.Names[tile.Terrain.Value];
                if (TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0) != WaterKind.River)
                {
                    continue;
                }

                float h00 = StripCornerHeight(stripMap, x, y) + TerrainSurface.RiverStripLift;
                float h10 = StripCornerHeight(stripMap, x + 1, y) + TerrainSurface.RiverStripLift;
                float h01 = StripCornerHeight(stripMap, x, y + 1) + TerrainSurface.RiverStripLift;
                float h11 = StripCornerHeight(stripMap, x + 1, y + 1) + TerrainSurface.RiverStripLift;

                // The bank mask per tile edge follows the land around it: bit 0 north, 1 east, 2 south, 3 west.
                // The shader inks exactly the edges that border land, so the line runs continuously along both
                // banks and never covers the middle, however narrow the river.
                float bankMask = TileBankMask(stripMap, x, y);

                // The same diagonal rule as the terrain tile, so strip and bed never cross.
                if (Math.Abs(h00 - h11) <= Math.Abs(h10 - h01))
                {
                    EmitStripTriangle(vertexList, normalList, uvList, bankList, indexList, x, h00, y, x + 1, h10, y, x + 1, h11, y + 1, bankMask);
                    EmitStripTriangle(vertexList, normalList, uvList, bankList, indexList, x, h00, y, x + 1, h11, y + 1, x, h01, y + 1, bankMask);
                }
                else
                {
                    EmitStripTriangle(vertexList, normalList, uvList, bankList, indexList, x, h00, y, x + 1, h10, y, x, h01, y + 1, bankMask);
                    EmitStripTriangle(vertexList, normalList, uvList, bankList, indexList, x + 1, h10, y, x + 1, h11, y + 1, x, h01, y + 1, bankMask);
                }
            }
        }

        if (vertexList.Count == 0)
        {
            return;
        }

        Vector3[] vertices = vertexList.ToArray();
        Vector3[] stripNormals = normalList.ToArray();

        Vector2[] uvs = uvList.ToArray();
        Color[] banks = bankList.ToArray();
        int[] indices = indexList.ToArray();

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

    // One strip triangle with its own face normal (clockwise viewed from above), matching the terrain's split so
    // the two never cross. UVs ride the world position, so the flow pattern stays continuous across tiles.
    private static void EmitStripTriangle(
        List<Vector3> vertexList,
        List<Vector3> normalList,
        List<Vector2> uvList,
        List<Color> bankList,
        List<int> indexList,
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        float bankMask)
    {
        TerrainSurface.FaceNormal(ax, ay, az, bx, by, bz, cx, cy, cz, out float nx, out float ny, out float nz);
        int stripBase = vertexList.Count;
        vertexList.Add(new Vector3(ax, ay, az));
        vertexList.Add(new Vector3(bx, by, bz));
        vertexList.Add(new Vector3(cx, cy, cz));
        normalList.Add(new Vector3(nx, ny, nz));
        normalList.Add(new Vector3(nx, ny, nz));
        normalList.Add(new Vector3(nx, ny, nz));
        var mask = new Color(bankMask / 15f, bankMask / 15f, bankMask / 15f, 1f);
        bankList.Add(mask);
        bankList.Add(mask);
        bankList.Add(mask);
        for (int k = 0; k < 3; k++)
        {
            Vector3 v = vertexList[stripBase + k];
            uvList.Add(new Vector2(v.X, v.Z));
            indexList.Add(stripBase + k);
        }
    }

    // The bank mask of a river tile: bit 0 north, 1 east, 2 south, 3 west, set when the neighbour across that
    // edge is not a river tile. Off-map counts as bank, so a river meets the map edge (and the sea) with a line.
    private float TileBankMask(IMapQueries map, int x, int y)
    {
        int mask = 0;
        if (!IsRiverTile(map, x, y - 1))
        {
            mask |= 1;
        }

        if (!IsRiverTile(map, x + 1, y))
        {
            mask |= 2;
        }

        if (!IsRiverTile(map, x, y + 1))
        {
            mask |= 4;
        }

        if (!IsRiverTile(map, x - 1, y))
        {
            mask |= 8;
        }

        return mask;
    }

    private bool IsRiverTile(IMapQueries map, int x, int y)
    {
        if (_host is null || x < 0 || y < 0 || x >= map.Width || y >= map.Height)
        {
            return false;
        }

        TileInfo tile = map.GetTile(x, y);
        string name = _host.Content.Terrains.Names[tile.Terrain.Value];
        return TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0) == WaterKind.River;
    }

    // The height of the tile corner shared by the tiles around vertex (vx, vy), through the shared corner rule
    // (river carve or average), so strip vertices agree with the terrain mesh exactly.
    private float StripCornerHeight(IMapQueries map, int vx, int vy)
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
                if (tileX >= 0 && tileY >= 0 && tileX < map.Width && tileY < map.Height)
                {
                    TileInfo tile = map.GetTile(tileX, tileY);
                    string name = _host?.Content.Terrains.Names[tile.Terrain.Value] ?? string.Empty;
                    WaterKind kind = TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0);
                    float baseHeight = TerrainSurface.MeshTopHeight(tile.Elevation, kind);
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

                    if (kind == WaterKind.River)
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
}
