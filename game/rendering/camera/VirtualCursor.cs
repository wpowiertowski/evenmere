using Evenmere.Game.Controls;
using Evenmere.Game.Rendering.Overlays;
using Godot;

namespace Evenmere.Game.Rendering.Camera;

/// <summary>
/// The gamepad's pointer. The right stick moves a point across the screen, and the tile under that point is the
/// cursor: a flat ring drawn on the tile, snapping from tile to tile. Panning the camera moves the map under the
/// cursor, like moving a mouse over a map. It is only active in <see cref="InputMode.Gamepad"/>.
/// </summary>
/// <remarks>It must be a child of a <see cref="CameraRig"/>, which activates it, and it runs after the rig each frame so the ring never lags the picture.</remarks>
public sealed partial class VirtualCursor : Node3D
{
    private const int RingSegments = 48;

    private CameraRig? _rig;
    private bool _hasTile;
    private Vector2I _tile;

    /// <summary>Gets or sets how fast a fully pushed stick moves the cursor, in screen heights per second. Resolution does not change how it feels.</summary>
    [Export(PropertyHint.Range, "0.1,3,0.05")]
    public float SpeedScreenHeightsPerSecond { get; set; } = 0.9f;

    /// <summary>Gets or sets the distance in pixels the cursor keeps from the screen edge.</summary>
    [Export(PropertyHint.Range, "0,200,1,suffix:px")]
    public float ScreenMargin { get; set; } = 24f;

    /// <summary>Gets or sets how far above the ground, in world units, the ring floats so it does not fight the terrain.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:units")]
    public float Lift { get; set; } = 0.06f;

    /// <summary>Gets or sets the ring colour.</summary>
    [Export]
    public Color RingColor { get; set; } = new(1f, 0.9f, 0.6f, 1f);

    /// <summary>Gets a value indicating whether the cursor is in use, which is true only in gamepad mode.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the cursor's position on screen, in viewport coordinates. This is the point the tile is picked from.</summary>
    public Vector2 ScreenPosition { get; private set; }

    /// <summary>Gets the tile under the cursor as of the last frame.</summary>
    /// <param name="tile">The tile when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the cursor is inactive or is not over the map.</returns>
    public bool TryGetTile(out Vector2I tile)
    {
        tile = _tile;
        return IsActive && _hasTile;
    }

    /// <summary>Turns the cursor on or off. Turning it on puts it in the middle of the screen, which is where the camera is looking.</summary>
    /// <param name="active">Whether the gamepad is the active device.</param>
    public void SetActive(bool active)
    {
        if (active == IsActive)
        {
            return;
        }

        IsActive = active;
        _hasTile = false;
        Visible = false;
        if (active)
        {
            ScreenPosition = GetViewport().GetVisibleRect().GetCenter();
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _rig = GetParentOrNull<CameraRig>();

        // Detached from the rig's transform: the ring lives at a tile in world space, not relative to the camera pivot.
        TopLevel = true;
        ProcessPriority = 1;
        Visible = false;

        AddChild(new MeshInstance3D
        {
            Name = "Ring",
            Mesh = BuildRing(0.34f, 0.46f, RingSegments),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = RingColor,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                RenderPriority = OverlayRenderer.AfterLookPriority,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!IsActive || _rig is null)
        {
            return;
        }

        Vector2 stick = _rig.InputEnabled
            ? Input.GetVector(InputActions.CursorLeft, InputActions.CursorRight, InputActions.CursorUp, InputActions.CursorDown)
            : Vector2.Zero;

        // Squaring the deflection gives fine control near the centre and full speed at the rim.
        stick *= stick.Length();

        Rect2 area = GetViewport().GetVisibleRect().GrowIndividual(-ScreenMargin, -ScreenMargin, -ScreenMargin, -ScreenMargin);
        Vector2 next = ScreenPosition + (stick * SpeedScreenHeightsPerSecond * area.Size.Y * (float)delta);
        ScreenPosition = next.Clamp(area.Position, area.End);

        _hasTile = _rig.TryPickTile(ScreenPosition, out _tile);
        Visible = _hasTile;
        if (_hasTile)
        {
            float ground = _rig.HeightAt(_tile.X, _tile.Y);
            GlobalPosition = new Vector3(MapBounds.TileCenter(_tile.X), ground + Lift, MapBounds.TileCenter(_tile.Y));
        }
    }

    /// <summary>Builds a flat ring in the XZ plane, centred on the origin, facing up.</summary>
    private static ArrayMesh BuildRing(float innerRadius, float outerRadius, int segments)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetNormal(Vector3.Up);
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Tau * i / segments;
            float a1 = Mathf.Tau * (i + 1) / segments;
            Vector3 inner0 = new(MathF.Cos(a0) * innerRadius, 0f, MathF.Sin(a0) * innerRadius);
            Vector3 outer0 = new(MathF.Cos(a0) * outerRadius, 0f, MathF.Sin(a0) * outerRadius);
            Vector3 inner1 = new(MathF.Cos(a1) * innerRadius, 0f, MathF.Sin(a1) * innerRadius);
            Vector3 outer1 = new(MathF.Cos(a1) * outerRadius, 0f, MathF.Sin(a1) * outerRadius);

            surface.AddVertex(inner0);
            surface.AddVertex(outer0);
            surface.AddVertex(outer1);
            surface.AddVertex(inner0);
            surface.AddVertex(outer1);
            surface.AddVertex(inner1);
        }

        return surface.Commit();
    }
}
