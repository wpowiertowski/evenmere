using Evenmere.Content;
using Evenmere.Game.Host;
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
    private static readonly string _riverShaderPath = "res://rendering/water/river.gdshader";

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

        Shader? seaShader = GD.Load<Shader>(_seaShaderPath);
        Shader? riverShader = GD.Load<Shader>(_riverShaderPath);
        if (seaShader is null || riverShader is null)
        {
            GD.PushError("[Evenmere] Water: missing sea or river shader.");
            return;
        }

        var seaMaterial = new ShaderMaterial { Shader = seaShader };
        seaMaterial.SetShaderParameter("shore_tex", ImageTexture.CreateFromImage(image));
        seaMaterial.SetShaderParameter("map_size", new Vector2(map.Width, map.Height));

        var seaMesh = new PlaneMesh
        {
            Size = new Vector2(map.Width, map.Height),
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

                // The same diagonal rule as the terrain tile, so strip and bed never cross.
                if (Math.Abs(h00 - h11) <= Math.Abs(h10 - h01))
                {
                    EmitStripTriangle(vertexList, normalList, uvList, indexList, x, h00, y, x + 1, h10, y, x + 1, h11, y + 1);
                    EmitStripTriangle(vertexList, normalList, uvList, indexList, x, h00, y, x + 1, h11, y + 1, x, h01, y + 1);
                }
                else
                {
                    EmitStripTriangle(vertexList, normalList, uvList, indexList, x, h00, y, x + 1, h10, y, x, h01, y + 1);
                    EmitStripTriangle(vertexList, normalList, uvList, indexList, x + 1, h10, y, x + 1, h11, y + 1, x, h01, y + 1);
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
        int[] indices = indexList.ToArray();

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = stripNormals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        MeshInstance3D strips = new()
        {
            Name = "RiverStrips",
            Mesh = mesh,
            MaterialOverride = new ShaderMaterial { Shader = riverShader },
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
        List<int> indexList,
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz)
    {
        TerrainSurface.FaceNormal(ax, ay, az, bx, by, bz, cx, cy, cz, out float nx, out float ny, out float nz);
        int stripBase = vertexList.Count;
        vertexList.Add(new Vector3(ax, ay, az));
        vertexList.Add(new Vector3(bx, by, bz));
        vertexList.Add(new Vector3(cx, cy, cz));
        normalList.Add(new Vector3(nx, ny, nz));
        normalList.Add(new Vector3(nx, ny, nz));
        normalList.Add(new Vector3(nx, ny, nz));
        for (int k = 0; k < 3; k++)
        {
            Vector3 v = vertexList[stripBase + k];
            uvList.Add(new Vector2(v.X, v.Z));
            indexList.Add(stripBase + k);
        }
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
