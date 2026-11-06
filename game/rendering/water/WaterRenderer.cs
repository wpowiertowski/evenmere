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

    // One flat-ish quad per river tile, its vertices riding the carved corners plus the lift, clamped below the
    // banks: above the bed by construction, below the bank tops, and continuous tile to tile (the shared corners
    // agree, so the water runs unbroken from source to mouth). A water-coloured skirt hangs off every bank edge
    // down into the bed, so no see-through crack frames the water at street zoom. Rivers never change in M1
    // (no bridges), so this runs once.
    private void BuildRiverStrips(Shader riverShader, float[] beds)
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

                float h00 = StripCornerHeight(stripMap, x, y, beds);
                float h10 = StripCornerHeight(stripMap, x + 1, y, beds);
                float h01 = StripCornerHeight(stripMap, x, y + 1, beds);
                float h11 = StripCornerHeight(stripMap, x + 1, y + 1, beds);
                float b00 = BedCornerHeight(stripMap, x, y, beds);
                float b10 = BedCornerHeight(stripMap, x + 1, y, beds);
                float b01 = BedCornerHeight(stripMap, x, y + 1, beds);
                float b11 = BedCornerHeight(stripMap, x + 1, y + 1, beds);

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

                // Skirts where the water meets land: a water-coloured face from the strip edge down past the bed,
                // so the grazing street-zoom view meets a clean waterline instead of a see-through crack onto
                // the bed. Interior edges (river on both sides) are continuous surfaces and need none.
                int mask = (int)bankMask;
                if ((mask & 1) != 0)
                {
                    EmitSkirtQuad(vertexList, normalList, uvList, bankList, indexList, x, h00, y, x + 1, h10, y, b00 - SkirtDepth, b10 - SkirtDepth, new Vector3(0f, 0f, -1f), bankMask);
                }

                if ((mask & 2) != 0)
                {
                    EmitSkirtQuad(vertexList, normalList, uvList, bankList, indexList, x + 1, h10, y, x + 1, h11, y + 1, b10 - SkirtDepth, b11 - SkirtDepth, new Vector3(1f, 0f, 0f), bankMask);
                }

                if ((mask & 4) != 0)
                {
                    EmitSkirtQuad(vertexList, normalList, uvList, bankList, indexList, x, h01, y + 1, x + 1, h11, y + 1, b01 - SkirtDepth, b11 - SkirtDepth, new Vector3(0f, 0f, 1f), bankMask);
                }

                if ((mask & 8) != 0)
                {
                    EmitSkirtQuad(vertexList, normalList, uvList, bankList, indexList, x, h00, y, x, h01, y + 1, b00 - SkirtDepth, b01 - SkirtDepth, new Vector3(-1f, 0f, 0f), bankMask);
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

    // How far below the carved bed a skirt reaches: buried in the bed, so no gap can open between the
    // waterline and the bed it sits on.
    private const float SkirtDepth = 0.1f;

    // The height of the tile corner shared by the tiles around vertex (vx, vy): the smoothed carved corner
    // (bank-aware, so perched channels drop into their valleys) plus the lift, clamped below the highest adjacent
    // bank top while staying above the bed — the same surface the terrain mesh draws, so strip and bed never part.
    private float StripCornerHeight(IMapQueries map, int vx, int vy, float[] beds) =>
        TerrainSurface.StripCornerHeight(BedCornerHeight(map, vx, vy, beds), BankMaxHeight(map, vx, vy));

    // The carved bed at one shared corner: the smoothed bank-aware surface the terrain mesh draws.
    private float BedCornerHeight(IMapQueries map, int vx, int vy, float[] beds)
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
                    float baseHeight = kind == WaterKind.River
                        ? beds[(tileY * map.Width) + tileX]
                        : TerrainSurface.MeshTopHeight(tile.Elevation, kind);
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

    // The highest land base height among the tiles sharing a vertex, or positive infinity when no land shares
    // it: the uphill bank the strip tucks under.
    private float BankMaxHeight(IMapQueries map, int vx, int vy)
    {
        float bankMax = float.PositiveInfinity;
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
                    if (TerrainSurface.Classify(name, (tile.Features & TileFeatures.River) != 0) == WaterKind.Land)
                    {
                        float ground = TerrainSurface.MeshTopHeight(tile.Elevation, WaterKind.Land);
                        bankMax = bankMax == float.PositiveInfinity ? ground : Math.Max(bankMax, ground);
                    }
                }
            }
        }

        return bankMax;
    }

    // One skirt quad down a bank edge: from the strip edge (top0, top1) to below the bed (bottoms buried),
    // facing outward. The triangle order is checked against the outward normal at runtime, so the face can never
    // come out culled: axis-aligned skirts give an exact zero cross product component, never a wrong flip.
    private static void EmitSkirtQuad(
        List<Vector3> vertexList,
        List<Vector3> normalList,
        List<Vector2> uvList,
        List<Color> bankList,
        List<int> indexList,
        float x0, float yt0, float z0,
        float x1, float yt1, float z1,
        float yb0, float yb1,
        Vector3 outward,
        float bankMask)
    {
        var top0 = new Vector3(x0, yt0, z0);
        var top1 = new Vector3(x1, yt1, z1);
        var bottom0 = new Vector3(x0, yb0, z0);
        var bottom1 = new Vector3(x1, yb1, z1);
        TerrainSurface.FaceNormal(top0.X, top0.Y, top0.Z, bottom0.X, bottom0.Y, bottom0.Z, bottom1.X, bottom1.Y, bottom1.Z, out float nx, out float ny, out float nz);
        bool flip = (nx * outward.X) + (ny * outward.Y) + (nz * outward.Z) < 0f;
        int skirtBase = vertexList.Count;
        var mask = new Color(bankMask / 15f, bankMask / 15f, bankMask / 15f, 1f);
        if (!flip)
        {
            AddSkirtVertex(vertexList, normalList, uvList, bankList, top0, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, bottom0, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, bottom1, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, top0, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, bottom1, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, top1, outward, mask);
        }
        else
        {
            AddSkirtVertex(vertexList, normalList, uvList, bankList, top0, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, bottom1, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, bottom0, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, top0, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, top1, outward, mask);
            AddSkirtVertex(vertexList, normalList, uvList, bankList, bottom1, outward, mask);
        }

        for (int k = 0; k < 6; k++)
        {
            indexList.Add(skirtBase + k);
        }
    }

    private static void AddSkirtVertex(
        List<Vector3> vertexList,
        List<Vector3> normalList,
        List<Vector2> uvList,
        List<Color> bankList,
        Vector3 position,
        Vector3 outward,
        Color mask)
    {
        vertexList.Add(position);
        normalList.Add(outward);
        uvList.Add(new Vector2(position.X, position.Z));
        bankList.Add(mask);
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
