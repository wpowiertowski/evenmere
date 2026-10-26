using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Rendering.Overlays;
using Evenmere.Game.Rendering.Terrain;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Exploration;
using Godot;

namespace Evenmere.Game.Rendering.Fog;

/// <summary>
/// The fog of the unknown (M2-23): a calm paper-white haze over the land of each unrevealed region, with a
/// light ink outline that traces coasts and region borders and strengthens slightly on hover. The sea draws
/// nothing — at most a faint one-tile fade into the shallows — so the ocean stays continuous to the horizon
/// and the square map edge never shows, in either look. Per-region meshes are built once when the map
/// arrives and only touched again on <see cref="RegionRevealed"/> (a short dissolve, then the meshes are
/// freed); nothing rebuilds per frame. The sheets render after the look post pass
/// (<see cref="OverlayRenderer.AfterLookPriority"/>), so the translucency survives the inked frame, and the
/// unshaded haze reads the same under the hand-drawn look and <c>--look=classic</c>.
/// </summary>
public sealed partial class FogRenderer : Node3D
{
    private static readonly string _shaderPath = "res://rendering/fog/fog.gdshader";

    // How far the haze rides above the ground, clear of the overlay sheet (OverlayMeshBuilder.Lift 0.08).
    private const float Lift = 0.15f;

    // How far above its tile the outline rides, so the ink never z-fights the haze.
    private const float OutlineLift = 0.03f;

    // Seconds a revealed region's haze takes to dissolve; long enough for a screenshot to catch mid-fade.
    private const float FadeSeconds = 2.5f;

    // Seconds between hover picks; picking marches the whole island, so it never runs per frame.
    private const float HoverCooldownSeconds = 0.15f;

    // The shallows fade is the same paper at a whisper, never a second colour.
    private const float ShallowsAlpha = 0.35f;

    private static readonly Color _inkSoft = new(0.28f, 0.25f, 0.22f, 0.35f);
    private static readonly Color _inkHover = new(0.28f, 0.25f, 0.22f, 0.6f);

