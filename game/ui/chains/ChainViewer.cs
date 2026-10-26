using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Hud;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Ui.Chains;

/// <summary>
/// The production chain viewer (M2-21): a docked panel on the right that shows the selected good's chain as a graph,
/// with live rates and bottlenecks. Each node is a button with the building count, average productivity and produced
/// and consumed per day; the bottleneck node from <c>ExplainGood</c> carries a "!" marker in the warning tint.
/// Clicking a node lists its buildings and focuses the worst one. It opens on a good through <see cref="Show"/>;
/// opening hides the research, civics and inspector panels, and it closes on <c>tool_cancel</c> or its Close button.
/// Navigation uses the built-in <c>ui_*</c> focus actions only: no new input actions.
/// </summary>
public sealed partial class ChainViewer : PanelContainer
{
    private const float PanelWidth = 416f;
    private const float PanelTop = 150f;
    private const float PanelMargin = 16f;

    // The pause menu's Menu button sits bottom-right (124 by 48 at a 16 margin); the panel ends above it with the
    // same margin, so the viewer's own buttons stay fully visible and clickable at every window size.
    private const float MenuReserve = 64f + PanelMargin;

    private const float CellWidth = 300f;
    private const float CellHeight = 88f;
    private const float GapX = 40f;
    private const float GapY = 18f;
    private const float Pad = 8f;

    private static readonly Color _lineColour = new(0.55f, 0.52f, 0.47f);

    private SimHost? _host;
    private CameraRig? _rig;
    private ContentDb? _builtFor;
    private UiStrings? _strings;
    private ChainViewModel? _model;
    private string? _signature;
    private string? _debugGood;
    private GoodId? _pendingShow;
    private bool _debugLabels;
    private bool _focusWorst;

    private Label? _title;
    private Label? _bottleneck;
    private Button? _closeButton;
    private ScrollContainer? _scroll;
    private ChainCanvas? _canvas;
    private Label? _buildingsHeader;
    private VBoxContainer? _buildings;
    private Label? _debug;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the <see cref="CameraRig"/> node, relative to this node.</summary>
    [Export]
    public NodePath RigPath { get; set; } = new("../CameraRig");

    /// <summary>Gets or sets the path to the research panel, hidden while the viewer is open.</summary>
    [Export]
    public NodePath ResearchPath { get; set; } = new("../ResearchPanel");

    /// <summary>Gets or sets the path to the civics panel, hidden while the viewer is open.</summary>
    [Export]
    public NodePath CulturePath { get; set; } = new("../CulturePanel");

