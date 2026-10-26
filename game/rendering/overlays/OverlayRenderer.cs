using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Terrain;
using Godot;

namespace Evenmere.Game.Rendering.Overlays;

/// <summary>
/// Draws one overlay layer as a translucent sheet over the island: a grid mesh that follows the terrain heights
/// (built by <see cref="OverlayMeshBuilder"/>) textured with the layer bytes through <see cref="OverlayPalette"/>.
/// The sheet rebuilds when the map size changes and re-tints when the tick moves on, so reach and coverage stay
/// current as roads and buildings change. Hidden until <see cref="Show"/> is called; the panel, the placement tools
/// and the <c>--overlay-shot</c> debug flag are the only callers.
/// </summary>
public sealed partial class OverlayRenderer : Node3D
{
    private const string ShotPrefix = "--overlay-shot=";
    private static readonly string _shaderPath = "res://rendering/overlays/overlay.gdshader";

    /// <summary>
    /// The render priority of the sheet: above the look post pass (M2-29), whose quad rides in front of the
    /// camera at priority 10 and replaces blended transparents with the un-tinted frame. Compositing after it
    /// tints the finished, inked frame. Shared by the placement ghost and cursor ring materials, which had the
    /// same bug. Don't edit <c>game/rendering/look/**</c>: it belongs to M2-32.
    /// </summary>
    public const int AfterLookPriority = 20;

    // How long a tint stays before the sheet re-reads the layer: re-tinting rebuilds the surface, so a running
    // game with an open overlay re-reads twice a second instead of every tick.
    private const ulong TintCooldownMsec = 500;

    private readonly OverlayMeshBuilder _builder = new();
    private ShaderMaterial? _material;

    private MeshInstance3D? _sheet;
    private ArrayMesh? _mesh;
    private SimHost? _host;
    private OverlaySpec? _spec;
    private long _tick = -1;
    private ulong _tintedAt;
    private int _width = -1;
    private int _height = -1;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets the layer on show, if any.</summary>
    public OverlaySpec? Shown => _spec;
    /// <summary>Shows a layer, building the sheet for the current map on the first show.</summary>
    /// <param name="spec">The layer.</param>
    public void Show(OverlaySpec spec)
    {
        _spec = spec;
        Visible = true;
        _tick = -1;
    }

    /// <summary>Hides the sheet.</summary>
    public void HideLayer()
    {
        _spec = null;
        Visible = false;
        if (_sheet is not null)
        {
            _sheet.Visible = false;
        }
    }

    /// <summary>
    /// The layer the placement tools should show for a building: fertility for farms, coverage for services, reach
    /// for storehouses, buildable otherwise (see <see cref="OverlayViewModel.SuggestFor"/>).
    /// </summary>
    /// <param name="type">The building type being placed.</param>
    /// <returns>The layer, or <see langword="null"/> when no game is running.</returns>
    public OverlaySpec? SuggestFor(BuildingTypeId type)
    {
        SimHost? host = Host();
        if (host is not { IsRunning: true })
        {
            return null;
        }

        return new OverlayViewModel(host.Content, host.Query.Overlay).SuggestFor(type);
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Visible = true;
        if (ShotRequested(out OverlaySpec shot))
        {
            _spec = shot;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_spec is not { } spec || Host() is not { IsRunning: true } host)
        {
            return;
        }

        int width = host.Query.Map.Width;
        int height = host.Query.Map.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (width != _width || height != _height)
        {
            RebuildSheet(host, width, height);
        }

        ulong now = Time.GetTicksMsec();
        if (host.CurrentTick == _tick && now - _tintedAt < TintCooldownMsec)
        {
            return;
        }

        Tint(host, spec, width, height);
    }

    private SimHost? Host()
    {
        _host ??= GetNodeOrNull<SimHost>(HostPath);
        return _host;
    }

    private void RebuildSheet(SimHost host, int width, int height)
    {
        float[] elevations = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                elevations[(y * width) + x] = TerrainSurface.GroundHeight(host.Query.Map.GetTile(x, y).Elevation);
            }
        }

        _builder.Rebuild(elevations, width, height);
        _width = width;
        _height = height;
        _tick = -1;
    }

    private void Tint(SimHost host, OverlaySpec spec, int width, int height)
    {
        byte[] layer = new OverlayViewModel(host.Content, host.Query.Overlay).Fetch(spec);
        if (layer.Length != width * height)
        {
            return;
        }

        var swatches = new OverlaySwatch[layer.Length];
        for (int i = 0; i < layer.Length; i++)
        {
            swatches[i] = OverlayPalette.ColorFor(spec, layer[i]);
        }

        _builder.Retint(swatches);
        UploadSheet();
        _material?.SetShaderParameter("overlay_alpha", OverlayPalette.AlphaFor(spec));
        _tick = host.CurrentTick;
        _tintedAt = Time.GetTicksMsec();
    }

    private void UploadSheet()
    {
        Vector3[] verts = ToVectorArray(_builder.Vertices);
        Vector3[] normals = ToVectorArray(_builder.Normals);
        Vector2[] uvs = ToVector2Array(_builder.Uvs);
        Color[] colours = ToColorArray(_builder.Colours);
        int[] indices = _builder.Indices.ToArray();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        _mesh ??= new ArrayMesh();
        _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _material ??= new ShaderMaterial { Shader = GD.Load<Shader>(_shaderPath), RenderPriority = AfterLookPriority };
        _mesh.SurfaceSetMaterial(0, _material);

        _sheet ??= new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        if (_sheet.GetParent() is null)
        {
            AddChild(_sheet);
        }

        _sheet.Mesh = _mesh;
        _sheet.Visible = true;
    }

    private static bool ShotRequested(out OverlaySpec spec)
    {
        spec = OverlaySpec.Reach;
        if (!OS.IsDebugBuild())
        {
            return false;
        }

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(ShotPrefix, StringComparison.Ordinal)
                && OverlaySpec.TryParseShot(arg[ShotPrefix.Length..], out OverlaySpec parsed))
            {
                spec = parsed;
                return true;
            }
        }

        return false;
    }

    private static Vector3[] ToVectorArray(ReadOnlySpan<float> triples)
    {
        var vectors = new Vector3[triples.Length / 3];
        for (int i = 0; i < vectors.Length; i++)
        {
            vectors[i] = new Vector3(triples[(i * 3) + 0], triples[(i * 3) + 1], triples[(i * 3) + 2]);
        }

        return vectors;
    }

    private static Vector2[] ToVector2Array(ReadOnlySpan<float> pairs)
    {
        var vectors = new Vector2[pairs.Length / 2];
        for (int i = 0; i < vectors.Length; i++)
        {
            vectors[i] = new Vector2(pairs[(i * 2) + 0], pairs[(i * 2) + 1]);
        }

        return vectors;
    }

    private static Color[] ToColorArray(ReadOnlySpan<float> quadruples)
    {
        var colours = new Color[quadruples.Length / 4];
        for (int i = 0; i < colours.Length; i++)
        {
            colours[i] = new Color(quadruples[(i * 4) + 0], quadruples[(i * 4) + 1], quadruples[(i * 4) + 2], quadruples[(i * 4) + 3]);
        }

        return colours;
    }
}
