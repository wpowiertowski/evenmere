using System.Globalization;
using Evenmere.Game.Controls;
using Evenmere.Game.Rendering.Look;
using Evenmere.Game.Ui.Common;
using Godot;
using SysVector3 = System.Numerics.Vector3;

namespace Evenmere.Game.Rendering.Camera;

/// <summary>
/// The player's view of the island: a camera that orbits a focus point on the ground, in perspective (the default)
/// or in the orthographic prototype. It pans with
/// WASD, the arrows, the left stick, the screen edge (optional) and middle-drag; zooms with the wheel, = and -, a
/// pinch and the triggers; and turns in 90° steps with Q and E or the shoulder buttons. All motion is eased and uses
/// closed-form integrators (<see cref="CameraMath"/>), so it feels the same at any frame rate.
/// </summary>
/// <remarks>
/// <para>
/// The rig node sits on the focus point and is turned by the view rotation; the child <c>Camera3D</c> is placed
/// <see cref="Zoom"/> units away along the pitch. Tile (x, y) maps to world (x + 0.5, 0, y + 0.5), one unit per tile.
/// </para>
/// <para>
/// Tools pick tiles through <see cref="TryPickTile"/>, <see cref="TryPickCursorTile"/> or <see cref="TryPickPointerTile"/>.
/// </para>
/// </remarks>
public sealed partial class CameraRig : Node3D
{
    private const float MaxStepSeconds = 0.25f;
    private const string CheckZoomPrefix = "--check-zoom=";
    private const float FocusHeightResponse = 8f;
    private const float SettleDistance = 1e-4f;
    private const float SettleVelocity = 1e-3f;

    private static readonly Func<int, int, float> _flatGround = static (_, _) => 0f;

    private Camera3D? _camera;
    private InputModeTracker? _tracker;
    private VirtualCursor? _cursor;
    private Func<int, int, float> _heightAt = _flatGround;

    private Vector2 _focus;
    private float _focusHeight;
    private float _velocityX;
    private float _velocityZ;
    private float _logZoom;
    private float _targetLogZoom;
    private float _yaw;
    private float _yawVelocity;
    private int _rotationSteps;
    private float? _yawFixed;

    private bool _dragging;
    private Vector2 _pointer;
    private Vector2I _lastFovSize = new(-1, -1);
    private float _lastFovScreen = -1f;

    // Debug-only --check-zoom state (M2-31 evidence): 0 idle, 1 warp the pointer, 2 inject the wheel, 3 read the zoom.
    private int _checkZoomStage;
    private Vector2 _checkZoomPoint = Vector2.Zero;
    private float _checkZoomBefore;
    private ulong _checkZoomFrame;

    // True from the first mouse-motion event until the pointer leaves the window. Entering the window is not enough: the
    // position is only known from a motion event, and edge scrolling must never act on a guess.
    private bool _pointerInside;
    private bool _windowFocused;

    /// <summary>Gets or sets a value indicating whether moving the mouse to the screen edge pans the view. It never applies in gamepad mode.</summary>
    [Export]
    public bool EdgeScrollEnabled { get; set; } = true;

    /// <summary>Gets or sets how close to the window edge, in pixels, the pointer must be to start edge scrolling.</summary>
    [Export(PropertyHint.Range, "1,100,1,suffix:px")]
    public float EdgeScrollMargin { get; set; } = 12f;

    /// <summary>Gets or sets the closest zoom (street level): the camera's distance from the focus point, in world units.</summary>
    [Export(PropertyHint.Range, "1,100,0.5,suffix:units")]
    public float MinZoom { get; set; } = 8f;

    /// <summary>Gets or sets the farthest zoom (whole-island view): the camera's distance from the focus point, in world units.</summary>
    [Export(PropertyHint.Range, "20,1000,1,suffix:units")]
    public float MaxZoom { get; set; } = 220f;

    /// <summary>Gets or sets the zoom the view starts at.</summary>
    [Export(PropertyHint.Range, "1,1000,1,suffix:units")]
    public float InitialZoom { get; set; } = 70f;

    /// <summary>Gets or sets the camera pitch at street level, in degrees below the horizon.</summary>
    [Export(PropertyHint.Range, "10,89,0.5,suffix:°")]
    public float StreetPitchDegrees { get; set; } = 30f;

