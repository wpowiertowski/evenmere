using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Controls;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Research.Commands;
using Godot;

namespace Evenmere.Game.Ui.Research;

/// <summary>
/// The research graph panel: the Era I tech tree as a navigable graph with cost, progress, inspiration and
/// unlocks on every card, opened and closed by <c>ui_research</c> and <c>tool_cancel</c> and by the
/// HUD-adjacent toggles. Clicking a name makes it the target (<see cref="SetResearchTarget"/>);
/// Shift-clicking or the card's queue button appends it to the queue (<see cref="SetResearchQueue"/>).
/// </summary>
public sealed partial class ResearchPanel : PanelContainer
{
    private const float PanelMargin = 16f;
    private const float FallbackLeft = 516f;
    private const float FallbackTop = 128f;

    private static readonly string[] _visualFiles =
    [
        "res://data/visuals/era1-slice.json",
        "res://data/visuals/era1-production.json",
        "res://data/visuals/era1-trees.json",
    ];

    private SimHost? _host;
    private ContentDb? _builtFor;
    private ISimQueries? _builtForQueries;
    private ResearchViewModel? _model;
    private UiStrings? _strings;
    private Dictionary<string, string>? _glyphs;
    private Label? _title;
    private Button? _close;
    private Label? _subtitle;
    private Label? _note;
    private Label? _teaser;
    private TreeView? _tree;
    private DebugScript? _debug;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the civics panel, hidden when this panel opens.</summary>
    [Export]
    public NodePath OtherPath { get; set; } = new("../CulturePanel");

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] research: no SimHost at '{HostPath}'; the panel stays empty.");
            return;
        }

        BuildChrome();
        _host.EventsDrained += OnEvents;
        _debug = DebugScript.Parse();
        Visible = false;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_host is not null)
        {
            _host.EventsDrained -= OnEvents;
        }

        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        SyncToToggles();

        if (_model is null || NeedsRebuild(host))
        {
            RebuildModel(host);
        }

        _model?.Advance(delta);
        _debug?.Tick(this);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event.IsEcho() || _host is null)
        {
            return;
        }

        if (InputMap.HasAction(InputActions.UiResearch) && @event.IsActionPressed(InputActions.UiResearch))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (Visible && InputMap.HasAction("tool_cancel") && @event.IsActionPressed("tool_cancel"))
        {
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the panel if it is closed and closes it if it is open.</summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            if (GetNodeOrNull<Control>(OtherPath) is { } other)
            {
                other.Visible = false;
            }

            _model?.ClearNote();
            _model?.MarkDirty();
            _model?.Advance(10.0);
        }
    }

    private void Place()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = FallbackLeft;
        OffsetRight = -PanelMargin;
        OffsetTop = FallbackTop;
        OffsetBottom = -PanelMargin;
        MouseFilter = Control.MouseFilterEnum.Stop;
    }

    private void SyncToToggles()
    {
        // The graph spans from just right of the research and civics toggles to the right edge, under the
        // HUD bar (which stays visible), so the Era I tree fits at 1280 × 800 without panning. The toggles
        // sit under the HUD bar and move when it grows, so this follows them every frame.
        if (GetNodeOrNull<Control>("../PanelToggles") is not { } toggles)
        {
            return;
        }

        Rect2 bounds = toggles.GetGlobalRect();
        OffsetLeft = bounds.End.X + PanelMargin;
        OffsetTop = bounds.Position.Y;
    }

    private void RebuildModel(SimHost host)
    {
        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }

        _strings = new UiStrings(host.Locale);
        _builtFor = host.Content;
        _builtForQueries = host.Query;
        _glyphs = LoadGlyphs(host.Content);
        _model = new ResearchViewModel(
            host.Content,
            _strings,
            host.Query.Research,
            host.Query.Progression);
        _model.Refreshed += OnRefreshed;
        _model.MarkDirty();
        Retitle();
    }

    // The content object outlives a save load (the host loads it once and reuses it), so the content
    // identity alone never notices a new world: the queries object is tracked too, and the simulation
    // creates it fresh with every game. Without this a loaded save keeps showing the old world's answers.
    private bool NeedsRebuild(SimHost host) =>
        !ReferenceEquals(_builtFor, host.Content) || !ReferenceEquals(_builtForQueries, host.Query);

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        // Events can arrive before the first refresh built the model (for example a command sent in the same
        // frame the panel appeared), so build it here rather than dropping the rejection note.
        if (_model is null && _host is { IsRunning: true } host)
        {
            RebuildModel(host);
        }

        if (_model is null)
        {
            return;
        }

        foreach (SimEvent @event in events)
        {
            if (@event is CommandRejected rejected)
            {
                _model.NoteRejected(rejected);
            }
        }

        _model.MarkDirty();
    }

    private void OnRefreshed() => UpdateAll();

    private void Send(Command? command)
    {
        if (_host is null || _model is null || command is null)
        {
            return;
        }

        _model.ClearNote();
        _host.Enqueue(command);
        _model.MarkDirty();
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

        var title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(title);
        _title = title;

        var close = new Button { FocusMode = FocusModeEnum.All };
        close.Pressed += () => Visible = false;
        header.AddChild(close);
        _close = close;

        var subtitle = new Label();
        subtitle.AddThemeFontSizeOverride("font_size", 16);
        top.AddChild(subtitle);
        _subtitle = subtitle;

        var note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        note.AddThemeFontSizeOverride("font_size", 16);
        top.AddChild(note);
        _note = note;

        var tree = new TreeView { SizeFlagsVertical = SizeFlags.ExpandFill };
        tree.TargetRequested += OnTargetRequested;
        tree.QueueRequested += OnQueueRequested;
        top.AddChild(tree);
        _tree = tree;

        var teaser = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        teaser.AddThemeFontSizeOverride("font_size", 16);
        top.AddChild(teaser);
        _teaser = teaser;
    }

    private void Retitle()
    {
        if (_strings is null || _title is null || _close is null)
        {
            return;
        }

        _title.Text = _strings.Get("ui.research.title");
        _close.Text = _strings.Get("ui.panel.close");
        _close.TooltipText = _strings.Get("ui.panel.close.tip");
    }

    private void UpdateAll()
    {
        if (_model is null || _strings is null || _tree is null || _glyphs is null)
        {
            return;
        }

        UiStrings strings = _strings;
        TreeSnapshot<TechId> snapshot = _model.Snapshot;

        _subtitle!.Text = strings.Format(
            "ui.tree.subtitle",
            ("done", NumberFormat.Count(snapshot.DoneCount)),
            ("total", NumberFormat.Count(snapshot.TotalCount)));

        if (_teaser is not null)
        {
            _teaser.Visible = snapshot.NextEraName is not null;
            _teaser.Text = snapshot.NextEraName is { } era
                ? strings.Format(
                    "ui.tree.next_era",
                    ("era", era),
                    ("count", NumberFormat.Count(snapshot.NextEraCount)))
                : string.Empty;
        }

        if (_note is not null)
        {
            _note.Visible = _model.Note is not null;
            _note.Text = _model.Note ?? string.Empty;
        }

        Dictionary<string, string> glyphs = _glyphs;
        string? GlyphFor(string building) =>
            glyphs.TryGetValue(building, out string? glyph) ? glyph : null;

        TreeGraph graph = _model.BuildGraph(GlyphFor);
        _tree.SetNodes(graph.Nodes, graph.Layout);
    }

    private void OnTargetRequested(int index)
    {
        if (_model is null || index < 0 || index >= _model.GraphCount)
        {
            return;
        }

        Send(_model.TargetCommand(_model.GraphId(index)));
    }

    private void OnQueueRequested(int index)
    {
        if (_model is null || index < 0 || index >= _model.GraphCount)
        {
            return;
        }

        Send(_model.AppendCommand(_model.GraphId(index)));
    }

    private static Dictionary<string, string> LoadGlyphs(ContentDb content)
    {
        // Until IconLibrary lands (ART-02), cards show the placeholder glyphs the build menu uses. Later
        // visuals files do not exist yet (M2-13); they are picked up when they land, without a code change.
        var glyphs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in _visualFiles)
        {
            if (!Godot.FileAccess.FileExists(file))
            {
                continue;
            }

            string json = Godot.FileAccess.GetFileAsString(file);
            if (string.IsNullOrEmpty(json))
            {
                continue;
            }

            var map = BuildingVisualMap.Load(
                json,
                static text => GD.PushWarning($"[Evenmere] research: {text}"));
            foreach (string name in content.Buildings.Names)
            {
                if (map.TryGet(name, out BuildingVisual visual) && !glyphs.ContainsKey(name))
                {
                    glyphs[name] = visual.Icon.Length > 0 ? visual.Icon : visual.Shape.ToString()[..1];
                }
            }
        }

        return glyphs;
    }

    // Staged screenshots (debug builds only): --research=show opens the panel once the world is running,
    // --research-zoom=<factor> multiplies the graph zoom once (0.5 for the Era I overview still), and
    // --research-focus=<index> moves keyboard focus to one card (scrolling it into view, for the bottom
    // of the Era I tree), the same pattern as the build menu's own debug script.
    private sealed class DebugScript
    {
        private readonly bool _show;
        private readonly float _zoom;
        private readonly int? _focus;
        private bool _zoomed;
        private bool _done;

        private DebugScript(bool show, float zoom, int? focus)
        {
            _show = show;
            _zoom = zoom;
            _focus = focus;
        }

        public static DebugScript? Parse()
        {
            if (!OS.IsDebugBuild())
            {
                return null;
            }

            bool show = false;
            float zoom = 1f;
            int? focus = null;
            bool seen = false;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--research=show")
                {
                    show = true;
                    seen = true;
                }
                else if (arg.StartsWith("--research-focus=", StringComparison.Ordinal)
                    && int.TryParse(
                        arg["--research-focus=".Length..],
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int focused))
                {
                    focus = focused;
                    seen = true;
                }
                else if (arg.StartsWith("--research-zoom=", StringComparison.Ordinal)
                    && float.TryParse(
                        arg["--research-zoom=".Length..],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float parsed)
                    && parsed > 0f)
                {
                    zoom = parsed;
                    seen = true;
                }
            }

            return seen ? new DebugScript(show, zoom, focus) : null;
        }

        public void Tick(ResearchPanel panel)
        {
            if (panel._model is null || panel._host is not { IsRunning: true })
            {
                return;
            }

            if (_show && !panel.Visible)
            {
                panel.Toggle();
            }

            if (!_zoomed)
            {
                if (_zoom != 1f)
                {
                    panel._tree?.ZoomBy(_zoom);
                }

                _zoomed = true;
            }

            if (_focus.HasValue)
            {
                // Pinned every frame, not once: on the first frames the scroll container has no layout yet,
                // so the scroll clamps to zero; the capture quits the process, so there is no last frame to
                // miss. Without a focus flag this runs once like the other one-shots.
                panel._tree?.FocusCard(_focus.Value);
            }
            else if (!_done)
            {
                _done = true;
            }
        }
    }
}