    /// <summary>Gets or sets the path to the inspector panel, hidden while the viewer is open.</summary>
    [Export]
    public NodePath InspectorPath { get; set; } = new("../InspectorPanel");

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        PanelTheme.Apply(this);
        _host = GetNodeOrNull<SimHost>(HostPath);
        _rig = GetNodeOrNull<CameraRig>(RigPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] chains: no SimHost at '{HostPath}'; the viewer stays empty.");
            return;
        }

        if (OS.IsDebugBuild())
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--chain=", StringComparison.Ordinal))
                {
                    _debugGood = arg["--chain=".Length..];
                    // The statistics debug line below is a review aid: it shows only in debug builds
                    // opened through the --chain= flag, never in a normal run.
                    _debugLabels = true;
                }
            }
        }

        BuildChrome();
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
            return;
        }

        if (!ReferenceEquals(_builtFor, host.Content))
        {
            _builtFor = host.Content;
            _strings = new UiStrings(host.Locale);
            _model = new ChainViewModel(
                host.Content,
                _strings,
                new SimChainData(host.Content, host.Query.Map, host.Query.Production, host.Query.Statistics, host.Query.Explain));
            _signature = null;
            Retitle();
        }

        ChainViewModel? model = _model;
        if (model is null)
        {
            return;
        }

        // The inspector's Production-chain button can fire before this panel's first refresh created
        // the model; the pending selection lands as soon as the model exists.
        if (!string.IsNullOrEmpty(_debugGood))
        {
            Show(_debugGood);
            _debugGood = null;
        }

        if (_pendingShow.HasValue && _model is not null)
        {
            GoodId pending = _pendingShow.Value;
            _pendingShow = null;
            Show(pending);
        }

        // A research, civics or inspector panel opened elsewhere hides this one, so at most one
        // docked panel is open (showing this one hides the others).
        if (Visible && OtherPanelOpen())
        {
            Visible = false;
        }

        if (!Visible)
        {
            return;
        }

        if (model.Advance(delta))
        {
            RefreshIfChanged();
        }

        if (_focusWorst)
        {
            _focusWorst = false;
            if (model.WorstBuilding is { IsNone: false } worst)
            {
                FocusBuilding(worst);
            }
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        // Unhandled, matching the inspector: while a tool is active the tools see Esc first and
        // cancel the tool, and only an idle Esc closes this panel.
        if (!Visible || @event.IsEcho())
        {
            return;
        }

        if (InputMap.HasAction("tool_cancel") && @event.IsActionPressed("tool_cancel"))
        {
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Shows the chain of a good.</summary>
    /// <param name="good">The good.</param>
    public void Show(GoodId good)
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        if (_model is null)
        {
            _pendingShow = good;
            return;
        }

        HideOthers();
        _model.Select(good);
        _model.Advance(10.0);
        Visible = true;
        _focusWorst = true;
        RefreshIfChanged();
        GD.Print($"[Evenmere] Chains: showing {_host.Content.NameOf(good)}.");
    }

    /// <summary>Shows the chain of a good by its string ID; unknown names are ignored.</summary>
    /// <param name="name">The good's string ID, such as <c>bread</c>.</param>
    public void Show(string name)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (host.Content.TryGet<GoodId>(name, out GoodId good))
        {
            Show(good);
        }
        else
        {
            GD.PushWarning($"[Evenmere] chains: unknown good '{name}'; the viewer stays closed.");
        }
    }

    private void OnEvents(IReadOnlyList<SimEvent> events) => _model?.MarkDirty();

    private void Place()
    {
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = -PanelWidth;
        OffsetRight = -PanelMargin;
        OffsetTop = PanelTop;
        OffsetBottom = -MenuReserve;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void BuildChrome()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        margin.AddChild(top);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        _title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(_title);

        _closeButton = new Button { FocusMode = FocusModeEnum.All };
        _closeButton.Pressed += () => Visible = false;
        header.AddChild(_closeButton);

        _bottleneck = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _bottleneck.AddThemeColorOverride("font_color", HudChip.WarningTint);
        top.AddChild(_bottleneck);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FollowFocus = true,
        };
        top.AddChild(scroll);
        _scroll = scroll;

        _canvas = new ChainCanvas();
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_canvas);

        _buildingsHeader = new Label();
        _buildingsHeader.AddThemeFontSizeOverride("font_size", 16);
        top.AddChild(_buildingsHeader);

        _buildings = new VBoxContainer();
        _buildings.AddThemeConstantOverride("separation", 4);
        top.AddChild(_buildings);

        _debug = new Label();
        _debug.AddThemeFontSizeOverride("font_size", 12);
        top.AddChild(_debug);

        Retitle();
        RefreshIfChanged();
    }

    private void Retitle()
    {
        if (_strings is null || _closeButton is null || _title is null)
        {
            return;
        }

        _closeButton.Text = _strings.Get("ui.inspector.close");
        _closeButton.TooltipText = _strings.Get("ui.inspector.close.tip");
        _title.Text = _model?.Title ?? string.Empty;
    }

    // Rebuilds the node buttons and building rows only when the visible text changed, so the slow poll
    // (which re-reads live rates) never steals gamepad focus by rebuilding identical buttons.
    private void RefreshIfChanged()
    {
        if (_model is null || _canvas is null || _title is null || _bottleneck is null
            || _buildingsHeader is null || _buildings is null || _debug is null || _strings is null)
        {
            return;
        }

        string signature = SignatureOf(_model);
        if (string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            return;
        }

        _signature = signature;
        _title.Text = _model.Title;
        _bottleneck.Text = _model.BottleneckLine;
        _bottleneck.Visible = !string.IsNullOrEmpty(_model.BottleneckLine);
        _debug.Text = OS.IsDebugBuild() && _debugLabels ? _model.DebugLine : string.Empty;
        _debug.Visible = !string.IsNullOrEmpty(_debug.Text);

        foreach (Node child in _canvas.GetChildren())
        {
            _canvas.RemoveChild(child);
            child.QueueFree();
        }

        var cells = new Dictionary<GoodId, Rect2>();
        int maxColumn = 0;
        int maxRow = 0;
        foreach (ChainRow row in _model.Rows)
        {
            maxColumn = Math.Max(maxColumn, row.Column);
            maxRow = Math.Max(maxRow, row.Row);
        }

        foreach (ChainRow row in _model.Rows)
        {
            var button = new Button
            {
                FocusMode = FocusModeEnum.All,
                CustomMinimumSize = new Vector2(CellWidth, CellHeight),
                SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
                Text = NodeText(row),
                TooltipText = row.RecipeLine,
            };
            if (row.IsBottleneck)
            {
                button.AddThemeColorOverride("font_color", HudChip.WarningTint);
            }

            ChainRow captured = row;
            button.Pressed += () => SelectNode(captured.Good);
            button.Position = new Vector2(Pad + row.Column * (CellWidth + GapX), Pad + row.Row * (CellHeight + GapY));
            button.Size = new Vector2(CellWidth, CellHeight);
            _canvas.AddChild(button);
            cells[row.Good] = new Rect2(button.Position, button.Size);
        }

        var links = new List<(Rect2 From, Rect2 To, bool Alert)>();
        var shortage = new HashSet<GoodId>(_model.ShortagePath) { _model.Selected };
        foreach (ChainEdge edge in _model.Edges)
        {
            if (cells.TryGetValue(edge.From, out Rect2 from) && cells.TryGetValue(edge.To, out Rect2 to))
            {
                links.Add((from, to, shortage.Contains(edge.From) && shortage.Contains(edge.To)));
            }
        }

        float width = Pad * 2 + (maxColumn + 1) * CellWidth + maxColumn * GapX;
        float height = Pad * 2 + (maxRow + 1) * CellHeight + maxRow * GapY;
        _canvas.CustomMinimumSize = new Vector2(Math.Max(width, CellWidth + Pad * 2), Math.Max(height, 1f));
        _canvas.SetLayout(cells, links);

        foreach (Node child in _buildings.GetChildren())
        {
            _buildings.RemoveChild(child);
            child.QueueFree();
        }

        if (_model.HasSelection)
        {
            _buildingsHeader.Visible = true;
            _buildingsHeader.Text = _strings.GoodName(_builtFor?.NameOf(_model.Selected) ?? string.Empty);
        }
        else
        {
            _buildingsHeader.Visible = false;
        }

        foreach (ChainRowBuilding building in _model.Buildings)
        {
            var row = new Button
            {
                FocusMode = FocusModeEnum.All,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Text = BuildingText(building),
            };
            if (building.IsWorst)
            {
                row.AddThemeColorOverride("font_color", HudChip.WarningTint);
            }

            EntityId captured = building.Id;
            row.Pressed += () => FocusBuilding(captured);
            _buildings.AddChild(row);
        }
    }

    private static string SignatureOf(ChainViewModel model)
    {
        var parts = new List<string>(model.Rows.Count * 2 + model.Buildings.Count + 3);
        parts.Add(model.Title);
        parts.Add(model.BottleneckLine);
        parts.Add(model.DebugLine);
        foreach (ChainRow row in model.Rows)
        {
            parts.Add($"{row.Good.Value}|{row.RecipeLine}|{row.StatsLine}|{row.RatesLine}|{row.IsBottleneck}");
        }

        foreach (ChainRowBuilding building in model.Buildings)
        {
            parts.Add($"{building.Id.Value}|{building.Name}|{building.ProductivityPerMille}|{building.StallText}|{building.IsWorst}");
        }

        return string.Join("\n", parts);
    }

    private static string NodeText(ChainRow row)
    {
        string marker = row.IsBottleneck ? "! " : string.Empty;
        return $"{marker}{row.Name}\n{row.StatsLine}\n{row.RatesLine}";
    }

    private static string BuildingText(ChainRowBuilding building)
    {
        string marker = building.IsWorst ? "! " : string.Empty;
        string percent = Explain.ExplainFormat.FormatPercent(
            Evenmere.Core.Numerics.Ratio.FromPerMille(building.ProductivityPerMille));
        return $"{marker}{building.Name} · {percent} · {building.StallText}";
    }

    private void SelectNode(GoodId good)
    {
        if (_model is null)
        {
            return;
        }

        _model.Select(good);
        _model.Advance(10.0);
        _focusWorst = true;
        if (_scroll is not null)
        {
            _scroll.ScrollVertical = 0;
        }

        RefreshIfChanged();
    }

    private void FocusBuilding(EntityId building)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (host.Query.Map.TryGetBuilding(building, out BuildingInfo info))
        {
            _rig?.FocusOnTile(new Vector2I(info.Origin.X, info.Origin.Y));
            GD.Print($"[Evenmere] Chains: focusing building {building.Value}.");
        }
    }

    private bool OtherPanelOpen()
    {
        return (GetNodeOrNull<Control>(ResearchPath) is { Visible: true })
            || (GetNodeOrNull<Control>(CulturePath) is { Visible: true })
            || (GetNodeOrNull<Control>(InspectorPath) is { Visible: true });
    }

    private void HideOthers()
    {
        if (GetNodeOrNull<Control>(ResearchPath) is { } research)
        {
            research.Visible = false;
        }

        if (GetNodeOrNull<Control>(CulturePath) is { } culture)
        {
            culture.Visible = false;
        }

        if (GetNodeOrNull<Control>(InspectorPath) is { } inspector)
        {
            inspector.Visible = false;
        }
    }

    /// <summary>Draws the elbow connectors between the node buttons of one chain graph: each input hangs
    /// below the good it feeds, so connectors run vertically from an output's bottom edge to its input's top.</summary>
    private sealed partial class ChainCanvas : Control
    {
        private List<(Rect2 From, Rect2 To, bool Alert)> _links = new();

        /// <summary>Replaces the connectors and redraws.</summary>
        /// <param name="cells">The node rects, by good (kept by the viewer; unused here beyond the call).</param>
        /// <param name="links">The connectors to draw.</param>
        public void SetLayout(Dictionary<GoodId, Rect2> cells, List<(Rect2 From, Rect2 To, bool Alert)> links)
        {
            ArgumentNullException.ThrowIfNull(cells);
            _links = links ?? throw new ArgumentNullException(nameof(links));
            QueueRedraw();
        }

        /// <inheritdoc />
        public override void _Draw()
        {
            foreach ((Rect2 from, Rect2 to, bool alert) in _links)
            {
                var start = new Vector2(to.GetCenter().X, to.End.Y);
                var end = new Vector2(from.GetCenter().X, from.Position.Y);
                float midY = (start.Y + end.Y) * 0.5f;
                Color colour = alert ? HudChip.WarningTint : _lineColour;
                DrawLine(start, new Vector2(start.X, midY), colour, 2f);
                DrawLine(new Vector2(start.X, midY), new Vector2(end.X, midY), colour, 2f);
                DrawLine(new Vector2(end.X, midY), end, colour, 2f);
            }
        }
    }
}