    /// <summary>Gets or sets the camera pitch at island view, in degrees below the horizon.</summary>
    [Export(PropertyHint.Range, "10,89,0.5,suffix:°")]
    public float IslandPitchDegrees { get; set; } = 50f;

    /// <summary>Gets or sets the isometric-style starting angle at street zoom, in degrees. The offset blends toward
    /// island view with zoom, and rotation still turns in 90° steps from it.</summary>
    [Export(PropertyHint.Range, "0,90,0.5,suffix:°")]
    public float YawOffsetDegrees { get; set; } = 30f;

    /// <summary>The camera's projection: the M1 perspective view, or the orthographic isometric view.</summary>
    public enum CameraProjection
    {
        /// <summary>The M1 look: perspective with the M2-31 height-driven field of view (<c>--look=classic</c>).</summary>
        Perspective = 0,

        /// <summary>The hand-drawn look: an orthographic view whose span scales with the window height. This is the default.</summary>
        Orthographic = 1,
    }

    /// <summary>Gets or sets the camera's projection. Orthographic is the default; classic runs switch to perspective.</summary>
    [Export]
    public CameraProjection Projection { get; set; } = CameraProjection.Orthographic;

    /// <summary>Gets or sets how fast a full pan input moves the focus, in zoom distances per second. Panning is slower when zoomed in and faster when zoomed out.</summary>
    [Export(PropertyHint.Range, "0.05,3,0.05")]
    public float PanSpeed { get; set; } = 0.6f;

    /// <summary>Gets or sets how quickly the pan velocity follows the input, per second. Lower is floatier.</summary>
    [Export(PropertyHint.Range, "1,40,0.5")]
    public float PanResponse { get; set; } = 10f;

    /// <summary>Gets or sets the distance ratio of one mouse-wheel notch. Below 1; each notch in multiplies the zoom target by this.</summary>
    [Export(PropertyHint.Range, "0.5,0.99,0.01")]
    public float ZoomStep { get; set; } = 0.87f;

    /// <summary>Gets or sets how fast holding a zoom key or trigger zooms, in e-folds of distance per second.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.1")]
    public float KeyZoomSpeed { get; set; } = 1.4f;

    /// <summary>Gets or sets how quickly the zoom follows its target, per second.</summary>
    [Export(PropertyHint.Range, "1,40,0.5")]
    public float ZoomResponse { get; set; } = 9f;

    /// <summary>Gets or sets the stiffness of the rotation easing, per second. A quarter turn settles in about 4.6 divided by this many seconds.</summary>
    [Export(PropertyHint.Range, "2,40,0.5")]
    public float RotationResponse { get; set; } = 9f;

    /// <summary>Gets or sets the map width in tiles. The focus point is kept inside the map.</summary>
    [Export(PropertyHint.Range, "1,4096,1")]
    public int MapWidthTiles { get; set; } = 128;

    /// <summary>Gets or sets the map height in tiles (the Y of a tile coordinate, world Z).</summary>
    [Export(PropertyHint.Range, "1,4096,1")]
    public int MapHeightTiles { get; set; } = 128;

    /// <summary>Gets or sets how far past the map edge, in world units, the focus point may go.</summary>
    [Export(PropertyHint.Range, "0,64,0.5,suffix:units")]
    public float BoundsMargin { get; set; }

    /// <summary>Gets or sets the highest ground height in the map, in world units. Picking starts its march at this height, so keep it at or above the real maximum. Flat ground needs 0.</summary>
    [Export(PropertyHint.Range, "0,256,0.5,suffix:units")]
    public float MaxTerrainHeight { get; set; }

    /// <summary>Gets or sets a value indicating whether the rig reads input. A modal menu can switch it off to freeze the view.</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the ground height of tile (x, y) in world units, which picking and the focus height use. It is
    /// flat ground at 0 until terrain rendering (M1-19) installs the real function. Setting <see langword="null"/> restores flat ground.
    /// </summary>
    public Func<int, int, float> HeightAt
    {
        get => _heightAt;
        set => _heightAt = value ?? _flatGround;
    }