    private SimHost? _host;
    private ShaderMaterial? _haze;
    private ShaderMaterial? _hazeFaint;
    private StandardMaterial3D? _outlineSoft;
    private StandardMaterial3D? _outlineHover;
    private readonly Dictionary<ushort, FogPatch> _patches = new();
    private readonly Dictionary<ushort, FadeState> _fading = new();
    private ushort[] _regions = [];
    private float[] _heights = [];
    private float[] _corners = [];
    private bool[] _isWater = [];
    private bool[] _isRiver = [];
    private int _width = -1;
    private int _height = -1;
    private object? _builtFor;
    private double _secondsSinceHover = double.MaxValue;
    private ushort? _hovered;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <inheritdoc />
    public override void _Ready()
    {
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] fog: no SimHost at '{HostPath}'; the island stays unveiled.");
            return;
        }

        _host.EventsDrained += OnEvents;
        Visible = false;
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
        if (_host is not { IsRunning: true } host)
        {
            Visible = false;
            return;
        }

        Visible = true;
        int width = host.Query.Map.Width;
        int height = host.Query.Map.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (width != _width || height != _height || !ReferenceEquals(_builtFor, host.Content))
        {
            RebuildAll(host, width, height);
        }

        if (_fading.Count > 0)
        {
            AdvanceFades(delta);
        }

        _secondsSinceHover += delta;
        if (_secondsSinceHover >= HoverCooldownSeconds)
        {
            _secondsSinceHover = 0.0;
            UpdateHover(host);
        }
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        foreach (SimEvent @event in events)
        {
            if (@event is RegionRevealed revealed)
            {
                BeginFade(revealed.Region);
            }
        }
    }

    private void RebuildAll(SimHost host, int width, int height)
    {
        foreach ((ushort _, FogPatch patch) in _patches)
        {
            patch.Fill.QueueFree();
            patch.Shallows?.QueueFree();
            patch.Outline.QueueFree();
        }

        _patches.Clear();
        _fading.Clear();
        _hovered = null;

        ShaderMaterial haze = _haze ??= new ShaderMaterial { Shader = GD.Load<Shader>(_shaderPath), RenderPriority = OverlayRenderer.AfterLookPriority };
        ShaderMaterial faint = _hazeFaint ??= new ShaderMaterial
        {
            Shader = GD.Load<Shader>(_shaderPath),
            RenderPriority = OverlayRenderer.AfterLookPriority,
        };
        faint.SetShaderParameter("haze_color", new Color(0.957f, 0.941f, 0.878f, 0.55f * ShallowsAlpha));
        StandardMaterial3D soft = _outlineSoft ??= InkMaterial(_inkSoft);
        _outlineHover ??= InkMaterial(_inkHover);

        var revealed = new HashSet<ushort>(host.Query.Exploration.RevealedRegions());
        _width = width;
        _height = height;
        _regions = new ushort[width * height];
        _heights = new float[width * height];
        _isWater = new bool[width * height];
        _isRiver = new bool[width * height];
        System.Collections.Immutable.ImmutableArray<string> terrainNames = host.Content.Terrains.Names;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                TileInfo tile = host.Query.Map.GetTile(x, y);
                int i = (y * width) + x;
                _regions[i] = tile.Region;
                _heights[i] = Math.Max(TerrainSurface.GroundHeight(tile.Elevation), TerrainSurface.WaterSurfaceY) + Lift;
                WaterKind kind = TerrainSurface.Classify(
                    terrainNames[tile.Terrain.Value],
                    (tile.Features & TileFeatures.River) != 0);
                _isWater[i] = kind is WaterKind.Sea or WaterKind.Deep or WaterKind.Reef or WaterKind.River;
                _isRiver[i] = kind == WaterKind.River;
            }
        }

        // The drape: every tile corner takes the averaged height of the tiles sharing it, exactly like
        // the terrain surface, so neighbouring patches agree on boundary values and the haze never steps.
        _corners = new float[(width + 1) * (height + 1)];
        for (int cy = 0; cy <= height; cy++)
        {
            for (int cx = 0; cx <= width; cx++)
            {
                float sum = 0f;
                int count = 0;
                for (int dy = -1; dy <= 0; dy++)
                {
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if (nx >= 0 && ny >= 0 && nx < width && ny < height)
                        {
                            sum += _heights[(ny * width) + nx];
                            count++;
                        }
                    }
                }

                _corners[(cy * (width + 1)) + cx] = count == 0 ? 0f : sum / count;
            }
        }

        // Fogged land, as tile indices: the haze covers land and inland rivers (a riverbed hole would read
        // as a dark diamond), while the open sea draws nothing. The shallows fade hugs these coasts.
        var foggedLand = new HashSet<int>();
        for (int i = 0; i < _regions.Length; i++)
        {
            if ((!_isWater[i] || _isRiver[i]) && !revealed.Contains(_regions[i]))
            {
                foggedLand.Add(i);
            }
        }

        var landByRegion = new Dictionary<ushort, List<int>>();
        foreach (int tile in foggedLand)
        {
            ushort region = _regions[tile];
            if (!landByRegion.TryGetValue(region, out List<int>? tiles))
            {
                tiles = new List<int>();
                landByRegion[region] = tiles;
            }

            tiles.Add(tile);
        }

        // The shallows fade: fogged-region water within one tile of fogged land (Chebyshev), so the haze
        // softens into the shallows instead of ending in a hard inked line at the coast.
        var shallowByRegion = new Dictionary<ushort, List<int>>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                if (!_isWater[i] || _isRiver[i] || revealed.Contains(_regions[i]) || !NearFoggedLand(foggedLand, x, y, width, height))
                {
                    continue;
                }

                ushort region = _regions[i];
                if (!shallowByRegion.TryGetValue(region, out List<int>? tiles))
                {
                    tiles = new List<int>();
                    shallowByRegion[region] = tiles;
                }

                tiles.Add(i);
            }
        }

        var ordered = new List<ushort>(landByRegion.Keys);
        ordered.Sort();
        foreach (ushort region in ordered)
        {
            shallowByRegion.TryGetValue(region, out List<int>? shallows);
            _patches[region] = BuildPatch(region, landByRegion[region], shallows ?? [], width, height, haze, faint, soft);
        }

        GD.Print($"[Evenmere] fog: regions={ordered.Count} biggest={Biggest(landByRegion)} r0land={LandCount(landByRegion, 0)}.");

        _builtFor = host.Content;
        GD.Print($"[Evenmere] fog: built {_patches.Count} patch(es) on a {width}x{height} island.");
    }

    private static bool NearFoggedLand(HashSet<int> foggedLand, int x, int y, int width, int height)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height
                    && foggedLand.Contains((ny * width) + nx))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private FogPatch BuildPatch(
        ushort region, List<int> land, List<int> shallows, int width, int height,
        ShaderMaterial haze, ShaderMaterial faint, StandardMaterial3D soft)
    {
        var fillInstance = new MeshInstance3D
        {
            Mesh = QuadMesh(land, width, height, haze),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(fillInstance);

        MeshInstance3D? shallowInstance = null;
        if (shallows.Count > 0)
        {
            shallowInstance = new MeshInstance3D
            {
                Mesh = QuadMesh(shallows, width, height, faint),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(shallowInstance);
        }

        // The ink edge: one line per land-tile side facing water (the coast) or another region's land.
        // Sea tiles draw no lines, so the open ocean stays unbroken.
        // Outline endpoints ride the shared corners, so the ink drapes with the haze instead of stepping.
        var lines = new List<Vector3>();
        foreach (int tile in land)
        {
            int x = tile % width;
            int y = tile / width;
            if (IsEdge(tile - 1, x == 0, region))
            {
                lines.Add(Corner(x, y));
                lines.Add(Corner(x, y + 1));
            }

            if (IsEdge(tile + 1, x == width - 1, region))
            {
                lines.Add(Corner(x + 1, y));
                lines.Add(Corner(x + 1, y + 1));
            }

            if (IsEdge(tile - width, y == 0, region))
            {
                lines.Add(Corner(x, y));
                lines.Add(Corner(x + 1, y));
            }

            if (IsEdge(tile + width, y == height - 1, region))
            {
                lines.Add(Corner(x, y + 1));
                lines.Add(Corner(x + 1, y + 1));
            }
        }

        var outline = new ArrayMesh();
        var lineArrays = new Godot.Collections.Array();
        lineArrays.Resize((int)Mesh.ArrayType.Max);
        lineArrays[(int)Mesh.ArrayType.Vertex] = lines.ToArray();
        outline.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, lineArrays);
        outline.SurfaceSetMaterial(0, soft);
        var outlineInstance = new MeshInstance3D { Mesh = outline, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        outlineInstance.MaterialOverride = soft;
        AddChild(outlineInstance);
        return new FogPatch(fillInstance, shallowInstance, outlineInstance);
    }

    private Vector3 Corner(int cx, int cy) =>
        new(cx, _corners[(cy * (_width + 1)) + cx] + OutlineLift, cy);

    // A hazed tile's side is an edge when it faces the open sea, the map border, or another region's
    // tiles: both the coast under the haze and the fog's border with revealed land read as one ink line.
    // Rivers inside one region never edge; the sea always does.
    // both the coast under the haze and the fog's border with revealed land read as one ink line.
    private bool IsEdge(int neighbour, bool atMapEdge, ushort region)
    {
        if (atMapEdge)
        {
            return true;
        }

        if (neighbour < 0 || neighbour >= _regions.Length)
        {
            return true;
        }

        if (!_isRiver[neighbour] && _isWater[neighbour])
        {
            return true;
        }

        return _regions[neighbour] != region;
    }

    private Vector3 CornerValue(int cx, int cy) =>
        new(cx, _corners[(cy * (_width + 1)) + cx], cy);

    private ArrayMesh QuadMesh(List<int> tiles, int width, int height, ShaderMaterial material)
    {
        // One flat quad per tile at the haze height, in the terrain's winding, with map-spanning UVs
        // for the paper grain. Tiles are independent quads so regions never share vertices.
        var mesh = new ArrayMesh();
        var vertices = new Vector3[tiles.Count * 4];
        var normals = new Vector3[tiles.Count * 4];
        var uvs = new Vector2[tiles.Count * 4];
        int[] indices = new int[tiles.Count * 6];
        for (int t = 0; t < tiles.Count; t++)
        {
            int tile = tiles[t];
            int x = tile % width;
            int y = tile / width;
            int v = t * 4;
            vertices[v + 0] = CornerValue(x, y);
            vertices[v + 1] = CornerValue(x + 1, y);
            vertices[v + 2] = CornerValue(x + 1, y + 1);
            vertices[v + 3] = CornerValue(x, y + 1);
            for (int k = 0; k < 4; k++)
            {
                normals[v + k] = Vector3.Up;
            }

            uvs[v + 0] = new Vector2((float)x / width, (float)y / height);
            uvs[v + 1] = new Vector2((float)(x + 1) / width, (float)y / height);
            uvs[v + 2] = new Vector2((float)(x + 1) / width, (float)(y + 1) / height);
            uvs[v + 3] = new Vector2((float)x / width, (float)(y + 1) / height);

            // The terrain facets each tile along the diagonal with the smaller height difference (ties go
            // c00-c11); the haze copies that triangulation exactly, so the sheet rides one lift above the
            // visible surface instead of dipping under its facets on slopes.
            float h00 = vertices[v + 0].Y;
            float h10 = vertices[v + 1].Y;
            float h11 = vertices[v + 2].Y;
            float h01 = vertices[v + 3].Y;
            int w = t * 6;
            if (Math.Abs(h00 - h11) <= Math.Abs(h10 - h01))
            {
                indices[w + 0] = v + 0;
                indices[w + 1] = v + 1;
                indices[w + 2] = v + 2;
                indices[w + 3] = v + 0;
                indices[w + 4] = v + 2;
                indices[w + 5] = v + 3;
            }
            else
            {
                indices[w + 0] = v + 0;
                indices[w + 1] = v + 1;
                indices[w + 2] = v + 3;
                indices[w + 3] = v + 1;
                indices[w + 4] = v + 2;
                indices[w + 5] = v + 3;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private void BeginFade(ushort region)
    {
        if (!_patches.TryGetValue(region, out FogPatch? patch))
        {
            return;
        }

        if (_fading.ContainsKey(region))
        {
            return;
        }

        // The shared haze cannot fade per region, so the leaving patch gets its own copies for the animation.
        var fading = new ShaderMaterial { Shader = GD.Load<Shader>(_shaderPath), RenderPriority = OverlayRenderer.AfterLookPriority };
        var fadingFaint = new ShaderMaterial { Shader = GD.Load<Shader>(_shaderPath), RenderPriority = OverlayRenderer.AfterLookPriority };
        fadingFaint.SetShaderParameter("haze_color", new Color(0.957f, 0.941f, 0.878f, 0.55f * ShallowsAlpha));
        patch.Fill.MaterialOverride = fading;
        if (patch.Shallows is not null)
        {
            patch.Shallows.MaterialOverride = fadingFaint;
        }

        _fading[region] = new FadeState(patch, fading, fadingFaint, 0f);
        if (_hovered == region)
        {
            _hovered = null;
        }

        GD.Print($"[Evenmere] fog: region {region} revealed, dissolving.");
    }

    private void AdvanceFades(double delta)
    {
        var done = new List<ushort>();
        foreach ((ushort region, FadeState fade) in _fading)
        {
            float elapsed = fade.Elapsed + (float)delta;
            float left = Math.Clamp(1f - (elapsed / FadeSeconds), 0f, 1f);
            fade.Full.SetShaderParameter("fade", left);
            fade.Faint.SetShaderParameter("fade", left);
            if (left <= 0f)
            {
                done.Add(region);
            }
            else
            {
                _fading[region] = fade with { Elapsed = elapsed };
            }
        }

        foreach (ushort region in done)
        {
            if (_patches.Remove(region, out FogPatch? patch))
            {
                patch.Fill.QueueFree();
                patch.Shallows?.QueueFree();
                patch.Outline.QueueFree();
            }

            _fading.Remove(region);
        }
    }

    private void UpdateHover(SimHost host)
    {
        ushort? hovered = PickRegion(host);
        if (hovered == _hovered)
        {
            return;
        }

        if (_hovered is { } old && _patches.TryGetValue(old, out FogPatch? previous))
        {
            previous.Outline.MaterialOverride = _outlineSoft;
        }

        _hovered = hovered;
        if (hovered is { } next && _patches.TryGetValue(next, out FogPatch? patch))
        {
            patch.Outline.MaterialOverride = _outlineHover;
        }
    }

    private ushort? PickRegion(SimHost host)
    {
        // The pointer over a panel still picks the tile behind it; the outline is a whisper, so that is fine.
        if (GetViewport().GetCamera3D() is not Camera3D camera)
        {
            return null;
        }

        Vector2 mouse = GetViewport().GetMousePosition();
        Vector3 origin = camera.ProjectRayOrigin(mouse);
        Vector3 direction = camera.ProjectRayNormal(mouse);
        float minHeight = TerrainSurface.WaterSurfaceY - 0.5f;
        float maxHeight = 0f;
        foreach (float h in _heights)
        {
            if (h > maxHeight)
            {
                maxHeight = h;
            }
        }

        if (!TilePicker.TryPick(
            new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
            new MapBounds(_width, _height),
            (x, y) => _heights[(y * _width) + x] - Lift,
            minHeight,
            maxHeight,
            out int tileX,
            out int tileY))
        {
            return null;
        }

        ushort region = _regions[(tileY * _width) + tileX];
        return _patches.ContainsKey(region) ? region : null;
    }

    private static StandardMaterial3D InkMaterial(Color colour) => new()
    {
        AlbedoColor = colour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        RenderPriority = OverlayRenderer.AfterLookPriority,
    };

    private static int LandCount(Dictionary<ushort, List<int>> byRegion, ushort region) =>
        byRegion.TryGetValue(region, out List<int>? tiles) ? tiles.Count : -1;

    private static string Biggest(Dictionary<ushort, List<int>> byRegion)
    {
        ushort best = 0;
        int count = -1;
        foreach ((ushort region, List<int> tiles) in byRegion)
        {
            if (tiles.Count > count)
            {
                count = tiles.Count;
                best = region;
            }
        }

        return $"{best}:{count}";
    }

    private sealed record FogPatch(MeshInstance3D Fill, MeshInstance3D? Shallows, MeshInstance3D Outline);

    private sealed record FadeState(FogPatch Patch, ShaderMaterial Full, ShaderMaterial Faint, float Elapsed);
}
