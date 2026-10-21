using System.Diagnostics.CodeAnalysis;
using Evenmere.Content;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim;
using Godot;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// Builds and refreshes a research or civics panel inside a <see cref="PanelContainer"/>: the title, the
/// grouped rows, the next-era teaser and the inline rejection note. The two concrete panel scripts are thin
/// owners of one of these; everything shared lives here. It reads the host's queries and sends commands, and
/// never holds simulation state beyond one refresh.
/// </summary>
/// <typeparam name="TId">The tech or civic ID type.</typeparam>
[SuppressMessage(
    "Microsoft.Design",
    "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "Godot owns node lifetimes: the rows are children of the panel and the scene tree frees them.")]
public sealed class TreePanelController<TId>
    where TId : struct, IEquatable<TId>
{
    private const float PanelWidth = 416f;
    private const float PanelTop = 150f;
    private const float PanelMargin = 16f;

    private readonly PanelContainer _root;
    private readonly string _titleKey;
    private readonly StringName _toggleAction;
    private readonly Func<SimHost, TreeViewModel<TId>> _makeModel;

    private SimHost? _host;
    private ContentDb? _builtFor;
    private TreeViewModel<TId>? _model;
    private UiStrings? _strings;
    private Label? _title;
    private Button? _close;
    private Label? _subtitle;
    private Label? _note;
    private Label? _teaser;
    private VBoxContainer? _list;
    private ScrollContainer? _scroll;
    private Button? _researchedToggle;
    private VBoxContainer? _researchedBox;
    private bool _researchedOpen;
    private readonly Dictionary<ProgressGroup, VBoxContainer> _groupBoxes = new();
    private readonly Dictionary<ProgressGroup, Label> _groupHeaders = new();
    private readonly Dictionary<TId, TreeRow> _rows = new();
    private readonly Dictionary<TId, RowWidgets> _widgets = new();

    /// <summary>Initializes a new instance of the <see cref="TreePanelController{TId}"/> class.</summary>
    /// <param name="root">The panel node this controller fills.</param>
    /// <param name="titleKey">The locale key of the panel's title.</param>
    /// <param name="toggleAction">The input action that opens and closes the panel.</param>
    /// <param name="makeModel">Creates the panel's view-model from the running host.</param>
    public TreePanelController(
        PanelContainer root,
        string titleKey,
        StringName toggleAction,
        Func<SimHost, TreeViewModel<TId>> makeModel)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(titleKey);
        ArgumentNullException.ThrowIfNull(toggleAction);
        ArgumentNullException.ThrowIfNull(makeModel);
        _root = root;
        _titleKey = titleKey;
        _toggleAction = toggleAction;
        _makeModel = makeModel;
    }

    /// <summary>Gets or sets what runs when the panel opens, so the research and civics panels can close each other.</summary>
    public Action? Opened { get; set; }

    /// <summary>Finds the host, builds the chrome and hides the panel until it is opened.</summary>
    /// <param name="hostPath">The path to the <see cref="SimHost"/> node, relative to the panel.</param>
    public void Ready(NodePath hostPath)
    {
        Place();
        _host = _root.GetNodeOrNull<SimHost>(hostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] panel: no SimHost at '{hostPath}'; the panel stays empty.");
            return;
        }

        BuildChrome();
        _host.EventsDrained += OnEvents;
        _root.Visible = false;
    }

    /// <summary>Unsubscribes from the host.</summary>
    public void Exit()
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

    /// <summary>Refreshes the view-model when a simulation is running.</summary>
    /// <param name="delta">The real time since the last frame, in seconds.</param>
    public void Process(double delta)
    {
        if (_host is not { IsRunning: true } host || _model is null && !TryBuildModel(host))
        {
            return;
        }

        if (!ReferenceEquals(_builtFor, host.Content))
        {
            RebuildModel(host);
        }

        _model?.Advance(delta);
    }

    /// <summary>Opens and closes the panel and closes it on cancel.</summary>
    /// <param name="event">The input event.</param>
    public void HandleInput(InputEvent @event)
    {
        if (@event.IsEcho() || _host is null)
        {
            return;
        }

        if (InputMap.HasAction(_toggleAction) && @event.IsActionPressed(_toggleAction))
        {
            Toggle();
            _root.GetViewport().SetInputAsHandled();
        }
        else if (_root.Visible && InputMap.HasAction("tool_cancel") && @event.IsActionPressed("tool_cancel"))
        {
            _root.Visible = false;
            _root.GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the panel if it is closed and closes it if it is open. Opening one panel closes the other.</summary>
    public void Toggle()
    {
        _root.Visible = !_root.Visible;
        if (_root.Visible)
        {
            Opened?.Invoke();
            _model?.ClearNote();
            _model?.MarkDirty();
            _model?.Advance(10.0);
        }
    }

    private void Place()
    {
        PanelTheme.Apply(_root);
        _root.AnchorLeft = 1f;
        _root.AnchorRight = 1f;
        _root.AnchorTop = 0f;
        _root.AnchorBottom = 1f;
        _root.OffsetLeft = -PanelWidth;
        _root.OffsetRight = -PanelMargin;
        _root.OffsetTop = PanelTop;
        _root.OffsetBottom = -PanelMargin;
        _root.MouseFilter = Control.MouseFilterEnum.Stop;
    }

    private bool TryBuildModel(SimHost host)
    {
        RebuildModel(host);
        return _model is not null;
    }

    private void RebuildModel(SimHost host)
    {
        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }

        _strings = new UiStrings(host.Locale);
        _builtFor = host.Content;
        _model = _makeModel(host);
        _model.Refreshed += OnRefreshed;
        _model.MarkDirty();
        Retitle();
        _rows.Clear();
        _widgets.Clear();
        foreach (VBoxContainer box in _groupBoxes.Values)
        {
            foreach (Node child in box.GetChildren())
            {
                box.RemoveChild(child);
                child.QueueFree();
            }
        }
    }

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
        _root.AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        margin.AddChild(top);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        var title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(title);
        _title = title;

        var close = new Button { FocusMode = Control.FocusModeEnum.All };
        close.Pressed += () => _root.Visible = false;
        header.AddChild(close);
        _close = close;

        _subtitle = new Label();
        Small(_subtitle);
        top.AddChild(_subtitle);

        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        Small(_note);
        top.AddChild(_note);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        top.AddChild(_scroll);

        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(_list);

        foreach (ProgressGroup group in new[] { ProgressGroup.Target, ProgressGroup.Queue, ProgressGroup.Available, ProgressGroup.Locked })
        {
            var headerLabel = new Label();
            _list.AddChild(headerLabel);
            _groupHeaders[group] = headerLabel;

            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            box.AddThemeConstantOverride("separation", 6);
            _list.AddChild(box);
            _groupBoxes[group] = box;
        }

        _researchedToggle = new Button { FocusMode = Control.FocusModeEnum.All };
        _researchedToggle.Pressed += () =>
        {
            _researchedOpen = !_researchedOpen;
            UpdateAll();
        };
        _list.AddChild(_researchedToggle);

        _researchedBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
        _researchedBox.AddThemeConstantOverride("separation", 6);
        _list.AddChild(_researchedBox);
        _groupBoxes[ProgressGroup.Researched] = _researchedBox;

        _teaser = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Small(_teaser);
        _list.AddChild(_teaser);
    }

    private void Retitle()
    {
        if (_strings is null || _title is null || _close is null)
        {
            return;
        }

        _title.Text = _strings.Get(_titleKey);
        _close.Text = _strings.Get("ui.panel.close");
        _close.TooltipText = _strings.Get("ui.panel.close.tip");
        foreach ((ProgressGroup group, Label header) in _groupHeaders)
        {
            header.Text = _strings.Get($"ui.tree.group.{group.ToString().ToLowerInvariant()}");
        }
    }

    private void UpdateAll()
    {
        if (_model is null || _strings is null || _list is null)
        {
            return;
        }

        UiStrings strings = _strings;
        TreeSnapshot<TId> snapshot = _model.Snapshot;

        var wanted = new HashSet<TId>();
        foreach (NodeRow<TId> row in snapshot.Rows)
        {
            wanted.Add(row.Id);
        }

        var gone = new List<TId>();
        foreach (TId id in _rows.Keys)
        {
            if (!wanted.Contains(id))
            {
                gone.Add(id);
            }
        }

        foreach (TId id in gone)
        {
            _rows[id].QueueFree();
            _rows.Remove(id);
            _widgets.Remove(id);
        }

        foreach (NodeRow<TId> row in snapshot.Rows)
        {
            if (!_rows.ContainsKey(row.Id))
            {
                CreateRow(row);
            }
        }

        var counts = new Dictionary<ProgressGroup, int>();
        foreach (NodeRow<TId> row in snapshot.Rows)
        {
            UpdateRow(row);
            TreeRow control = _rows[row.Id];
            VBoxContainer box = _groupBoxes[row.Group];
            if (!ReferenceEquals(control.GetParent(), box))
            {
                control.Reparent(box, false);
            }

            counts.TryGetValue(row.Group, out int used);
            box.MoveChild(control, used);
            counts[row.Group] = used + 1;
        }

        foreach ((ProgressGroup group, Label header) in _groupHeaders)
        {
            header.Visible = counts.TryGetValue(group, out int used) && used > 0;
        }

        int researched = counts.TryGetValue(ProgressGroup.Researched, out int done) ? done : 0;
        if (_researchedToggle is not null && _researchedBox is not null)
        {
            _researchedToggle.Visible = researched > 0;
            _researchedToggle.Text = (_researchedOpen ? "v " : "> ") +
                strings.Format("ui.tree.researched.toggle", ("count", NumberFormat.Count(researched)));
            _researchedBox.Visible = _researchedOpen && researched > 0;
        }

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

        if (_subtitle is not null)
        {
            _subtitle.Text = strings.Format(
                "ui.tree.subtitle",
                ("done", NumberFormat.Count(snapshot.DoneCount)),
                ("total", NumberFormat.Count(snapshot.TotalCount)));
        }

        if (_note is not null)
        {
            _note.Visible = _model.Note is not null;
            _note.Text = _model.Note ?? string.Empty;
        }
    }

    private void CreateRow(NodeRow<TId> row)
    {
        TId id = row.Id;
        var control = new TreeRow { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        control.DropReceived += (from, onto) =>
        {
            TreeViewModel<TId>? model = _model;
            if (model is null)
            {
                return;
            }

            foreach (NodeRow<TId> candidate in model.Snapshot.Rows)
            {
                if (candidate.QueueIndex == from)
                {
                    Send(model.MoveToCommand(candidate.Id, onto.QueuePosition));
                    return;
                }
            }
        };

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 2);
        info.MouseFilter = Control.MouseFilterEnum.Ignore;
        control.AddChild(info);

        var nameLine = new HBoxContainer();
        nameLine.AddThemeConstantOverride("separation", 8);
        nameLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(nameLine);

        var queuePos = new Label();
        queuePos.MouseFilter = Control.MouseFilterEnum.Ignore;
        Small(queuePos);
        nameLine.AddChild(queuePos);

        var main = new Button
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All,
            Alignment = HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
        };
        main.Pressed += () =>
        {
            if (_model is not null)
            {
                Send(_model.TargetCommand(id));
            }
        };
        main.GuiInput += @event => OnRowInput(@event, id, main);
        main.FocusEntered += () => _scroll?.EnsureControlVisible(control);
        nameLine.AddChild(main);

        var numbers = new HBoxContainer();
        numbers.AddThemeConstantOverride("separation", 8);
        numbers.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(numbers);

        var full = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        full.AddThemeColorOverride("default_color", _muted);
        full.AddThemeFontSizeOverride("normal_font_size", 16);
        numbers.AddChild(full);

        var cost = new Label();
        cost.MouseFilter = Control.MouseFilterEnum.Ignore;
        Small(cost);
        numbers.AddChild(cost);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1000,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(96, 14),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        numbers.AddChild(bar);

        var progress = new Label();
        progress.MouseFilter = Control.MouseFilterEnum.Ignore;
        Small(progress);
        numbers.AddChild(progress);

        var eta = new Label();
        eta.MouseFilter = Control.MouseFilterEnum.Ignore;
        eta.AddThemeColorOverride("font_color", _muted);
        Small(eta);
        numbers.AddChild(eta);

        var inspirationLine = new HBoxContainer();
        inspirationLine.AddThemeConstantOverride("separation", 8);
        inspirationLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(inspirationLine);

        var inspiration = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        inspiration.AddThemeColorOverride("font_color", _muted);
        Small(inspiration);
        inspirationLine.AddChild(inspiration);

        var badge = new Label();
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.AddThemeColorOverride("font_color", _sea);
        Small(badge);
        inspirationLine.AddChild(badge);

        var eraLine = new HBoxContainer();
        eraLine.AddThemeConstantOverride("separation", 8);
        eraLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(eraLine);

        var era = new Label();
        era.MouseFilter = Control.MouseFilterEnum.Ignore;
        era.AddThemeColorOverride("font_color", _muted);
        Small(era);
        eraLine.AddChild(era);

        var missing = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Small(missing);
        eraLine.AddChild(missing);

        var actions = new VBoxContainer();
        actions.AddThemeConstantOverride("separation", 4);
        control.AddChild(actions);

        var queue = new Button { FocusMode = Control.FocusModeEnum.All };
        queue.AddThemeFontSizeOverride("font_size", 16);
        queue.Pressed += () =>
        {
            if (_model is not null)
            {
                Send(_model.AppendCommand(id));
            }
        };
        actions.AddChild(queue);

        var up = new Button { FocusMode = Control.FocusModeEnum.All };
        up.AddThemeFontSizeOverride("font_size", 16);
        up.Pressed += () =>
        {
            if (_model is not null)
            {
                Send(_model.MoveCommand(id, -1));
            }
        };
        actions.AddChild(up);

        var down = new Button { FocusMode = Control.FocusModeEnum.All };
        down.AddThemeFontSizeOverride("font_size", 16);
        down.Pressed += () =>
        {
            if (_model is not null)
            {
                Send(_model.MoveCommand(id, 1));
            }
        };
        actions.AddChild(down);

        _rows[id] = control;
        _widgets[id] = new RowWidgets(control, main, queuePos, era, full, cost, bar, progress, eta, inspiration, badge, missing, queue, up, down);
        _groupBoxes[row.Group].AddChild(control);
    }

    private void UpdateRow(NodeRow<TId> row)
    {
        UiStrings strings = _strings!;
        RowWidgets widgets = _widgets[row.Id];
        TreeRow control = widgets.Root;

        control.RowKey = row.Name;
        control.QueuePosition = row.QueueIndex;
        widgets.Main.Text = row.Name;
        widgets.Main.Disabled = row.Group == ProgressGroup.Researched;
        widgets.QueuePos.Visible = row.Group == ProgressGroup.Queue;
        widgets.QueuePos.Text = strings.Format(
            "ui.tree.row.queue_position", ("position", NumberFormat.Count(row.QueueIndex + 1)));
        widgets.Era.Text = row.EraName;
        widgets.Full.Visible = row.Discounted;
        if (row.Discounted)
        {
            widgets.Full.Text = strings.Format(
                "ui.tree.row.cost.inspired", ("full", row.FullCostText), ("cost", row.CostText));
        }

        widgets.Cost.Text = strings.Format("ui.tree.row.cost", ("cost", row.CostText));
        widgets.Bar.Value = row.Fraction * 1000;
        widgets.Progress.Text = row.ProgressText;
        widgets.Eta.Text = row.EtaText;
        widgets.Inspiration.Text = row.InspirationText;
        widgets.Badge.Visible = row.Inspired;
        widgets.Badge.Text = strings.Get("ui.tree.row.inspired");
        widgets.Missing.Visible = row.MissingText.Length > 0;
        widgets.Missing.Text = row.MissingText;
        widgets.Queue.Visible = row.Group != ProgressGroup.Researched;
        widgets.Queue.Text = strings.Get("ui.tree.row.append");
        widgets.Queue.TooltipText = strings.Get("ui.tree.row.append.tip");
        widgets.Up.Visible = row.Group == ProgressGroup.Queue;
        widgets.Up.Text = strings.Get("ui.tree.row.move_up");
        widgets.Up.TooltipText = strings.Get("ui.tree.row.move_up.tip");
        widgets.Down.Visible = row.Group == ProgressGroup.Queue;
        widgets.Down.Text = strings.Get("ui.tree.row.move_down");
        widgets.Down.TooltipText = strings.Get("ui.tree.row.move_down.tip");

        string tip = strings.Get(row.Group == ProgressGroup.Queue ? "ui.tree.row.tip.queued" : "ui.tree.row.tip");
        widgets.Main.TooltipText = row.MissingText.Length > 0 ? row.MissingText + "\n" + tip : tip;
        if (row.Group == ProgressGroup.Researched)
        {
            control.TooltipText = row.InspirationText;
        }
    }

    private static void Small(Label label) => label.AddThemeFontSizeOverride("font_size", 16);

    private void OnRowInput(InputEvent @event, TId id, Button main)
    {
        TreeViewModel<TId>? model = _model;
        if (model is null)
        {
            return;
        }

        // Mouse only: click sets the target, Shift-click appends to the queue. Keyboard and gamepad confirmation
        // arrives through the button's Pressed signal, and queued rows have their own Earlier, Later and Queue
        // buttons, so arrow keys and the left stick keep moving focus instead of reordering.
        if (@event is InputEventMouseButton pressed && pressed.Pressed && pressed.ButtonIndex == MouseButton.Left)
        {
            Send(Input.IsKeyPressed(Key.Shift) ? model.AppendCommand(id) : model.TargetCommand(id));
            main.AcceptEvent();
        }
    }

    private static readonly Color _muted = new(0.37f, 0.42f, 0.46f);
    private static readonly Color _sea = new(0.12f, 0.35f, 0.47f);

    private sealed class RowWidgets
    {
        public RowWidgets(
            TreeRow root,
            Button main,
            Label queuePos,
            Label era,
            RichTextLabel full,
            Label cost,
            ProgressBar bar,
            Label progress,
            Label eta,
            Label inspiration,
            Label badge,
            Label missing,
            Button queue,
            Button up,
            Button down)
        {
            Root = root;
            Main = main;
            QueuePos = queuePos;
            Era = era;
            Full = full;
            Cost = cost;
            Bar = bar;
            Progress = progress;
            Eta = eta;
            Inspiration = inspiration;
            Badge = badge;
            Missing = missing;
            Queue = queue;
            Up = up;
            Down = down;
        }

        public TreeRow Root { get; }
        public Button Main { get; }
        public Label QueuePos { get; }
        public Label Era { get; }
        public RichTextLabel Full { get; }
        public Label Cost { get; }
        public ProgressBar Bar { get; }
        public Label Progress { get; }
        public Label Eta { get; }
        public Label Inspiration { get; }
        public Label Badge { get; }
        public Label Missing { get; }
        public Button Queue { get; }
        public Button Up { get; }
        public Button Down { get; }
    }
}
