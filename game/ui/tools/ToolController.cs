using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Game.Controls;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Host;
using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Rendering.Buildings;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Rendering.Overlays;
using Evenmere.Game.Rendering.Roads;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Menus;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Godot;

namespace Evenmere.Game.Ui.Tools;

/// <summary>
/// The one node that owns the world-click input and the placement tool state: <see cref="ToolMode.Idle"/>,
/// <see cref="ToolMode.Build"/>, <see cref="ToolMode.Road"/> or <see cref="ToolMode.Demolish"/>.
/// </summary>
/// <remarks>
/// <para>
/// The view enqueues commands only and never mutates the simulation: a click on a valid ghost enqueues
/// <c>PlaceBuilding</c> (a click on a red ghost enqueues it too, so the sim's <c>CommandRejected</c> teaches the rule
/// through a toast), a road release enqueues <c>PlaceRoad</c>, and demolish clicks enqueue <c>Demolish</c> or
/// <c>RemoveRoad</c>.
/// </para>
/// <para>
/// The contract for the build menu (M1-23) and the inspector (M1-24): <c>BeginBuild</c>, <c>BeginRoad</c>,
/// <c>BeginDemolish</c> and <c>Cancel</c> switch the tool, <c>ToolChanged</c> fires on every switch, and
/// <c>BuildingClicked</c> fires with the clicked building when the player clicks one while idle (M1-24 opens the
/// inspector from it). Input goes through <c>_UnhandledInput</c> so UI panels get the click first, and
/// <c>tool_cancel</c> in idle is left unhandled so the pause menu (M1-27) can take it.
/// </para>
/// <para>
/// Picking uses the mouse position, or <c>VirtualCursor.ScreenPosition</c> while the gamepad cursor is active, so
/// gamepad players place and demolish with <c>cursor_confirm</c>. Placement is revalidated only when the tile, the
/// rotation or the type changes (never per frame), and the hover and preview paths allocate nothing per frame.
/// </para>
/// </remarks>
public sealed partial class ToolController : Node
{
    private const int MaxRoadPreviewTiles = 512;
    private const int OwnRingSize = 16;
    private const int MaxIssueIcons = 12;
    private const int ConfirmThreshold = 5;
    private const float OutlineLift = 0.08f;
    private const float OutlineWidth = 0.06f;
    private const double PulseSeconds = 0.35;

    private readonly PlaceholderMeshFactory _factory = new();
    private readonly RoadMeshBuilder _roadBuilder = new();
    private readonly TileCoord[] _roadPath = new TileCoord[MaxRoadPreviewTiles];
    private readonly bool[] _roadInvalid = new bool[MaxRoadPreviewTiles];
    private readonly Command[] _ownCommands = new Command[OwnRingSize];
    private readonly List<PanelContainer> _issueIcons = new();
    private readonly List<EntityId> _pendingBuildings = new();
    private readonly List<TileCoord> _pendingRoads = new();

    private SimHost? _host;
    private CameraRig? _rig;
    private VirtualCursor? _cursor;
    private OverlayRenderer? _overlays;
    private OverlaySpec? _overlayBeforeTool;
    private bool _overlayAutoShown;
    private UiStrings? _strings;
    private ContentDb? _stringsFor;

    private ToolMode _mode = ToolMode.Idle;
    private BuildingTypeId _buildType;
    private BuildingTypeId _roadType;
    private Rotation _buildRotation;
    private bool _roadElbow;

    private Node3D? _ghostRoot;
    private MeshInstance3D? _ghostBody;
    private MeshInstance3D? _ghostPlinth;
    private MeshInstance3D? _ghostOutline;
    private ArrayMesh? _ghostOutlineMesh;
    private StandardMaterial3D? _ghostOkMaterial;
    private StandardMaterial3D? _ghostWarnMaterial;
    private StandardMaterial3D? _ghostBadMaterial;
    private StandardMaterial3D? _outlineMaterial;
    private BuildingTypeId _ghostMeshType;
    private bool _hasGhostMesh;

    private MeshInstance3D? _roadPreview;
    private StandardMaterial3D? _roadMaterial;
    private double _pulseLeft;

    private MeshInstance3D? _demoFill;
    private MeshInstance3D? _demoOutline;
    private ImmediateMesh? _demoOutlineMesh;
    private BoxMesh? _demoBox;
    private StandardMaterial3D? _demoOkMaterial;
    private StandardMaterial3D? _demoBadMaterial;

    private CanvasLayer? _layer;
    private PanelContainer? _reasonPanel;
    private Label? _reasonLabel;
    private HBoxContainer? _issueBar;
    private ToolToast? _toast;
    private ConfirmationDialog? _demolishConfirm;

    private bool _hasHover;
    private int _hoverX;
    private int _hoverY;

    private BuildingTypeId _validType;
    private int _validX = int.MinValue;
    private int _validY = int.MinValue;
    private Rotation _validRot;
    private PlacementResult _validResult = PlacementResult.Ok;
    private int _validateCount;
    private float _padHighest;
    private float _padLowest;
    private bool _hasPad;

    private bool _roadDragging;
    private TileCoord _roadAnchor;
    private TileCoord _roadValidAnchor = new(int.MinValue, int.MinValue);
    private TileCoord _roadValidCurrent;
    private bool _roadValidElbow;
    private PlacementResult _roadResult = PlacementResult.Ok;
    private bool _hasRoadCache;

    private bool _demoDragging;
    private TileCoord _demoAnchor;
    private int _demoValidX = int.MinValue;
    private int _demoValidY = int.MinValue;
    private EntityId _demoBuilding = EntityId.None;
    private bool _demoHasRoad;
    private bool _demoProtected;
    private string _demoName = string.Empty;
    private TileCoord _rectValidMin = new(int.MinValue, int.MinValue);
    private TileCoord _rectValidMax;
    private int _rectBuildings;
    private int _rectRoads;

    private int _ownNext;

    private bool _hasPin;
    private TileCoord _pin;
    private bool _debugRoadDrag;
    private TileCoord _debugAnchor;
    private TileCoord _debugCurrent;

    /// <summary>Raised whenever the tool changes, including back to <see cref="ToolMode.Idle"/>.</summary>
    public event Action? ToolChanged;

    /// <summary>Raised with the clicked building when the player clicks one while idle. M1-24 opens the inspector from it.</summary>
    public event Action<EntityId>? BuildingClicked;

    /// <summary>Gets the active tool.</summary>
    public ToolMode Mode => _mode;

    /// <summary>Gets the building type being placed. It is meaningful only while <see cref="Mode"/> is <see cref="ToolMode.Build"/>.</summary>
    public BuildingTypeId BuildType => _buildType;

    /// <summary>Gets the road type being laid. It is meaningful only while <see cref="Mode"/> is <see cref="ToolMode.Road"/>.</summary>
    public BuildingTypeId RoadType => _roadType;

    /// <summary>Gets the ghost's orientation. A quarter or three-quarter turn swaps the footprint's width and height.</summary>
    public Rotation BuildRotation => _buildRotation;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the <see cref="CameraRig"/> node, relative to this node.</summary>
    [Export]
    public NodePath RigPath { get; set; } = new("../CameraRig");

    /// <summary>Gets or sets the path to the overlay renderer, relative to this node.</summary>
    [Export]
    public NodePath OverlayPath { get; set; } = new("../Overlays");

    /// <summary>Gets or sets the command-log recorder the safety autosaves rebind. It is set by the scene root.</summary>
    public CommandLogRecorder? Recorder { get; set; }