    /// <summary>Gets the child camera.</summary>
    public Camera3D? Camera => _camera;

    /// <summary>Gets the device the player used last. It is <see cref="InputMode.MouseKeyboard"/> until a gamepad is touched.</summary>
    public InputMode Mode => _tracker?.Mode ?? InputMode.MouseKeyboard;

    /// <summary>Gets the camera's current distance from the focus point, in world units (between <see cref="MinZoom"/> and <see cref="MaxZoom"/>).</summary>
    public float Zoom => MathF.Exp(_logZoom);

    /// <summary>Gets the point on the ground the camera looks at, in world coordinates.</summary>
    public Vector3 FocusPoint => new(_focus.X, _focusHeight, _focus.Y);

    /// <summary>Gets the view's current rotation about the vertical axis, in radians, including any easing in progress.</summary>
    public float Yaw => _yaw;

    /// <summary>Gets which of the four quarter turns the view is turning to or resting at, from 0 to 3. Zero looks toward -Z (north) from the +Z side.</summary>
    public int RotationQuadrant => CameraMath.QuadrantForSteps(_rotationSteps);

    /// <summary>Gets the map size in tiles.</summary>
    public MapBounds Bounds => new(MapWidthTiles, MapHeightTiles);

    /// <inheritdoc />
    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        _tracker = GetNode<InputModeTracker>("InputModeTracker");
        _cursor = GetNodeOrNull<VirtualCursor>("VirtualCursor");

        // The launch look decides the projection; debug experiment switches may override both afterwards.
        LookMode activeLook = LookSettings.ActiveLook;
        if (activeLook == LookMode.Classic)
        {
            Projection = CameraProjection.Perspective;
        }

        LookSettings.ApplyCameraOverrides(this);
        GD.Print($"[Evenmere] Look: {activeLook} look, {Projection} projection.");

        _tracker.ModeChanged += OnModeChanged;
        OnModeChanged(_tracker.Mode);

        Window window = GetWindow();
        _windowFocused = window.HasFocus();
        window.MouseExited += OnMouseExited;
        window.FocusEntered += OnFocusEntered;
        window.FocusExited += OnFocusExited;
        window.SizeChanged += ApplyProjection;

        (float centerX, float centerZ) = Bounds.Center();
        _focus = new Vector2(centerX, centerZ);
        (float minZoom, float maxZoom) = ZoomRange();
        _logZoom = _targetLogZoom = MathF.Log(Math.Clamp(InitialZoom, minZoom, maxZoom));
        _yaw = CameraMath.TargetYawRadians(_rotationSteps, CurrentYawOffsetDegrees());
        _yawVelocity = 0f;
        _focusHeight = FocusGroundHeight();
        _lastFovSize = GetWindow().Size;
        _lastFovScreen = DisplayServer.ScreenGetScale((int)DisplayServer.ScreenOfMainWindow);
        ApplyProjection();

        // Debug evidence only: --check-zoom=sx,sy warps the pointer there, injects one wheel-up through the real
        // input path and logs the hovered control and the zoom before and after (M2-31).
        if (OS.IsDebugBuild())
        {
            ParseCheckZoom();
        }

        ApplyTransform(minZoom, maxZoom);

        if (_camera is not null)
        {
            LookPost.AttachIfEnabled(_camera, activeLook);
        }

        // The hand-drawn scene light (warm key, matched ambient); classic runs skip it and keep the M1 sun.
        if (GetTree().CurrentScene is not null)
        {
            LookScene.ApplyIfEnabled(GetTree().CurrentScene, activeLook);
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_tracker is not null)
        {
            _tracker.ModeChanged -= OnModeChanged;
        }

        Window window = GetWindow();
        window.MouseExited -= OnMouseExited;
        window.FocusEntered -= OnFocusEntered;
        window.FocusExited -= OnFocusExited;
        window.SizeChanged -= ApplyProjection;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        CheckFovScale();
        RefreshOrthoSize();
        if (_checkZoomStage != 0)
        {
            TickCheckZoom();
        }

        float dt = Math.Min((float)delta, MaxStepSeconds);
        (float minZoom, float maxZoom) = ZoomRange();