    /// <summary>
    /// Starts the build tool with a building type, for the build menu (M1-23). The ghost follows the pointer and a
    /// click enqueues <c>PlaceBuilding</c>; Shift-click keeps the tool active, otherwise it returns to idle.
    /// </summary>
    /// <param name="type">The building type to place.</param>
    public void BeginBuild(BuildingTypeId type)
    {
        _mode = ToolMode.Build;
        _buildType = type;
        _buildRotation = Rotation.Deg0;
        _roadDragging = false;
        _demoDragging = false;
        HideRoadPreview();
        HideDemolish();
        ShowPlacementOverlay(type);
        LogTool($"build {NameOf(type)}");
        ToolChanged?.Invoke();
    }

    /// <summary>
    /// Starts the road tool with a road type, for the build menu (M1-23). Press and drag previews an L-shaped path
    /// and releasing enqueues <c>PlaceRoad</c>; a click without a drag places one tile.
    /// </summary>
    /// <param name="roadType">The road type to lay.</param>
    public void BeginRoad(BuildingTypeId roadType)
    {
        _mode = ToolMode.Road;
        _roadType = roadType;
        _roadElbow = false;
        _roadDragging = false;
        _demoDragging = false;
        HideGhost();
        HideDemolish();
        LogTool($"road {NameOf(roadType)}");
        ToolChanged?.Invoke();
    }

    /// <summary>
    /// Starts the demolish tool, for the build menu (M1-23) and the inspector (M1-24). Hovering highlights the target,
    /// a click removes one building or road tile, and a drag selects several (asking above five targets).
    /// </summary>
    public void BeginDemolish()
    {
        _mode = ToolMode.Demolish;
        _roadDragging = false;
        _demoDragging = false;
        HideGhost();
        HideRoadPreview();
        LogTool("demolish");
        ToolChanged?.Invoke();
    }

    /// <summary>Cancels the active tool and returns to idle, hiding every preview.</summary>
    public void Cancel()
    {
        if (_mode == ToolMode.Idle)
        {
            return;
        }

        _mode = ToolMode.Idle;
        _roadDragging = false;
        _demoDragging = false;
        HideGhost();
        HideRoadPreview();
        HideDemolish();
        RestorePlacementOverlay();
        LogTool("idle");
        ToolChanged?.Invoke();
    }

    /// <summary>
    /// Pins the hover to one tile for scripted screenshots (debug only): the ghost and previews follow the pin instead
    /// of the pointer until <c>DebugClearPin</c>.
    /// </summary>
    /// <param name="tile">The tile to hover.</param>
    public void DebugPinHover(TileCoord tile)
    {
        _hasPin = true;
        _pin = tile;
    }

    /// <summary>Clears the debug hover pin, so the pointer drives the tools again.</summary>
    public void DebugClearPin() => _hasPin = false;

    /// <summary>
    /// Shows an in-progress road drag for scripted screenshots (debug only): the preview runs from the anchor to the
    /// current tile as if the player were holding the press.
    /// </summary>
    /// <param name="anchor">The tile the press started on.</param>
    /// <param name="current">The tile the pointer is on now.</param>
    public void DebugSetRoadDrag(TileCoord anchor, TileCoord current)
    {
        _debugRoadDrag = true;
        _debugAnchor = anchor;
        _debugCurrent = current;
    }

    /// <summary>
    /// Enqueues an out-of-bounds placement for the current (or a fallback) build type and watches for its rejection
    /// (debug only): it proves the rejected-command toast without needing a red ghost click.
    /// </summary>
    public void DebugDemoReject()
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        BuildingTypeId type = _mode == ToolMode.Build ? _buildType : DefaultBuildType();
        EnqueueOwn(new PlaceBuilding(type, -10, -10, Rotation.Deg0));
        GD.Print("[Evenmere] Tools: demo reject enqueued (out of bounds)");
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _host = GetNodeOrNull<SimHost>(HostPath);
        _rig = GetNodeOrNull<CameraRig>(RigPath);
        _cursor = _rig?.GetNodeOrNull<VirtualCursor>("VirtualCursor");
        _overlays = GetNodeOrNull<OverlayRenderer>(OverlayPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] tools: no SimHost at '{HostPath}'; the tools stay idle.");
        }

        if (_rig is null)
        {
            GD.PushError($"[Evenmere] tools: no CameraRig at '{RigPath}'; the tools stay idle.");
        }

        _factory.LoadMapping();
        BuildMaterials();
        BuildGhostNodes();
        BuildRoadPreview();
        BuildDemolishNodes();
        BuildUi();

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

        _factory.Dispose();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        RefreshStrings();
        UpdateHoverTile();
        _pulseLeft = Math.Max(0.0, _pulseLeft - delta);

        switch (_mode)
        {
            case ToolMode.Build:
                UpdateBuild();
                break;
            case ToolMode.Road:
                UpdateRoad();
                break;
            case ToolMode.Demolish:
                UpdateDemolish();
                break;
            default:
                HideGhost();
                HideRoadPreview();
                HideDemolish();
                break;
        }

        PositionLabels();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_host is not { IsRunning: true } || _rig is null)
        {
            return;
        }

        if (InputMap.HasAction(InputActions.ToolCancel) && @event.IsActionPressed(InputActions.ToolCancel))
        {
            // In idle the event stays unhandled, so the pause menu (M1-27) can take it.
            if (_mode != ToolMode.Idle)
            {
                Cancel();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (InputMap.HasAction(InputActions.ToolRoad) && @event.IsActionPressed(InputActions.ToolRoad))
        {
            if (TryDefaultRoadType(out BuildingTypeId road))
            {
                BeginRoad(road);
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (InputMap.HasAction(InputActions.ToolDemolish) && @event.IsActionPressed(InputActions.ToolDemolish))
        {
            BeginDemolish();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (InputMap.HasAction(InputActions.ToolRotate) && @event.IsActionPressed(InputActions.ToolRotate))
        {
            if (_mode == ToolMode.Build)
            {
                _buildRotation = _buildRotation.Next();
                GetViewport().SetInputAsHandled();
            }
            else if (_mode == ToolMode.Road)
            {
                _roadElbow = !_roadElbow;
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (InputMap.HasAction(InputActions.CursorConfirm) && @event.IsActionPressed(InputActions.CursorConfirm))
        {
            PressAtPointer();
            return;
        }

        if (InputMap.HasAction(InputActions.CursorConfirm) && @event.IsActionReleased(InputActions.CursorConfirm))
        {
            ReleaseAtPointer();
        }
    }

    private void PressAtPointer()
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        switch (_mode)
        {
            case ToolMode.Idle:
                if (PickNow(out int ix, out int iy))
                {
                    EntityId at = _host.Query.Map.BuildingAt(ix, iy);
                    if (!at.IsNone)
                    {
                        BuildingClicked?.Invoke(at);
                        GetViewport().SetInputAsHandled();
                    }
                }

                break;
            case ToolMode.Build:
                if (PickNow(out int bx, out int by))
                {
                    // A red ghost never enqueues: the sim would only refuse it, and the command log (M1-27)
                    // records every enqueue. The reason label shakes instead; the reject toast stays for races.
                    FootprintOf(_buildType, _buildRotation, out int bw, out int bh);
                    ValidateBuildNow(bx, by, bw, bh);
                    if (!_validResult.CanPlace)
                    {
                        _pulseLeft = PulseSeconds;
                        GD.Print($"[Evenmere] Tools: placement refused at ({bx}, {by}); reason shown, nothing enqueued.");
                    }
                    else
                    {
                        EnqueueOwn(new PlaceBuilding(_buildType, bx, by, _buildRotation));
                        GD.Print($"[Evenmere] Tools: enqueued PlaceBuilding {NameOf(_buildType)} at ({bx}, {by}) rot={(byte)_buildRotation}");
                        if (!Input.IsKeyPressed(Key.Shift))
                        {
                            Cancel();
                        }
                    }

                    GetViewport().SetInputAsHandled();
                }

                break;
            case ToolMode.Road:
                if (PickNow(out int rx, out int ry))
                {
                    _roadAnchor = new TileCoord(rx, ry);
                    _roadDragging = true;
                    _roadElbow = false;
                    GetViewport().SetInputAsHandled();
                }

                break;
            case ToolMode.Demolish:
                if (PickNow(out int dx, out int dy))
                {
                    _demoAnchor = new TileCoord(dx, dy);
                    _demoDragging = true;
                    GetViewport().SetInputAsHandled();
                }

                break;
        }
    }

    private void ReleaseAtPointer()
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        switch (_mode)
        {
            case ToolMode.Road when _roadDragging:
                _roadDragging = false;
                if (!PickNow(out int rx, out int ry))
                {
                    break;
                }

                int count = ToolPaths.BuildLPath(_roadAnchor, new TileCoord(rx, ry), _roadElbow, _roadPath.AsSpan());
                PlacementResult result = _host.Query.Placement.ValidateRoad(_roadPath.AsSpan(0, count), _roadType);
                if (result.CanPlace)
                {
                    var path = new TileCoord[count];
                    _roadPath.AsSpan(0, count).CopyTo(path.AsSpan());
                    EnqueueOwn(new PlaceRoad(path, _roadType));
                    GD.Print($"[Evenmere] Tools: enqueued PlaceRoad {count} tile(s)");
                }
                else
                {
                    _pulseLeft = PulseSeconds;
                    ShowBlockedToast(result);
                }

                GetViewport().SetInputAsHandled();
                break;
            case ToolMode.Demolish when _demoDragging:
                _demoDragging = false;
                if (!PickNow(out int dx, out int dy))
                {
                    break;
                }

                ReleaseDemolish(new TileCoord(dx, dy));
                GetViewport().SetInputAsHandled();
                break;
            default:
                break;
        }
    }

    private void ReleaseDemolish(TileCoord current)
    {
        if (_demoAnchor.Equals(current))
        {
            DemolishSingle(current);
            return;
        }

        _pendingBuildings.Clear();
        _pendingRoads.Clear();
        CollectRectInto(_demoAnchor, current, _pendingBuildings, _pendingRoads);
        int total = _pendingBuildings.Count + _pendingRoads.Count;
        if (total == 0)
        {
            return;
        }

        if (total <= ConfirmThreshold)
        {
            EnqueuePendingNow();
            return;
        }

        ShowDemolishConfirm(_pendingBuildings.Count, _pendingRoads.Count);
    }

    private void DemolishSingle(TileCoord tile)
    {
        if (_host is null)
        {
            return;
        }

        EntityId id = _host.Query.Map.BuildingAt(tile.X, tile.Y);
        if (!id.IsNone)
        {
            if (IsProtectedHall(id))
            {
                ShowText(_strings?.Get("ui.tools.demolish.protected") ?? "founders_hall");
                return;
            }

            string name = _host.Query.Map.TryGetBuilding(id, out BuildingInfo info)
                ? _host.Content.Buildings.NameOf(info.Type)
                : id.ToString();
            EnqueueOwn(new Demolish(id));
            GD.Print($"[Evenmere] Tools: enqueued Demolish {id.Value} ({name})");
            return;
        }

        if (_host.Query.Roads.RoadAt(tile.X, tile.Y) is not null)
        {
            EnqueueOwn(new RemoveRoad([tile]));
            GD.Print($"[Evenmere] Tools: enqueued RemoveRoad ({tile.X}, {tile.Y})");
        }
    }

    private void UpdateHoverTile()
    {
        if (_hasPin)
        {
            _hasHover = true;
            _hoverX = _pin.X;
            _hoverY = _pin.Y;
            return;
        }

        if (_rig is not null && _rig.TryPickPointerTile(out Vector2I tile))
        {
            _hasHover = true;
            _hoverX = tile.X;
            _hoverY = tile.Y;
        }
        else
        {
            _hasHover = false;
        }
    }

    private bool PickNow(out int x, out int y)
    {
        if (_hasHover)
        {
            x = _hoverX;
            y = _hoverY;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private void UpdateBuild()
    {
        HideRoadPreview();
        HideDemolish();
        if (!_hasHover || _host is null)
        {
            HideGhost();
            return;
        }

        ShowGhost();
        if (!_hasGhostMesh || !_ghostMeshType.Equals(_buildType))
        {
            SetGhostMesh(_buildType);
        }

        FootprintOf(_buildType, _buildRotation, out int width, out int height);
        if (!_hasPad || _validX != _hoverX || _validY != _hoverY || !_validRot.Equals(_buildRotation) || !_validType.Equals(_buildType))
        {
            GroundSampler.FootprintHeights(_host.Query.Map, _host.Content, _hoverX, _hoverY, width, height, out _padHighest, out _padLowest);
            _hasPad = true;
        }

        PositionGhost(_hoverX, _hoverY, width, height);
        ValidateBuildNow(_hoverX, _hoverY, width, height);
    }

    // Runs the placement query when the tile, rotation or type changed since the last check; otherwise the cached
    // result stands. Both the hover frame and the click path go through here, so a click never acts on a stale result.
    private void ValidateBuildNow(int x, int y, int width, int height)
    {
        if (_host is null)
        {
            return;
        }

        if (_validType.Equals(_buildType) && _validX == x && _validY == y && _validRot.Equals(_buildRotation))
        {
            return;
        }

        _validType = _buildType;
        _validX = x;
        _validY = y;
        _validRot = _buildRotation;
        _validResult = _host.Query.Placement.Validate(_buildType, x, y, _buildRotation);
        _validateCount++;
        GD.Print($"[Evenmere] Tools: Validate {_validType.Value} ({NameOf(_buildType)}) at ({x}, {y}) rot={(byte)_buildRotation} -> errors={_validResult.Errors.Length} warnings={_validResult.Warnings.Length} (#{_validateCount})");
        ApplyBuildVisuals(x, y, width, height);
    }

    private void ApplyBuildVisuals(int x, int y, int width, int height)
    {
        bool errors = !_validResult.CanPlace;
        bool warnings = !errors && _validResult.Warnings.Length > 0;
        StandardMaterial3D? body = errors ? _ghostBadMaterial : warnings ? _ghostWarnMaterial : _ghostOkMaterial;
        if (_ghostBody is not null)
        {
            _ghostBody.MaterialOverride = body;
        }

        if (_ghostPlinth is not null)
        {
            _ghostPlinth.MaterialOverride = body;
        }

        RebuildGhostOutline(x, y, width, height, errors ? ToolColours.OutlineError : warnings ? ToolColours.OutlineWarn : ToolColours.OutlineOk);
        if (_reasonPanel is not null && _reasonLabel is not null && _strings is not null)
        {
            if (errors && _validResult.Errors.Length > 0)
            {
                _reasonLabel.Text = _strings.Format("ui.tools.reason.blocked", ("issue", _strings.Get(IssueKey(_validResult.Errors[0].Issue))));
            }
            else if (_validResult.Warnings.Length > 0)
            {
                _reasonLabel.Text = _strings.Format("ui.tools.reason.warning", ("issue", _strings.Get(IssueKey(_validResult.Warnings[0].Issue))));
            }
            else
            {
                _reasonLabel.Text = _strings.Get("ui.tools.reason.ok");
            }

            _reasonPanel.Visible = true;
        }

        int icon = 0;
        foreach (PlacementFinding finding in _validResult.Errors)
        {
            if (icon >= MaxIssueIcons)
            {
                break;
            }

            SetIssueIcon(icon++, false, finding.Issue);
        }

        foreach (PlacementFinding finding in _validResult.Warnings)
        {
            if (icon >= MaxIssueIcons)
            {
                break;
            }

            SetIssueIcon(icon++, true, finding.Issue);
        }

        for (int i = icon; i < _issueIcons.Count; i++)
        {
            _issueIcons[i].Visible = false;
        }
    }

    private void UpdateRoad()
    {
        HideGhost();
        HideDemolish();
        if (_host is null)
        {
            HideRoadPreview();
            return;
        }

        bool dragging = _roadDragging || _debugRoadDrag;
        TileCoord anchor;
        TileCoord current;
        if (_debugRoadDrag)
        {
            anchor = _debugAnchor;
            current = _debugCurrent;
        }
        else if (dragging)
        {
            anchor = _roadAnchor;
            if (!_hasHover)
            {
                HideRoadPreview();
                return;
            }

            current = new TileCoord(_hoverX, _hoverY);
        }
        else
        {
            if (!_hasHover)
            {
                HideRoadPreview();
                return;
            }

            anchor = new TileCoord(_hoverX, _hoverY);
            current = anchor;
        }

        int needed = Math.Abs(current.X - anchor.X) + Math.Abs(current.Y - anchor.Y) + 1;
        if (needed > MaxRoadPreviewTiles)
        {
            GD.Print($"[Evenmere] Tools: road drag too long ({needed} tiles); preview capped at {MaxRoadPreviewTiles}.");
            current = ClampPath(anchor, current, MaxRoadPreviewTiles);
        }

        int count = ToolPaths.BuildLPath(anchor, current, _roadElbow, _roadPath.AsSpan());
        if (!_hasRoadCache || !_roadValidAnchor.Equals(anchor) || !_roadValidCurrent.Equals(current) || _roadValidElbow != _roadElbow)
        {
            _roadValidAnchor = anchor;
            _roadValidCurrent = current;
            _roadValidElbow = _roadElbow;
            _hasRoadCache = true;
            _roadResult = _host.Query.Placement.ValidateRoad(_roadPath.AsSpan(0, count), _roadType);
            _validateCount++;
            GD.Print($"[Evenmere] Tools: ValidateRoad {count} tile(s) from ({anchor.X}, {anchor.Y}) to ({current.X}, {current.Y}) -> errors={_roadResult.Errors.Length} warnings={_roadResult.Warnings.Length} (#{_validateCount})");
            MarkRoadInvalid(count);
            RebuildRoadPreview(count);
            UpdateRoadLabel();
        }

        ShowRoadPreview();
    }

    private void UpdateDemolish()
    {
        HideGhost();
        HideRoadPreview();
        if (_host is null)
        {
            HideDemolish();
            return;
        }

        if (_demoDragging)
        {
            if (!_hasHover)
            {
                return;
            }

            TileCoord current = new(_hoverX, _hoverY);
            ToolPaths.NormalizeRect(_demoAnchor, current, out TileCoord min, out TileCoord max);
            ShowDemolishNodes();
            if (!_rectValidMin.Equals(min) || !_rectValidMax.Equals(max))
            {
                _rectValidMin = min;
                _rectValidMax = max;
                _pendingBuildings.Clear();
                _pendingRoads.Clear();
                CollectRectInto(_demoAnchor, current, _pendingBuildings, _pendingRoads);
                _rectBuildings = _pendingBuildings.Count;
                _rectRoads = _pendingRoads.Count;
                RebuildDemolishRect(min, max);
            }

            if (_reasonPanel is not null && _reasonLabel is not null && _strings is not null)
            {
                _reasonLabel.Text = _strings.Format("ui.tools.demolish.count", ("count", (_rectBuildings + _rectRoads).ToString()));
                _reasonPanel.Visible = true;
            }

            HideIssueIcons();
            return;
        }

        if (!_hasHover)
        {
            HideDemolish();
            return;
        }

        ShowDemolishNodes();
        if (_demoValidX != _hoverX || _demoValidY != _hoverY)
        {
            _demoValidX = _hoverX;
            _demoValidY = _hoverY;
            RefreshDemolishTarget(_hoverX, _hoverY);
        }
    }

    private void RefreshDemolishTarget(int x, int y)
    {
        if (_host is null)
        {
            return;
        }

        _demoBuilding = _host.Query.Map.BuildingAt(x, y);
        _demoHasRoad = !_demoBuilding.IsNone ? false : _host.Query.Roads.RoadAt(x, y) is not null;
        _demoProtected = !_demoBuilding.IsNone && IsProtectedHall(_demoBuilding);
        if (!_demoBuilding.IsNone && _host.Query.Map.TryGetBuilding(_demoBuilding, out BuildingInfo info))
        {
            _demoName = _host.Content.Buildings.NameOf(info.Type);
            FootprintOf(info.Type, info.Rotation, out int width, out int height);
            GroundSampler.FootprintHeights(_host.Query.Map, _host.Content, info.Origin.X, info.Origin.Y, width, height, out _padHighest, out _padLowest);
            PositionDemolishBox(info.Origin.X, info.Origin.Y, width, height, _demoProtected);
            _demoName = DisplayName(_demoName);
        }
        else if (_demoHasRoad)
        {
            _demoName = _host.Query.Roads.RoadAt(x, y) is { } road ? DisplayName(_host.Content.Buildings.NameOf(road)) : string.Empty;
            GroundSampler.FootprintHeights(_host.Query.Map, _host.Content, x, y, 1, 1, out _padHighest, out _padLowest);
            PositionDemolishBox(x, y, 1, 1, false);
        }
        else
        {
            _demoName = string.Empty;
            HideDemolishNodes();
        }

        if (_reasonPanel is not null && _reasonLabel is not null)
        {
            if (_demoProtected)
            {
                _reasonLabel.Text = _strings?.Get("ui.tools.demolish.protected") ?? _demoName;
            }
            else if (!string.IsNullOrEmpty(_demoName))
            {
                _reasonLabel.Text = _demoName;
            }

            _reasonPanel.Visible = !string.IsNullOrEmpty(_demoName) || _demoProtected;
        }

        HideIssueIcons();
    }

    private void CollectRectInto(TileCoord first, TileCoord second, List<EntityId> buildings, List<TileCoord> roads)
    {
        if (_host is null)
        {
            return;
        }

        ToolPaths.NormalizeRect(first, second, out TileCoord min, out TileCoord max);
        foreach (BuildingInfo building in _host.Query.Map.Buildings)
        {
            if (ToolPaths.FootprintOverlaps(building.Origin, building.Width, building.Height, min, max) && !IsProtectedHall(building.Id))
            {
                buildings.Add(building.Id);
            }
        }

        for (int y = min.Y; y <= max.Y; y++)
        {
            for (int x = min.X; x <= max.X; x++)
            {
                if (x < 0 || y < 0 || x >= _host.Query.Map.Width || y >= _host.Query.Map.Height)
                {
                    continue;
                }

                if (_host.Query.Roads.RoadAt(x, y) is not null)
                {
                    roads.Add(new TileCoord(x, y));
                }
            }
        }
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        SimHost? host = _host;
        if (host is null || !host.IsRunning)
        {
            return;
        }

        foreach (SimEvent @event in events)
        {
            if (@event is CommandRejected rejected && IsOwn(rejected.Command))
            {
                ShowRejectedToast(rejected);
            }
        }
    }

    private bool IsOwn(Command command)
    {
        foreach (Command own in _ownCommands)
        {
            if (ReferenceEquals(own, command))
            {
                return true;
            }
        }

        return false;
    }

    private void EnqueueOwn(Command command)
    {
        if (_host is null)
        {
            return;
        }

        _host.Enqueue(command);
        _ownCommands[_ownNext] = command;
        _ownNext = (_ownNext + 1) % OwnRingSize;
    }

    private void ShowRejectedToast(CommandRejected rejected)
    {
        string reason = _strings?.Get(RejectKey(rejected.Code)) ?? rejected.Code.ToString();
        ShowText(_strings?.Format("ui.tools.toast.rejected", ("reason", reason)) ?? reason);
    }

    private void ShowBlockedToast(PlacementResult result)
    {
        if (_strings is null || result.CanPlace)
        {
            return;
        }

        string issue = result.Errors.Length > 0 ? _strings.Get(IssueKey(result.Errors[0].Issue)) : _strings.Get("ui.tools.reject.placement_invalid");
        ShowText(_strings.Format("ui.tools.toast.rejected", ("reason", issue)));
    }

    private void ShowText(string text)
    {
        if (_toast is not null)
        {
            _toast.Show(text);
        }
    }

    private void ShowDemolishConfirm(int buildings, int roads)
    {
        if (_demolishConfirm is null || _strings is null)
        {
            EnqueuePendingNow();
            return;
        }

        _demolishConfirm.Title = _strings.Format("ui.tools.demolish.confirm_title", ("count", (buildings + roads).ToString()));
        _demolishConfirm.DialogText = _strings.Format("ui.tools.demolish.confirm_text", ("buildings", buildings.ToString()), ("roads", roads.ToString()));
        _demolishConfirm.GetOkButton().Text = _strings.Get("ui.tools.demolish.confirm_ok");
        _demolishConfirm.GetCancelButton().Text = _strings.Get("ui.tools.demolish.cancel");
        _demolishConfirm.PopupCentered();
    }

    private void EnqueuePendingNow()
    {
        TakePreDemolishAutosave();

        foreach (EntityId id in _pendingBuildings)
        {
            EnqueueOwn(new Demolish(id));
        }

        if (_pendingRoads.Count > 0)
        {
            EnqueueOwn(new RemoveRoad(_pendingRoads.ToArray()));
        }

        GD.Print($"[Evenmere] Tools: enqueued confirmed demolish ({_pendingBuildings.Count} building(s), {_pendingRoads.Count} road tile(s))");
        _pendingBuildings.Clear();
        _pendingRoads.Clear();
    }

    // A confirmed bulk demolish takes a safety autosave first, so a misclick never loses more than a few minutes.
    // The save joins the normal autosave rotation; like the periodic autosave it needs the strings and the recorder.
    private void TakePreDemolishAutosave()
    {
        if (_host is not { IsRunning: true } || Recorder is null || _strings is null)
        {
            return;
        }

        string name = _strings.Get("ui.slot.autosave") + " — " + MenuText.SuggestName(_host, _strings);
        string? hash = SaveService.AutosaveBeforeBulkDemolish(_host, Recorder, _pendingBuildings.Count, name);
        if (hash is not null)
        {
            GD.Print($"[Evenmere] Tools: pre-demolish autosave {hash} before {_pendingBuildings.Count} building(s)");
        }
    }

    private void MarkRoadInvalid(int count)
    {
        Array.Clear(_roadInvalid, 0, _roadInvalid.Length);
        foreach (PlacementFinding finding in _roadResult.Errors)
        {
            foreach (TileCoord bad in finding.Tiles)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_roadPath[i].Equals(bad))
                    {
                        _roadInvalid[i] = true;
                    }
                }
            }
        }
    }

    private void RebuildRoadPreview(int count)
    {
        if (_host is null || _roadPreview is null)
        {
            return;
        }

        var byChunk = new HashSet<ChunkCoord>();
        for (int i = 0; i < count; i++)
        {
            byChunk.Add(ChunkCoord.Of(_roadPath[i]));
        }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        var indices = new List<int>();
        foreach (ChunkCoord chunk in byChunk)
        {
            AppendRoadChunk(chunk, count, vertices, normals, colours, indices);
        }
        if (vertices.Count == 0)
        {
            _roadPreview.Mesh = null;
            return;
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, _roadMaterial);
        _roadPreview.Mesh = mesh;
    }

    private void AppendRoadChunk(
        ChunkCoord chunk,
        int count,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Color> colours,
        List<int> indices)
    {
        if (_host is null)
        {
            return;
        }

        const int size = ChunkCoord.TilesPerSide;
        const int stride = size + 2;
        int firstX = chunk.X * size;
        int firstY = chunk.Y * size;

        // Every preview tile in the window counts, including the one-tile halo over the chunk border: the
        // auto-connection arms cross chunk borders, and a halo of zeros used to cut them off mid-path.
        byte[] window = new byte[stride * stride];
        for (int i = 0; i < count; i++)
        {
            TileCoord tile = _roadPath[i];
            int wx = tile.X - firstX + 1;
            int wy = tile.Y - firstY + 1;
            if (wx < 0 || wy < 0 || wx > size + 1 || wy > size + 1)
            {
                continue;
            }

            byte slot = (byte)(_roadInvalid[i] ? 2 : 1);
            int at = (wy * stride) + wx;
            window[at] = window[at] == 2 ? (byte)2 : slot;
        }

        float[] corners = new float[(size + 1) * (size + 1)];
        for (int vy = 0; vy <= size; vy++)
        {
            for (int vx = 0; vx <= size; vx++)
            {
                corners[(vy * (size + 1)) + vx] = GroundSampler.CornerHeight(_host.Query.Map, _host.Content, firstX + vx, firstY + vy) + RoadMeshBuilder.Lift;
            }
        }

        int baseVertex = vertices.Count;
        _roadBuilder.Rebuild(window, corners, size, stride, firstX, firstY);
        ReadOnlySpan<float> positions = _roadBuilder.Vertices;
        ReadOnlySpan<float> normalData = _roadBuilder.Normals;
        ReadOnlySpan<byte> slots = _roadBuilder.Slots;
        for (int i = 0; i < _roadBuilder.VertexCount; i++)
        {
            vertices.Add(new Vector3(positions[(i * 3) + 0], positions[(i * 3) + 1], positions[(i * 3) + 2]));
            normals.Add(new Vector3(normalData[(i * 3) + 0], normalData[(i * 3) + 1], normalData[(i * 3) + 2]));
            colours.Add(slots[i] == 2 ? ToolColours.RoadError : ToolColours.RoadOk);
        }

        ReadOnlySpan<int> indexData = _roadBuilder.Indices;
        for (int i = 0; i < _roadBuilder.IndexCount; i++)
        {
            indices.Add(baseVertex + indexData[i]);
        }
    }

    private void UpdateRoadLabel()
    {
        if (_reasonPanel is null || _reasonLabel is null || _strings is null)
        {
            return;
        }

        if (_roadResult.CanPlace)
        {
            _reasonPanel.Visible = false;
            return;
        }

        string issue = _roadResult.Errors.Length > 0 ? _strings.Get(IssueKey(_roadResult.Errors[0].Issue)) : _strings.Get("ui.tools.reject.placement_invalid");
        _reasonLabel.Text = _strings.Format("ui.tools.reason.blocked", ("issue", issue));
        _reasonPanel.Visible = true;
    }

    private void PositionLabels()
    {
        if (_reasonPanel is null || _issueBar is null)
        {
            return;
        }

        if (_mode == ToolMode.Idle || !_hasHover)
        {
            _reasonPanel.Visible = false;
            _issueBar.Visible = false;
            return;
        }

        Vector2 pointer = PointerScreenPosition();
        if (_mode == ToolMode.Build)
        {
            // The ghost is what the text explains, and the mouse can be anywhere (pinned tiles, gamepad cursor),
            // so the reason and the icons anchor to the ghost itself, projected to the screen.
            Vector2 ghosted = GhostScreenPosition();
            if (ghosted.X >= 0f)
            {
                pointer = ghosted;
            }
        }

        // A refused click shakes the reason panel briefly instead of enqueueing; no sound, per the calm rules.
        float shake = _pulseLeft > 0.0 ? MathF.Sin((float)(_pulseLeft / PulseSeconds * Math.Tau * 2.0)) * 6f * (float)(_pulseLeft / PulseSeconds) : 0f;

        Vector2 view = GetViewport().GetVisibleRect().Size;
        if (_reasonPanel.Visible)
        {
            _reasonPanel.Position = new Vector2(
                Math.Min(pointer.X + 18f, Math.Max(0f, view.X - 340f)) + shake,
                Math.Min(pointer.Y + 26f, Math.Max(0f, view.Y - 48f)));
        }

        bool showBar = _mode == ToolMode.Build && AnyIssueIconVisible();
        _issueBar.Visible = showBar;
        if (showBar)
        {
            _issueBar.Position = new Vector2(
                Math.Min(pointer.X + 18f, Math.Max(0f, view.X - 200f)),
                Math.Max(0f, pointer.Y - 10f));
        }
    }

    private Vector2 PointerScreenPosition()
    {
        if (_rig is not null && _rig.Mode == InputMode.Gamepad && _cursor is { IsActive: true })
        {
            return _cursor.ScreenPosition;
        }

        return GetViewport().GetMousePosition();
    }

    // Projects the ghost's top to the screen for the labels, or (-1, -1) when the camera cannot show it.
    private Vector2 GhostScreenPosition()
    {
        if (_ghostRoot is not { Visible: true } root || _rig?.Camera is not { } camera || !camera.IsInsideTree())
        {
            return new Vector2(-1f, -1f);
        }

        Vector3 top = root.Position + new Vector3(0f, 2f, 0f);
        return camera.IsPositionBehind(top) ? new Vector2(-1f, -1f) : camera.UnprojectPosition(top);
    }

    private void SetIssueIcon(int index, bool warning, PlacementIssue issue)
    {
        while (_issueIcons.Count <= index)
        {
            // Each icon gets its own small backing chip from the shared theme, so the glyph reads at 1280x800
            // on any terrain; the tooltip carries the locale reason.
            var chip = new PanelContainer
            {
                FocusMode = Control.FocusModeEnum.None,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            PanelTheme.Apply(chip);
            var icon = new Label
            {
                Text = "!",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FocusMode = Control.FocusModeEnum.None,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            icon.AddThemeFontSizeOverride("font_size", 20);
            icon.CustomMinimumSize = new Vector2(28f, 28f);
            chip.AddChild(icon);
            _issueBar?.AddChild(chip);
            _issueIcons.Add(chip);
        }

        PanelContainer chipShown = _issueIcons[index];
        var label = (Label)chipShown.GetChild(0);
        label.Text = warning ? "i" : "!";
        label.AddThemeColorOverride("font_color", warning ? ToolColours.OutlineWarn : ToolColours.OutlineError);
        chipShown.TooltipText = _strings?.Get(IssueKey(issue)) ?? issue.ToString();
        chipShown.Visible = true;
        if (_issueBar is not null)
        {
            _issueBar.Visible = true;
        }
    }

    private void HideIssueIcons()
    {
        foreach (PanelContainer icon in _issueIcons)
        {
            icon.Visible = false;
        }

        if (_issueBar is not null)
        {
            _issueBar.Visible = false;
        }
    }

    private bool AnyIssueIconVisible()
    {
        foreach (PanelContainer icon in _issueIcons)
        {
            if (icon.Visible)
            {
                return true;
            }
        }

        return false;
    }

    private void ShowGhost()
    {
        if (_ghostRoot is not null)
        {
            _ghostRoot.Visible = true;
        }
    }

    // Shows the overlay the placement wants (fertility for a farm, coverage for a service) while the build
    // tool is out, remembering what the player had open so cancelling restores it (usually nothing). The first
    // BeginBuild wins the stash, so switching buildings keeps restoring the pre-tool layer. A building with no
    // suggestion (a sawpit after a farm) falls back to that pre-tool layer instead of going stale.
    private void ShowPlacementOverlay(BuildingTypeId type)
    {
        _overlayBeforeTool ??= _overlays?.Shown;
        _overlayAutoShown = true;
        OverlaySpec? next = OverlayViewModel.PlacementLayer(_overlays?.SuggestFor(type), _overlayBeforeTool);
        if (_overlays is null)
        {
            return;
        }

        if (next is { } show)
        {
            _overlays.Show(show);
        }
        else
        {
            _overlays.HideLayer();
        }
    }

    // Hides the placement overlay, restoring what the player had open before the build tool started. Other
    // tools never auto-show, so their cancel leaves the sheet alone.
    private void RestorePlacementOverlay()
    {
        if (!_overlayAutoShown)
        {
            return;
        }

        _overlayAutoShown = false;
        if (_overlays is null)
        {
            return;
        }

        if (_overlayBeforeTool is { } previous)
        {
            _overlays.Show(previous);
        }
        else
        {
            _overlays.HideLayer();
        }

        _overlayBeforeTool = null;
    }

    private void HideGhost()
    {
        if (_ghostRoot is not null)
        {
            _ghostRoot.Visible = false;
        }

        if (_reasonPanel is not null && _mode != ToolMode.Road && _mode != ToolMode.Demolish)
        {
            _reasonPanel.Visible = false;
        }

        HideIssueIcons();
        _validX = int.MinValue;
        _hasPad = false;
    }

    private void SetGhostMesh(BuildingTypeId type)
    {
        if (_host is null || _ghostBody is null || _ghostPlinth is null)
        {
            return;
        }

        string name = _host.Content.Buildings.NameOf(type);
        BuildingDef def = _host.Content.Buildings.Get(type);
        _ghostBody.Mesh = _factory.BodyFor(name, def.Footprint.Width, def.Footprint.Height);
        _ghostPlinth.Mesh = _factory.PlinthUnit();
        _ghostMeshType = type;
        _hasGhostMesh = true;
    }

    private void PositionGhost(int x, int y, int width, int height)
    {
        if (_ghostRoot is null || _ghostBody is null || _ghostPlinth is null)
        {
            return;
        }

        float cx = x + (width / 2f);
        float cz = y + (height / 2f);
        _ghostRoot.Position = new Vector3(cx, _padHighest, cz);
        _ghostBody.Rotation = new Vector3(0f, -(float)(byte)_buildRotation * MathF.PI / 2f, 0f);
        float drop = _padHighest - _padLowest;
        _ghostPlinth.Visible = drop >= 0.02f;
        if (_ghostPlinth.Visible)
        {
            _ghostPlinth.Scale = new Vector3(width + 0.12f, drop, height + 0.12f);
            _ghostPlinth.Position = Vector3.Zero;
        }
    }

    // The footprint outline as a flat ribbon 0.06 units wide around the footprint's edges, at the terrain heights:
    // real width, not a 1 px line, so it reads on grass, forest, sand and hills. Rebuilt only on revalidation.
    private void RebuildGhostOutline(int x, int y, int width, int height, Color colour)
    {
        if (_host is null || _ghostOutlineMesh is null || _ghostOutline is null || _ghostRoot is null)
        {
            return;
        }

        const float half = OutlineWidth / 2f;
        Vector3 origin = _ghostRoot.Position;

        float h00 = GroundSampler.CornerHeight(_host.Query.Map, _host.Content, x, y) + OutlineLift;
        float h10 = GroundSampler.CornerHeight(_host.Query.Map, _host.Content, x + width, y) + OutlineLift;
        float h01 = GroundSampler.CornerHeight(_host.Query.Map, _host.Content, x, y + height) + OutlineLift;
        float h11 = GroundSampler.CornerHeight(_host.Query.Map, _host.Content, x + width, y + height) + OutlineLift;

        // Outer and inner corners of the ribbon, in edge order: north, east, south, west.
        Vector3[] corners =
        [
            new Vector3(x - half, h00, y - half),
            new Vector3(x + width + half, h10, y - half),
            new Vector3(x + width + half, h11, y + height + half),
            new Vector3(x - half, h01, y + height + half),
            new Vector3(x + half, h00, y + half),
            new Vector3(x + width - half, h10, y + half),
            new Vector3(x + width - half, h11, y + height - half),
            new Vector3(x + half, h01, y + height - half),
        ];
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] -= origin;
        }

        Vector3[] vertices =
        [
            corners[0], corners[1], corners[5], corners[0], corners[5], corners[4],
            corners[1], corners[2], corners[6], corners[1], corners[6], corners[5],
            corners[2], corners[3], corners[7], corners[2], corners[7], corners[6],
            corners[3], corners[0], corners[4], corners[3], corners[4], corners[7],
        ];
        var normals = new Vector3[vertices.Length];
        var colours = new Color[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            normals[i] = Vector3.Up;
            colours[i] = colour;
        }

        int[] indices = new int[vertices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        _ghostOutlineMesh.ClearSurfaces();
        _ghostOutlineMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _ghostOutlineMesh.SurfaceSetMaterial(0, _outlineMaterial);
    }

    private void ShowRoadPreview()
    {
        if (_roadPreview is not null)
        {
            _roadPreview.Visible = true;
        }
    }

    private void HideRoadPreview()
    {
        if (_roadPreview is not null)
        {
            _roadPreview.Visible = false;
        }

        _hasRoadCache = false;
        _roadValidAnchor = new TileCoord(int.MinValue, int.MinValue);
        if (_reasonPanel is not null && _mode != ToolMode.Build && _mode != ToolMode.Demolish)
        {
            _reasonPanel.Visible = false;
        }
    }

    private void ShowDemolishNodes()
    {
        if (_demoFill is not null)
        {
            _demoFill.Visible = true;
        }

        if (_demoOutline is not null)
        {
            _demoOutline.Visible = _demoDragging;
        }
    }

    private void HideDemolishNodes()
    {
        if (_demoFill is not null)
        {
            _demoFill.Visible = false;
        }

        if (_demoOutline is not null)
        {
            _demoOutline.Visible = false;
        }
    }

    private void HideDemolish()
    {
        HideDemolishNodes();
        if (_reasonPanel is not null && _mode != ToolMode.Build && _mode != ToolMode.Road)
        {
            _reasonPanel.Visible = false;
        }

        _demoValidX = int.MinValue;
        _rectValidMin = new TileCoord(int.MinValue, int.MinValue);
    }

    private void PositionDemolishBox(int x, int y, int width, int height, bool bad)
    {
        if (_demoFill is null || _demoBox is null || _host is null)
        {
            return;
        }

        // The fill stands a full two units tall around the target (not just a slab on the pad), so the tint reads
        // even over a tall finished building instead of hiding inside it.
        _demoBox.Size = new Vector3(width + 0.1f, 2f, height + 0.1f);
        _demoFill.Position = new Vector3(x + (width / 2f), _padHighest + 1f, y + (height / 2f));
        _demoFill.MaterialOverride = bad ? _demoBadMaterial : _demoOkMaterial;
        HideDemolishRect();
    }

    private void RebuildDemolishRect(TileCoord min, TileCoord max)
    {
        if (_host is null || _demoOutlineMesh is null || _demoOutline is null || _demoFill is null || _demoBox is null)
        {
            return;
        }

        int width = max.X - min.X + 1;
        int height = max.Y - min.Y + 1;
        float top = _padHighestForRect(min, max) + OutlineLift;
        _demoBox.Size = new Vector3(width + 0.1f, 2f, height + 0.1f);
        _demoFill.Position = new Vector3(min.X + (width / 2f), top + 1f, min.Y + (height / 2f));
        _demoFill.MaterialOverride = _demoOkMaterial;

        _demoOutlineMesh.ClearSurfaces();
        _demoOutlineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _demoOkMaterial);
        float x0 = min.X;
        float x1 = max.X + 1;
        float z0 = min.Y;
        float z1 = max.Y + 1;
        SurfaceLine(new Vector3(x0, top, z0), new Vector3(x1, top, z0));
        SurfaceLine(new Vector3(x1, top, z0), new Vector3(x1, top, z1));
        SurfaceLine(new Vector3(x1, top, z1), new Vector3(x0, top, z1));
        SurfaceLine(new Vector3(x0, top, z1), new Vector3(x0, top, z0));
        _demoOutlineMesh.SurfaceEnd();
        _demoOutline.Position = Vector3.Zero;

        void SurfaceLine(Vector3 a, Vector3 b)
        {
            _demoOutlineMesh.SurfaceSetNormal(Vector3.Up);
            _demoOutlineMesh.SurfaceAddVertex(a);
            _demoOutlineMesh.SurfaceSetNormal(Vector3.Up);
            _demoOutlineMesh.SurfaceAddVertex(b);
        }
    }

    private float _padHighestForRect(TileCoord min, TileCoord max)
    {
        if (_host is null)
        {
            return 0f;
        }

        float highest = float.MinValue;
        for (int vy = min.Y; vy <= max.Y + 1; vy++)
        {
            for (int vx = min.X; vx <= max.X + 1; vx++)
            {
                highest = Math.Max(highest, GroundSampler.CornerHeight(_host.Query.Map, _host.Content, vx, vy));
            }
        }

        return highest == float.MinValue ? 0f : highest;
    }

    private void HideDemolishRect()
    {
        if (_demoOutline is not null)
        {
            _demoOutline.Visible = false;
        }
    }

    private void RefreshStrings()
    {
        if (_host is null || ReferenceEquals(_stringsFor, _host.Content))
        {
            return;
        }

        _stringsFor = _host.Content;
        _strings = new UiStrings(_host.Locale);
        _hasGhostMesh = false;
    }

    private void FootprintOf(BuildingTypeId type, Rotation rotation, out int width, out int height)
    {
        if (_host is null)
        {
            width = 1;
            height = 1;
            return;
        }

        Footprint footprint = rotation.Rotate(_host.Content.Buildings.Get(type).Footprint);
        width = footprint.Width;
        height = footprint.Height;
    }

    private string NameOf(BuildingTypeId type)
    {
        if (_host is null)
        {
            return type.ToString();
        }

        string name = _host.Content.Buildings.NameOf(type);
        return DisplayName(name);
    }

    private string DisplayName(string contentName)
    {
        if (_strings is null)
        {
            return contentName;
        }

        string key = $"building.{contentName}.name";
        string text = _strings.Get(key);
        return text == key ? contentName : text;
    }

    private bool IsProtectedHall(EntityId id)
    {
        // The sim is the source of truth and rejects the Founders' Hall with RejectReason.Protected; the name check
        // here only tints the hover red up front so the player is not surprised.
        if (_host is null || !_host.Query.Map.TryGetBuilding(id, out BuildingInfo info))
        {
            return false;
        }

        return _host.Content.Buildings.NameOf(info.Type) == "founders_hall";
    }

    private bool TryDefaultRoadType(out BuildingTypeId road)
    {
        if (_host is { IsRunning: true })
        {
            foreach (string name in _host.Content.Buildings.Names)
            {
                BuildingTypeId id = _host.Content.Buildings.IdOf(name);
                if (_host.Content.Buildings.Get(id).RoadCost.HasValue)
                {
                    road = id;
                    return true;
                }
            }
        }

        road = default;
        return false;
    }

    private BuildingTypeId DefaultBuildType()
    {
        if (_host is { IsRunning: true })
        {
            foreach (string name in _host.Content.Buildings.Names)
            {
                BuildingTypeId id = _host.Content.Buildings.IdOf(name);
                if (!_host.Content.Buildings.Get(id).RoadCost.HasValue)
                {
                    return id;
                }
            }

            return _host.Content.Buildings.IdOf(_host.Content.Buildings.Names[0]);
        }

        return default;
    }

    private static TileCoord ClampPath(TileCoord anchor, TileCoord current, int maxTiles)
    {
        int dx = current.X - anchor.X;
        int dy = current.Y - anchor.Y;
        while (Math.Abs(dx) + Math.Abs(dy) + 1 > maxTiles)
        {
            if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0)
            {
                dx -= Math.Sign(dx);
            }
            else if (dy != 0)
            {
                dy -= Math.Sign(dy);
            }
            else
            {
                break;
            }
        }

        return new TileCoord(anchor.X + dx, anchor.Y + dy);
    }

    private static string IssueKey(PlacementIssue issue) => issue switch
    {
        PlacementIssue.None => "ui.tools.issue.none",
        PlacementIssue.OutOfBounds => "ui.tools.issue.out_of_bounds",
        PlacementIssue.Occupied => "ui.tools.issue.occupied",
        PlacementIssue.TerrainNotAllowed => "ui.tools.issue.terrain_not_allowed",
        PlacementIssue.TerrainAccessLocked => "ui.tools.issue.terrain_access_locked",
        PlacementIssue.SlopeTooSteep => "ui.tools.issue.slope_too_steep",
        PlacementIssue.NotUnlocked => "ui.tools.issue.not_unlocked",
        PlacementIssue.UniqueAlreadyBuilt => "ui.tools.issue.unique_already_built",
        PlacementIssue.NeedsCoastline => "ui.tools.issue.needs_coastline",
        PlacementIssue.NeedsDeposit => "ui.tools.issue.needs_deposit",
        PlacementIssue.FertilityTooLow => "ui.tools.issue.fertility_too_low",
        PlacementIssue.NeedsRiverside => "ui.tools.issue.needs_riverside",
        PlacementIssue.NoRoadAccess => "ui.tools.issue.no_road_access",
        PlacementIssue.OutsideStorehouseReach => "ui.tools.issue.outside_storehouse_reach",
        PlacementIssue.LowYield => "ui.tools.issue.low_yield",
        _ => "ui.tools.issue.none",
    };

    private static string RejectKey(RejectReason reason) => reason switch
    {
        RejectReason.UnknownCommand => "ui.tools.reject.unknown_command",
        RejectReason.Invalid => "ui.tools.reject.invalid",
        RejectReason.NotImplemented => "ui.tools.reject.not_implemented",
        RejectReason.UnknownTarget => "ui.tools.reject.unknown_target",
        RejectReason.PlacementInvalid => "ui.tools.reject.placement_invalid",
        RejectReason.NodeNotAvailable => "ui.tools.reject.node_not_available",
        RejectReason.NotApplicable => "ui.tools.reject.not_applicable",
        RejectReason.Protected => "ui.tools.reject.protected",
        _ => "ui.tools.reject.generic",
    };

    private static void LogTool(string what) => GD.Print($"[Evenmere] Tools: tool {what}");

    private void BuildMaterials()
    {
        // Flat translucent bodies (no vertex colours): one glance tells whether the spot works, on any terrain.
        _ghostOkMaterial = FlatGhost(ToolColours.GhostOk);
        _ghostWarnMaterial = FlatGhost(ToolColours.GhostWarn);
        _ghostBadMaterial = FlatGhost(ToolColours.GhostError);
        _outlineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _roadMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(1f, 1f, 1f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = OverlayRenderer.AfterLookPriority,
        };
        _demoOkMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = ToolColours.DemolishOk,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = OverlayRenderer.AfterLookPriority,
        };
        _demoBadMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = ToolColours.DemolishBad,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = OverlayRenderer.AfterLookPriority,
        };
    }

    private static StandardMaterial3D FlatGhost(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = colour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        RenderPriority = OverlayRenderer.AfterLookPriority,
    };

    private void BuildGhostNodes()
    {
        _ghostRoot = new Node3D { Name = "BuildGhost", Visible = false, TopLevel = false };
        AddChild(_ghostRoot);

        _ghostPlinth = new MeshInstance3D
        {
            Name = "Plinth",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _ghostRoot.AddChild(_ghostPlinth);

        _ghostBody = new MeshInstance3D
        {
            Name = "Body",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _ghostRoot.AddChild(_ghostBody);

        _ghostOutlineMesh = new ArrayMesh();
        _ghostOutline = new MeshInstance3D
        {
            Name = "Outline",
            Mesh = _ghostOutlineMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _ghostRoot.AddChild(_ghostOutline);
    }

    private void BuildRoadPreview()
    {
        _roadPreview = new MeshInstance3D
        {
            Name = "RoadPreview",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_roadPreview);
    }

    private void BuildDemolishNodes()
    {
        _demoBox = new BoxMesh();
        _demoFill = new MeshInstance3D
        {
            Name = "DemolishFill",
            Mesh = _demoBox,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_demoFill);

        _demoOutlineMesh = new ImmediateMesh();
        _demoOutline = new MeshInstance3D
        {
            Name = "DemolishOutline",
            Mesh = _demoOutlineMesh,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_demoOutline);
    }

    private void BuildUi()
    {
        _layer = new CanvasLayer { Name = "ToolLayer", Layer = 10 };
        AddChild(_layer);

        _issueBar = new HBoxContainer
        {
            Name = "IssueBar",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _issueBar.AddThemeConstantOverride("separation", 6);
        PanelTheme.Apply(_issueBar);
        _layer.AddChild(_issueBar);

        _reasonPanel = new PanelContainer
        {
            Name = "Reason",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        PanelTheme.Apply(_reasonPanel);
        _reasonLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _reasonPanel.AddChild(_reasonLabel);
        _layer.AddChild(_reasonPanel);

        _toast = new ToolToast { Name = "ToolToast" };
        _toast.AnchorLeft = 0.5f;
        _toast.AnchorRight = 0.5f;
        _toast.AnchorTop = 1f;
        _toast.AnchorBottom = 1f;
        _toast.OffsetLeft = -260f;
        _toast.OffsetRight = 260f;
        _toast.OffsetTop = -220f;
        _toast.OffsetBottom = -140f;
        _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        _layer.AddChild(_toast);

        _demolishConfirm = new ConfirmationDialog { Name = "DemolishConfirm" };
        _layer.AddChild(_demolishConfirm);
        _demolishConfirm.Confirmed += EnqueuePendingNow;
        _demolishConfirm.Canceled += ClearPending;
    }

    private void ClearPending()
    {
        _pendingBuildings.Clear();
        _pendingRoads.Clear();
    }
}