        UpdateZoom(dt, minZoom, maxZoom);
        UpdateRotation(dt);
        UpdatePan(dt);
        UpdateFocusHeight(dt);
        ApplyTransform(minZoom, maxZoom);
    }

    /// <summary>Tracks the pointer and ends a drag. It runs before the UI so a button release over a panel still ends the drag.</summary>
    /// <param name="event">The input event.</param>
    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                _pointer = motion.Position;
                _pointerInside = true;
                if (_dragging)
                {
                    DragBy(motion);
                }

                break;

            case InputEventMouseButton when _dragging && @event.IsActionReleased(InputActions.CameraDrag):
                _dragging = false;
                break;
        }
    }

    /// <summary>Handles the discrete inputs the UI did not use: starting a drag, wheel and pinch zoom (only when the pointer is over no blocking control), and quarter turns.</summary>
    /// <param name="event">The input event.</param>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton button when button.IsActionPressed(InputActions.CameraDrag):
                _dragging = true;
                break;

            case InputEventMouseButton button when button.IsActionPressed(InputActions.CameraZoomIn):
                if (CameraMath.ShouldPointerZoom(GetViewport().GuiGetHoveredControl() is not null))
                {
                    ZoomByNotches(button.Factor > 0f ? button.Factor : 1f);
                }

                break;

            case InputEventMouseButton button when button.IsActionPressed(InputActions.CameraZoomOut):
                if (CameraMath.ShouldPointerZoom(GetViewport().GuiGetHoveredControl() is not null))
                {
                    ZoomByNotches(-(button.Factor > 0f ? button.Factor : 1f));
                }

                break;

            case InputEventMagnifyGesture pinch when pinch.Factor > 0f:
                // Fingers apart (factor above 1) bring the camera closer.
                if (CameraMath.ShouldPointerZoom(GetViewport().GuiGetHoveredControl() is not null))
                {
                    _targetLogZoom -= MathF.Log(pinch.Factor);
                    ClampTargetZoom();
                }

                break;

            case InputEventAction or InputEventKey or InputEventJoypadButton:
                if (@event.IsActionPressed(InputActions.CameraRotateLeft))
                {
                    RotateView(-1);
                }
                else if (@event.IsActionPressed(InputActions.CameraRotateRight))
                {
                    RotateView(1);
                }

                break;
        }
    }

    /// <summary>Turns the view by quarter turns about the focus point. The motion is eased.</summary>
    /// <param name="quarterTurns">How many quarter turns; negative turns left (the camera moves to the left around the island), positive turns right.</param>
    public void RotateView(int quarterTurns) => _rotationSteps += quarterTurns;

    /// <summary>Moves the focus point to the middle of a tile at once, within the map bounds.</summary>
    /// <param name="tile">The tile.</param>
    public void FocusOnTile(Vector2I tile) => SetFocus(MapBounds.TileCenter(tile.X), MapBounds.TileCenter(tile.Y));

    /// <summary>Moves the focus point to a world position at once (X and Z), within the map bounds.</summary>
    /// <param name="worldX">The world X.</param>
    /// <param name="worldZ">The world Z.</param>
    public void SetFocus(float worldX, float worldZ)
    {
        _velocityX = 0f;
        _velocityZ = 0f;
        MoveFocusTo(worldX, worldZ);
        _focusHeight = FocusGroundHeight();
    }

    /// <summary>Sets the zoom distance.</summary>
    /// <param name="zoom">The distance in world units; it is clamped to the zoom range.</param>
    /// <param name="instant">When true the view jumps there; otherwise it eases.</param>
    public void SetZoom(float zoom, bool instant = false)
    {
        (float minZoom, float maxZoom) = ZoomRange();
        _targetLogZoom = MathF.Log(Math.Clamp(zoom, minZoom, maxZoom));
        if (instant)
        {
            _logZoom = _targetLogZoom;
        }
    }

    /// <summary>Sets the map size, which bounds the focus point and picking.</summary>
    /// <param name="widthTiles">The width in tiles.</param>
    /// <param name="heightTiles">The height in tiles.</param>
    public void SetMapBounds(int widthTiles, int heightTiles)
    {
        MapWidthTiles = Math.Max(1, widthTiles);
        MapHeightTiles = Math.Max(1, heightTiles);
        MoveFocusTo(_focus.X, _focus.Y);
    }

    /// <summary>
    /// Finds the tile under a screen point by casting the camera ray onto the terrain (<see cref="HeightAt"/>).
    /// </summary>
    /// <param name="screenPoint">The point in viewport coordinates, as a mouse event reports it.</param>
    /// <param name="tile">The tile (x, y) when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the ray misses the map (above the horizon or off the island).</returns>
    public bool TryPickTile(Vector2 screenPoint, out Vector2I tile)
    {
        tile = default;
        if (_camera is not { } camera || !camera.IsInsideTree())
        {
            return false;
        }

        Vector3 origin = camera.ProjectRayOrigin(screenPoint);
        Vector3 direction = camera.ProjectRayNormal(screenPoint);
        bool hit = TilePicker.TryPick(
            new SysVector3(origin.X, origin.Y, origin.Z),
            new SysVector3(direction.X, direction.Y, direction.Z),
            Bounds,
            _heightAt,
            0f,
            MaxTerrainHeight,
            out int tileX,
            out int tileY);
        tile = new Vector2I(tileX, tileY);
        return hit;
    }

    /// <summary>Finds the tile under the gamepad's virtual cursor. It only succeeds in gamepad mode.</summary>
    /// <param name="tile">The tile when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> in mouse and keyboard mode, or when the cursor is not over the map.</returns>
    public bool TryPickCursorTile(out Vector2I tile)
    {
        tile = default;
        return _cursor is { IsActive: true } cursor && cursor.TryGetTile(out tile);
    }

    /// <summary>Finds the tile under whichever pointer is in use: the mouse, or the virtual cursor in gamepad mode. Tools should call this.</summary>
    /// <param name="tile">The tile when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the pointer is not over the map.</returns>
    public bool TryPickPointerTile(out Vector2I tile) =>
        Mode == InputMode.Gamepad ? TryPickCursorTile(out tile) : TryPickTile(GetViewport().GetMousePosition(), out tile);

    private (float Min, float Max) ZoomRange()
    {
        float min = Math.Max(0.1f, MinZoom);
        float max = Math.Max(min * 1.01f, MaxZoom);
        return (min, max);
    }

    private float FocusGroundHeight()
    {
        MapBounds bounds = Bounds;
        int x = Math.Clamp(MapBounds.TileOf(_focus.X), 0, bounds.WidthTiles - 1);
        int y = Math.Clamp(MapBounds.TileOf(_focus.Y), 0, bounds.HeightTiles - 1);
        return _heightAt(x, y);
    }

    private bool IsTextEditing() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;

    private Vector2 ReadPanInput()
    {
        if (!InputEnabled)
        {
            return Vector2.Zero;
        }

        Vector2 input = IsTextEditing()
            ? Vector2.Zero
            : Input.GetVector(InputActions.CameraPanLeft, InputActions.CameraPanRight, InputActions.CameraPanUp, InputActions.CameraPanDown);
        return (input + ReadEdgeScroll()).LimitLength(1f);
    }

    private Vector2 ReadEdgeScroll()
    {
        // The pointer position comes from events, not from polling, so a headless run or a window that has never seen
        // the mouse never scrolls by itself.
        if (!EdgeScrollEnabled || _dragging || Mode != InputMode.MouseKeyboard || !_pointerInside || !_windowFocused)
        {
            return Vector2.Zero;
        }

        Rect2 view = GetViewport().GetVisibleRect();
        float margin = EdgeScrollMargin;
        float x = 0f;
        float y = 0f;
        if (_pointer.X <= view.Position.X + margin)
        {
            x = -1f;
        }
        else if (_pointer.X >= view.End.X - margin)
        {
            x = 1f;
        }

        if (_pointer.Y <= view.Position.Y + margin)
        {
            y = -1f;
        }
        else if (_pointer.Y >= view.End.Y - margin)
        {
            y = 1f;
        }

        return new Vector2(x, y);
    }

    private void UpdateZoom(float dt, float minZoom, float maxZoom)
    {
        if (InputEnabled && !IsTextEditing())
        {
            // Wheel notches arrive as events (ZoomByNotches); keys and triggers are held, so they are polled here.
            float held = Input.GetActionStrength(InputActions.CameraZoomIn) - Input.GetActionStrength(InputActions.CameraZoomOut);
            if (held != 0f)
            {
                _targetLogZoom -= held * KeyZoomSpeed * dt;
            }
        }

        _targetLogZoom = Math.Clamp(_targetLogZoom, MathF.Log(minZoom), MathF.Log(maxZoom));
        _logZoom = CameraMath.Approach(_logZoom, _targetLogZoom, ZoomResponse, dt);
        if (MathF.Abs(_logZoom - _targetLogZoom) < SettleDistance)
        {
            _logZoom = _targetLogZoom;
        }
    }

    /// <summary>Locks the yaw offset to one angle, skipping the zoom blend. Debug experiments use it.</summary>
    /// <param name="fixedDegrees">The fixed offset in degrees.</param>
    public void OverrideYawOffset(float fixedDegrees)
    {
        YawOffsetDegrees = fixedDegrees;
        _yawFixed = fixedDegrees;
    }

    // The yaw offset the rotation spring aims at: the zoom blend from the street angle toward island view, or the
    // locked angle when a debug experiment set one. It follows the smoothed zoom, so smooth zooming never jitters.
    private float CurrentYawOffsetDegrees()
    {
        if (_yawFixed is { } locked)
        {
            return locked;
        }

        (float minZoom, float maxZoom) = ZoomRange();
        return CameraMath.BlendedYawOffsetDegrees(Zoom, minZoom, maxZoom, YawOffsetDegrees, YawOffsetDegrees + (CameraMath.IslandYawDegrees - CameraMath.StreetYawDegrees));
    }

    private void UpdateRotation(float dt)
    {
        float target = CameraMath.TargetYawRadians(_rotationSteps, CurrentYawOffsetDegrees());
        CameraMath.SpringStep(ref _yaw, ref _yawVelocity, target, RotationResponse, dt);

        if (MathF.Abs(_yaw - target) < SettleDistance && MathF.Abs(_yawVelocity) < SettleVelocity)
        {
            // At rest: fold whole turns away so the angle never grows without bound.
            _rotationSteps = RotationQuadrant;
            _yaw = CameraMath.SettledYawRadians(_rotationSteps, CurrentYawOffsetDegrees());
            _yawVelocity = 0f;
        }
    }

    private void UpdatePan(float dt)
    {
        if (_dragging && !Input.IsActionPressed(InputActions.CameraDrag))
        {
            // The release event never reached us (focus lost mid-drag, for example).
            _dragging = false;
        }

        Vector2 input = _dragging ? Vector2.Zero : ReadPanInput();
        float speed = PanSpeed * Zoom;
        (float worldX, float worldZ) = CameraMath.RotateByYaw(input.X, input.Y, _yaw);

        float moveX = CameraMath.PanStep(ref _velocityX, worldX * speed, PanResponse, dt);
        float moveZ = CameraMath.PanStep(ref _velocityZ, worldZ * speed, PanResponse, dt);
        if (moveX != 0f || moveZ != 0f)
        {
            MoveFocusTo(_focus.X + moveX, _focus.Y + moveZ);
        }
    }

    private void MoveFocusTo(float worldX, float worldZ)
    {
        (float x, float z) = Bounds.ClampFocus(worldX, worldZ, BoundsMargin);

        // A wall stops the glide: otherwise leftover velocity would push against it and delay the turn-around.
        if (x != worldX)
        {
            _velocityX = 0f;
        }

        if (z != worldZ)
        {
            _velocityZ = 0f;
        }

        _focus = new Vector2(x, z);
    }

    private void UpdateFocusHeight(float dt) =>
        _focusHeight = CameraMath.Approach(_focusHeight, FocusGroundHeight(), FocusHeightResponse, dt);

    private void ApplyTransform(float minZoom, float maxZoom)
    {
        if (_camera is null)
        {
            return;
        }

        float zoom = Zoom;
        float pitch = CameraMath.PitchRadians(zoom, minZoom, maxZoom, StreetPitchDegrees, IslandPitchDegrees);

        // The rig turns about the focus point; the camera hangs behind and above it, looking at the focus.
        Position = new Vector3(_focus.X, _focusHeight, _focus.Y);
        Rotation = new Vector3(0f, _yaw, 0f);
        _camera.Position = new Vector3(0f, MathF.Sin(pitch) * zoom, MathF.Cos(pitch) * zoom);
        _camera.Rotation = new Vector3(-pitch, 0f, 0f);
    }

    private void ZoomByNotches(float notches)
    {
        _targetLogZoom += MathF.Log(ZoomStep) * notches;
        ClampTargetZoom();
    }

    private void ClampTargetZoom()
    {
        (float minZoom, float maxZoom) = ZoomRange();
        _targetLogZoom = Math.Clamp(_targetLogZoom, MathF.Log(minZoom), MathF.Log(maxZoom));
    }

    /// <summary>
    /// Sets the projection from the window's height in logical points, so one tile covers the same number of points
    /// at any window size and a bigger window shows more island. Perspective sets the vertical field of view
    /// (<see cref="CameraMath.VerticalFovDegrees"/>); orthographic sets the span
    /// (<see cref="CameraMath.OrthographicSize"/>), which <see cref="RefreshOrthoSize"/> then follows every frame
    /// as the zoom eases. It runs on ready and on every resize; the 1600 x 900-point view keeps its 45-degree look.
    /// </summary>
    private void ApplyProjection()
    {
        if (_camera is null || !IsInsideTree())
        {
            return;
        }

        // Window.Size counts physical pixels (backing store); the formulas work in logical points.
        float screen = DisplayServer.ScreenGetScale((int)DisplayServer.ScreenOfMainWindow);
        float pointsH = UiScale.PointsFromPixels(GetWindow().Size.Y, screen);
        if (Projection == CameraProjection.Orthographic)
        {
            _camera.Projection = Camera3D.ProjectionType.Orthogonal;
            float size = CameraMath.OrthographicSize(Zoom, pointsH);
            if (MathF.Abs(_camera.Size - size) > 0.001f)
            {
                _camera.Size = size;
            }
        }
        else
        {
            _camera.Projection = Camera3D.ProjectionType.Perspective;
            float fov = CameraMath.VerticalFovDegrees(pointsH);
            if (MathF.Abs(_camera.Fov - fov) > 0.01f)
            {
                _camera.Fov = fov;
            }
        }
    }

    // The orthographic size follows the zoom every frame; the perspective field of view only follows resizes.
    private void RefreshOrthoSize()
    {
        if (_camera is null || Projection != CameraProjection.Orthographic)
        {
            return;
        }

        float size = CameraMath.OrthographicSize(Zoom, CurrentPointsHeight());
        if (MathF.Abs(_camera.Size - size) > 0.001f)
        {
            _camera.Size = size;
        }
    }

    // The cached window height in logical points, refreshed by CheckFovScale just before this runs each frame.
    private float CurrentPointsHeight()
    {
        float screen = _lastFovScreen > 0f ? _lastFovScreen : 1f;
        return UiScale.PointsFromPixels(_lastFovSize.Y, screen);
    }

    // The window's screen scale can change without a resize (the window moving to a display with another HiDPI
    // scale), so the field of view re-applies whenever the size or the scale changed: the same check HudRoot runs.
    private void CheckFovScale()
    {
        Vector2I size = GetWindow().Size;
        float screen = DisplayServer.ScreenGetScale((int)DisplayServer.ScreenOfMainWindow);
        if (size != _lastFovSize || screen != _lastFovScreen)
        {
            _lastFovSize = size;
            _lastFovScreen = screen;
            ApplyProjection();
        }
    }

    // Reads a debug-only --check-zoom=sx,sy argument (viewport pixels): the _Process sequence warps the pointer
    // there, injects one wheel-up and logs the hovered control with the zoom before and after.
    private void ParseCheckZoom()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith(CheckZoomPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = arg[CheckZoomPrefix.Length..].Split(',');
            if (parts.Length == 2
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy))
            {
                _checkZoomPoint = new Vector2(sx, sy);
                _checkZoomStage = 1;
                GD.Print($"[Evenmere] check-zoom: will inject one wheel-up at ({sx}, {sy}).");
                return;
            }

            GD.PushWarning($"[Evenmere] ignoring malformed --check-zoom '{arg}'; want 'sx,sy'.");
        }
    }

    // The debug-only --check-zoom sequence: warp the pointer to the point, let the hover settle, inject one
    // wheel-up through the real input path (so panels consume it exactly as a physical wheel would), then log the
    // zoom once the easing settled and quit.
    private void TickCheckZoom()
    {
        ulong frame = Engine.GetProcessFrames();
        if (_checkZoomStage == 1 && frame >= 60)
        {
            // Both: the warp moves the visible cursor, and the synthetic motion drives the viewport's hover state
            // (hover follows event positions, so the check never depends on where the OS cursor rested).
            Input.WarpMouse(_checkZoomPoint);
            var motion = new InputEventMouseMotion { Position = _checkZoomPoint };
            Input.ParseInputEvent(motion);
            _checkZoomFrame = frame;
            _checkZoomStage = 2;
        }
        else if (_checkZoomStage == 2 && frame >= _checkZoomFrame + 6)
        {
            Control? hovered = GetViewport().GuiGetHoveredControl();
            Vector2 mouse = GetViewport().GetMousePosition();
            _checkZoomBefore = Zoom;
            GD.Print($"[Evenmere] ZOOM point=({_checkZoomPoint.X:F0}, {_checkZoomPoint.Y:F0}) mouse=({mouse.X:F0}, {mouse.Y:F0}) hovered={(hovered is null ? "none" : hovered.Name + " (" + hovered.GetType().Name + ")")} zoomBefore={_checkZoomBefore:F3}");
            // Press and release go out together, the way a physical wheel detent arrives: the action never reads
            // as held, so the polled key-zoom adds nothing and the log shows exactly one discrete notch (or none).
            var press = new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = _checkZoomPoint };
            Input.ParseInputEvent(press);
            var release = new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = false, Position = _checkZoomPoint };
            Input.ParseInputEvent(release);
            _checkZoomFrame = frame;
            _checkZoomStage = 3;
        }
        else if (_checkZoomStage == 3 && frame >= _checkZoomFrame + 120)
        {
            GD.Print($"[Evenmere] ZOOM point=({_checkZoomPoint.X:F0}, {_checkZoomPoint.Y:F0}) zoomBefore={_checkZoomBefore:F3} zoomAfter={Zoom:F3}");
            _checkZoomStage = 0;
            GetTree().Quit(0);
        }
    }

    /// <summary>Moves the focus so the ground point under the pointer stays under it.</summary>
    private void DragBy(InputEventMouseMotion motion)
    {
        if (_camera is not { } camera || !camera.IsInsideTree() || !InputEnabled)
        {
            return;
        }

        Vector2 current = motion.Position;
        Vector2 previous = current - motion.Relative;
        if (!TryGroundPoint(camera, previous, out Vector3 from) || !TryGroundPoint(camera, current, out Vector3 to))
        {
            return;
        }

        // Both points are found with the same camera pose, so the difference does not depend on how many motion
        // events arrived since the rig last moved.
        _velocityX = 0f;
        _velocityZ = 0f;
        MoveFocusTo(_focus.X + (from.X - to.X), _focus.Y + (from.Z - to.Z));
    }

    private bool TryGroundPoint(Camera3D camera, Vector2 screenPoint, out Vector3 point)
    {
        Vector3 origin = camera.ProjectRayOrigin(screenPoint);
        Vector3 direction = camera.ProjectRayNormal(screenPoint);
        bool hit = CameraMath.TryIntersectPlane(
            new SysVector3(origin.X, origin.Y, origin.Z),
            new SysVector3(direction.X, direction.Y, direction.Z),
            _focusHeight,
            out SysVector3 found);
        point = new Vector3(found.X, found.Y, found.Z);
        return hit;
    }

    private void OnModeChanged(InputMode mode) => _cursor?.SetActive(mode == InputMode.Gamepad);

    private void OnMouseExited() => _pointerInside = false;

    private void OnFocusEntered() => _windowFocused = true;

    private void OnFocusExited()
    {
        _windowFocused = false;
        _dragging = false;
    }
}
