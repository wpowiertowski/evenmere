using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Controls;
using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Hud;
using Evenmere.Game.Ui.Inspector;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Tools;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Ui.BuildMenu;

/// <summary>
/// The categorised build menu (M1-23): what each building costs, needs and does, including what is still locked
/// and why. It is docked at the bottom of the screen, toggled by <c>ui_build_menu</c> (B, or Y on a gamepad) and
/// closed by <c>tool_cancel</c>. Selecting an unlocked entry starts the matching tool on
/// <see cref="ToolController"/> (<c>BeginBuild</c>, <c>BeginRoad</c> for roads, <c>BeginDemolish</c> for the fixed
/// demolish entry) and closes the menu. Locked entries stay focusable so their "Unlocked by …" line is readable,
/// and clicking one does nothing.
/// </summary>
/// <remarks>
/// <para>
/// Input: B/Y toggles, LB and RB (the camera's rotate actions, consumed here while the menu is open so the camera
/// never turns under the menu) switch tabs, the d-pad moves focus through Godot's built-in <c>ui_*</c> actions,
/// and A selects through the built-in <c>ui_accept</c> (which carries the gamepad A button, so the focused
/// entry, tab or close button fires by itself). Keys 1-9 pick the nth entry of the current tab. The 1-3 speed
/// hotkeys are suspended while the menu is open (see <see cref="HudRoot.SpeedHotkeysSuspended"/>), so the number
/// keys never change the speed.
/// </para>
/// <para>
/// Exclusivity: opening the menu hides the research, civics and inspector panels, and the menu closes itself if
/// any of them becomes visible (for example through the HUD-adjacent toggles, which this task may not edit).
/// There is no shared exclusivity hook between the panels, so this polling is the whole mechanism.
/// </para>
/// </remarks>
public sealed partial class BuildMenu : PanelContainer
{
    private const float MenuHeight = 400f;
    private const float SideMargin = 16f;
    private const float BottomMargin = 16f;
    private const float CardMinWidth = 300f;
    private const float SwatchSize = 52f;
    private const int IconFontSize = 30;
    private const int SecondaryFontSize = 16;
    private const float DimmedAlpha = 0.55f;

    private static readonly Color _lockColour = new(0.72f, 0.52f, 0.2f);
    private static readonly Color _fallbackSwatch = new(0.54f, 0.54f, 0.54f);
    private SimHost? _host;
    private ToolController? _tools;
    private Control? _research;
    private Control? _culture;
    private InspectorPanel? _inspector;
    private ContentDb? _builtFor;
    private BuildMenuViewModel? _model;
    private UiStrings? _strings;
    private ButtonGroup? _tabGroup;
    private DebugScript? _debug;

    private Label? _title;
    private Button? _closeButton;
    private ScrollContainer? _scroll;
    private readonly Button[] _tabButtons = new Button[BuildCategory.TabCount];
    private readonly HBoxContainer[] _tabBoxes = new HBoxContainer[BuildCategory.TabCount];
    private readonly Dictionary<string, EntryRow> _rows = new(StringComparer.Ordinal);
    private int _tab = (int)BuildTab.Production;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the <see cref="ToolController"/> node, relative to this node.</summary>
    [Export]
    public NodePath ToolsPath { get; set; } = new("../Tools");

    /// <summary>Gets or sets the path to the research panel, hidden when this menu opens.</summary>
    [Export]
    public NodePath ResearchPath { get; set; } = new("../ResearchPanel");

    /// <summary>Gets or sets the path to the civics panel, hidden when this menu opens.</summary>
    [Export]
    public NodePath CulturePath { get; set; } = new("../CulturePanel");

    /// <summary>Gets or sets the path to the inspector panel, closed when this menu opens and closing this menu when it opens.</summary>
    [Export]
    public NodePath InspectorPath { get; set; } = new("../InspectorPanel");

    /// <summary>Gets a value indicating whether the menu is open.</summary>
    public bool IsOpen => Visible;

    /// <summary>Gets the currently selected tab, for the debug nav log.</summary>
    public BuildTab Tab => (BuildTab)_tab;

    /// <summary>Opens the menu if it is closed and closes it if it is open.</summary>
    public void Toggle()
    {
        if (Visible)
        {
            HideMenu();
        }
        else
        {
            ShowMenu();
        }
    }

    /// <summary>
    /// Opens the menu: cancels the active tool, hides the research and civics panels, refreshes at once and moves
    /// gamepad focus onto the first entry of the current tab.
    /// </summary>
    public void ShowMenu()
    {
        if (_host is not { IsRunning: true } || _model is null)
        {
            return;
        }

        _tools?.Cancel();
        if (_research is not null)
        {
            _research.Visible = false;
        }

        if (_culture is not null)
        {
            _culture.Visible = false;
        }

        _inspector?.Close();

        Visible = true;
        HudRoot.SpeedHotkeysSuspended = true;
        _model.MarkDirty();
        _model.Advance(10.0);
        UpdateAll();
        FocusFirst();
    }

    /// <summary>Closes the menu and gives the speed hotkeys back.</summary>
    public void HideMenu()
    {
        Visible = false;
        HudRoot.SpeedHotkeysSuspended = false;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        _host = GetNodeOrNull<SimHost>(HostPath);
        _tools = GetNodeOrNull<ToolController>(ToolsPath);
        _research = GetNodeOrNull<Control>(ResearchPath);
        _culture = GetNodeOrNull<Control>(CulturePath);
        _inspector = GetNodeOrNull<InspectorPanel>(InspectorPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] build menu: no SimHost at '{HostPath}'; the menu stays empty.");
        }

        if (_tools is null)
        {
            GD.PushError($"[Evenmere] build menu: no ToolController at '{ToolsPath}'; entries cannot place.");
        }

        BuildChrome();
        if (_host is not null)
        {
            _host.EventsDrained += OnEvents;
        }

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

        if (!ReferenceEquals(_builtFor, host.Content))
        {
            RebuildModel(host);
        }

        _model?.Advance(delta);

        // The research, civics and inspector panels only know about each other (and the inspector only about the
        // trees); if any opened, the menu yields. This is the other half of the exclusivity with ShowMenu.
        if (Visible && ((_research?.Visible ?? false) || (_culture?.Visible ?? false) || (_inspector?.Visible ?? false)))
        {
            HideMenu();
        }

        _debug?.Tick(this);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event.IsEcho())
        {
            return;
        }

        if (InputMap.HasAction(InputActions.UiBuildMenu) && @event.IsActionPressed(InputActions.UiBuildMenu))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!Visible)
        {
            return;
        }

        if (InputMap.HasAction(InputActions.ToolCancel) && @event.IsActionPressed(InputActions.ToolCancel))
        {
            HideMenu();
            GetViewport().SetInputAsHandled();
            return;
        }

        // LB and RB (and Q and E on keyboard) switch tabs while the menu is open. The camera turns on the same
        // actions, but it listens in _UnhandledInput, so marking them handled here keeps the view still.
        if (@event.IsActionPressed(InputActions.CameraRotateLeft))
        {
            SetTab((_tab + BuildCategory.TabCount - 1) % BuildCategory.TabCount, true);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(InputActions.CameraRotateRight))
        {
            SetTab((_tab + 1) % BuildCategory.TabCount, true);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Keyboard mnemonics 1-9 pick the nth entry of the current tab. The speed hotkeys 1-3 are suspended while
        // the menu is open (HudRoot.SpeedHotkeysSuspended), so they never fire as well.
        if (@event is InputEventKey key && key.Pressed && !key.Echo
            && key.PhysicalKeycode is >= Key.Key1 and <= Key.Key9)
        {
            int index = (int)key.PhysicalKeycode - (int)Key.Key1;
            if (Pick(index))
            {
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>Selects the nth entry of the current tab, if there is one.</summary>
    /// <param name="index">The zero-based entry index.</param>
    /// <returns><see langword="true"/> when an entry was selected (locked ones count: they swallow the key).</returns>
    public bool Pick(int index)
    {
        if (_model is null || _tab < 0 || _tab >= _model.Tabs.Length)
        {
            return false;
        }

        ImmutableArray<BuildMenuViewModel.BuildEntry> entries = _model.Tabs[_tab].Entries;
        if (index < 0 || index >= entries.Length)
        {
            return false;
        }

        Select(entries[index]);
        return true;
    }

    private void OnEvents(IReadOnlyList<SimEvent> events) => _model?.MarkDirty();

    private void OnRefreshed() => UpdateAll();

    private void Place()
    {
        PanelTheme.Apply(this);
        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = SideMargin;
        OffsetRight = -SideMargin;
        OffsetTop = -MenuHeight;
        OffsetBottom = -BottomMargin;
        MouseFilter = Control.MouseFilterEnum.Stop;
    }

    private void RebuildModel(SimHost host)
    {
        if (_model is not null)
        {
            _model.Refreshed -= OnRefreshed;
        }

        _strings = new UiStrings(host.Locale);
        _builtFor = host.Content;
        Dictionary<string, BuildMenuViewModel.BuildingVisualRef> visuals = LoadVisuals(host.Content);
        _model = new BuildMenuViewModel(host.Content, _strings, host.Query.Progression, host.Query.Workforce, visuals, UnlockIndex.Build(host.Content));
        _model.Refreshed += OnRefreshed;
        _model.MarkDirty();
        _model.Advance(10.0);

        foreach (EntryRow row in _rows.Values)
        {
            row.Root.QueueFree();
        }

        _rows.Clear();
        Retitle();
        UpdateAll();
    }

    private static Dictionary<string, BuildMenuViewModel.BuildingVisualRef> LoadVisuals(ContentDb content)
    {
        var visuals = new Dictionary<string, BuildMenuViewModel.BuildingVisualRef>(StringComparer.Ordinal);
        string json = Godot.FileAccess.GetFileAsString("res://data/visuals/era1-slice.json");
        if (string.IsNullOrEmpty(json))
        {
            GD.PushWarning("[Evenmere] build menu: no placeholder mapping; entries fall back to grey swatches.");
            return visuals;
        }

        var map = BuildingVisualMap.Load(json, static text => GD.PushWarning($"[Evenmere] build menu: {text}"));
        foreach (string name in content.Buildings.Names)
        {
            if (map.TryGet(name, out BuildingVisual visual))
            {
                visuals[name] = new BuildMenuViewModel.BuildingVisualRef(visual.Icon, visual.Shape.ToString(), visual.Colour);
            }
        }

        return visuals;
    }

    private void Retitle()
    {
        if (_strings is null || _title is null || _closeButton is null)
        {
            return;
        }

        _title.Text = _strings.Get("ui.build.title");
        _closeButton.Text = _strings.Get("ui.build.close");
        _closeButton.TooltipText = _strings.Get("ui.build.close.tip");
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            _tabButtons[i].Text = _model?.Tabs[i].Title ?? string.Empty;
        }
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

        _title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        header.AddChild(_title);

        _closeButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _closeButton.Pressed += HideMenu;
        header.AddChild(_closeButton);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        top.AddChild(tabs);

        _tabGroup = new ButtonGroup();
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            int tab = i;
            var button = new Button
            {
                Name = $"BuildTab_{tab}",
                ToggleMode = true,
                ButtonGroup = _tabGroup,
                FocusMode = Control.FocusModeEnum.All,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            button.Pressed += () => SetTab(tab, true);
            tabs.AddChild(button);
            _tabButtons[i] = button;
        }

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        top.AddChild(_scroll);

        var pages = new HBoxContainer();
        pages.AddThemeConstantOverride("separation", 12);
        pages.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pages.MouseFilter = Control.MouseFilterEnum.Ignore;
        _scroll.AddChild(pages);

        for (int i = 0; i < _tabBoxes.Length; i++)
        {
            var box = new HBoxContainer();
            box.AddThemeConstantOverride("separation", 12);
            box.MouseFilter = Control.MouseFilterEnum.Ignore;
            box.Visible = i == _tab;
            pages.AddChild(box);
            _tabBoxes[i] = box;
        }
    }

    private void UpdateAll()
    {
        if (_model is null || _strings is null)
        {
            return;
        }

        UiStrings strings = _strings;
        for (int t = 0; t < _model.Tabs.Length && t < _tabBoxes.Length; t++)
        {
            ImmutableArray<BuildMenuViewModel.BuildEntry> entries = _model.Tabs[t].Entries;
            HBoxContainer box = _tabBoxes[t];
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (BuildMenuViewModel.BuildEntry entry in entries)
            {
                wanted.Add(entry.Key);
                if (!_rows.TryGetValue(entry.Key, out EntryRow? row))
                {
                    row = CreateRow(entry);
                    _rows[entry.Key] = row;
                    box.AddChild(row.Root);
                }
                else if (!ReferenceEquals(row.Root.GetParent(), box))
                {
                    row.Root.Reparent(box, false);
                }

                UpdateRow(row, entry, strings, entries);
            }

            var gone = new List<string>();
            foreach (string key in _rows.Keys)
            {
                if (IsMine(key, t) && !wanted.Contains(key))
                {
                    gone.Add(key);
                }
            }

            foreach (string key in gone)
            {
                _rows[key].Root.QueueFree();
                _rows.Remove(key);
            }

            for (int i = 0; i < entries.Length; i++)
            {
                box.MoveChild(_rows[entries[i].Key].Root, i);
            }
        }

        Retitle();
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            _tabButtons[i].SetPressedNoSignal(i == _tab);
        }
    }

    // Rows are keyed by content building ID (plus "demolish"), and each building lives on exactly one tab, so a key
    // found under another tab's box belongs to that tab. The tab of a row is whatever box holds it.
    private bool IsMine(string key, int tab)
    {
        if (!_rows.TryGetValue(key, out EntryRow? row))
        {
            return false;
        }

        return ReferenceEquals(row.Root.GetParent(), _tabBoxes[tab]);
    }

    private EntryRow CreateRow(BuildMenuViewModel.BuildEntry entry)
    {
        // One card: a PanelContainer draws the theme background around the whole content, and a transparent overlay
        // button on top carries the focus ring and the clicks. A Button cannot be the card root: it is not a
        // Container, so wrapped labels spill outside its rect and its background only covers the first line.
        var root = new PanelContainer
        {
            Name = $"BuildEntry_{entry.Key}",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(CardMinWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        line.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(line);

        var swatch = new PanelContainer
        {
            CustomMinimumSize = new Vector2(SwatchSize, SwatchSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        line.AddChild(swatch);

        var icon = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        icon.AddThemeFontSizeOverride("font_size", IconFontSize);
        swatch.AddChild(icon);

        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 2);
        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        info.MouseFilter = Control.MouseFilterEnum.Ignore;
        line.AddChild(info);

        var nameLine = new HBoxContainer();
        nameLine.AddThemeConstantOverride("separation", 8);
        nameLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(nameLine);

        var name = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        nameLine.AddChild(name);

        var hotkey = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        Small(hotkey);
        nameLine.AddChild(hotkey);

        var cost = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        Small(cost);
        info.AddChild(cost);

        var workforce = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        Small(workforce);
        info.AddChild(workforce);

        var detail = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Small(detail);
        info.AddChild(detail);

        var desc = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        Small(desc);
        info.AddChild(desc);

        var lockLine = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        lockLine.AddThemeColorOverride("font_color", _lockColour);
        Small(lockLine);
        info.AddChild(lockLine);

        var overlay = new Button
        {
            Name = $"BuildEntry_{entry.Key}_Button",
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        overlay.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        overlay.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        overlay.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        overlay.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
        root.AddChild(overlay);

        var row = new EntryRow(root, overlay, swatch, icon, name, hotkey, cost, workforce, detail, desc, lockLine);
        overlay.Pressed += () => Select(row.Entry);
        overlay.FocusEntered += () => _scroll?.EnsureControlVisible(root);
        return row;
    }

    private static void UpdateRow(
        EntryRow row,
        BuildMenuViewModel.BuildEntry entry,
        UiStrings strings,
        ImmutableArray<BuildMenuViewModel.BuildEntry> siblings)
    {
        row.Entry = entry;
        row.Name.Text = entry.Name;
        row.Cost.Text = entry.CostText;
        row.Workforce.Visible = entry.WorkforceText is not null;
        row.Workforce.Text = entry.WorkforceText ?? string.Empty;
        if (entry.WorkforceShort)
        {
            row.Workforce.AddThemeColorOverride("font_color", Hud.HudChip.WarningTint);
        }
        else
        {
            row.Workforce.RemoveThemeColorOverride("font_color");
        }
        row.Detail.Text = entry.DetailText;
        row.Desc.Visible = entry.Description.Length > 0;
        row.Desc.Text = entry.Description;
        row.Lock.Visible = entry.Locked;
        row.Lock.Text = entry.LockText;
        row.Icon.Text = entry.Icon;
        row.Swatch.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = SwatchColour(entry.ColourHex) });
        row.Root.Modulate = entry.Locked ? new Color(1f, 1f, 1f, DimmedAlpha) : new Color(1f, 1f, 1f, 1f);

        int position = -1;
        for (int i = 0; i < siblings.Length; i++)
        {
            if (string.Equals(siblings[i].Key, entry.Key, StringComparison.Ordinal))
            {
                position = i;
                break;
            }
        }

        // The first nine entries show their keyboard mnemonic; the rest (and locked entries keep theirs, so the
        // row reads the same locked or not) show none.
        row.Hotkey.Visible = position >= 0 && position < 9;
        row.Hotkey.Text = position >= 0 && position < 9 ? (position + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

        if (entry.Locked)
        {
            row.Overlay.TooltipText = TipWithDesc(strings.Get("ui.build.locked.tip"), entry);
        }
        else if (entry.Type is null)
        {
            row.Overlay.TooltipText = TipWithDesc(strings.Get("ui.build.demolish.tip"), entry);
        }
        else
        {
            row.Overlay.TooltipText = TipWithDesc(strings.Format("ui.build.pick.tip", ("key", row.Hotkey.Text)), entry);
        }
    }

    // The tooltip carries the full description (the card shows at most two lines, with an ellipsis when cut)
    // plus the action hint.
    private static string TipWithDesc(string action, BuildMenuViewModel.BuildEntry entry) =>
        entry.Description.Length > 0 ? entry.Description + "\n" + action : action;

    private static Color SwatchColour(string hex)
    {
        if (!string.IsNullOrEmpty(hex) && Color.HtmlIsValid(hex))
        {
            return Color.FromHtml(hex);
        }

        return _fallbackSwatch;
    }

    private static void Small(Label label) => label.AddThemeFontSizeOverride("font_size", SecondaryFontSize);

    private void SetTab(int tab, bool focusFirst)
    {
        _tab = tab;
        for (int i = 0; i < _tabBoxes.Length; i++)
        {
            _tabBoxes[i].Visible = i == tab;
        }

        for (int i = 0; i < _tabButtons.Length; i++)
        {
            _tabButtons[i].SetPressedNoSignal(i == tab);
        }

        if (focusFirst && Visible)
        {
            FocusFirst();
        }
    }

    private void FocusFirst()
    {
        if (_model is null || _tab < 0 || _tab >= _model.Tabs.Length)
        {
            return;
        }

        ImmutableArray<BuildMenuViewModel.BuildEntry> entries = _model.Tabs[_tab].Entries;
        if (entries.Length > 0 && _rows.TryGetValue(entries[0].Key, out EntryRow? row))
        {
            row.Overlay.GrabFocus();
        }
    }

    private void Select(BuildMenuViewModel.BuildEntry entry)
    {
        // A locked entry does nothing when clicked, but it stays focusable so its reason stays readable.
        if (entry.Locked || _tools is null)
        {
            GD.Print($"[Evenmere] BuildMenu: select {entry.Key} ignored (locked={entry.Locked}).");
            return;
        }

        if (entry.Type is null)
        {
            _tools.BeginDemolish();
        }
        else if (entry.IsRoad)
        {
            _tools.BeginRoad(entry.Type.Value);
        }
        else
        {
            _tools.BeginBuild(entry.Type.Value);
        }

        GD.Print($"[Evenmere] BuildMenu: select {entry.Key} tool={_tools.Mode}.");
        HideMenu();
    }

    // Scripted screenshots and the gamepad proof (debug builds only): --build-menu=show opens the menu once the
    // world is running, --build-menu-tab and --build-menu-focus select and focus for stills, --build-menu-pick
    // selects an entry (proving the tool starts), and --build-menu-nav steps simulated d-pad input through the
    // same ui_* actions the pad produces, logging the focus path and the speed after every step.
    private sealed class DebugScript
    {
        private readonly int? _tab;
        private readonly int? _focus;
        private readonly int? _pick;
        private readonly List<string> _nav;
        private readonly bool _show;
        private int _stage;
        private int _navStep;
        private int _navPhase;
        private int _navWait;
        private bool _done;
        private int _doneFrames;
        private SimSpeed _navSpeed;
        private bool _navSpeedTaken;

        private DebugScript(bool show, int? tab, int? focus, int? pick, List<string> nav)
        {
            _show = show;
            _tab = tab;
            _focus = focus;
            _pick = pick;
            _nav = nav;
        }

        public static DebugScript? Parse()
        {
            if (!OS.IsDebugBuild())
            {
                return null;
            }

            bool show = false;
            int? tab = null;
            int? focus = null;
            int? pick = null;
            var nav = new List<string>();
            bool seen = false;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--build-menu=show")
                {
                    show = true;
                    seen = true;
                }
                else if (ValueOf(arg, "--build-menu-tab=", out int tabValue))
                {
                    tab = tabValue;
                    seen = true;
                }
                else if (ValueOf(arg, "--build-menu-focus=", out int focusValue))
                {
                    focus = focusValue;
                    seen = true;
                }
                else if (ValueOf(arg, "--build-menu-pick=", out int pickValue))
                {
                    pick = pickValue;
                    seen = true;
                }
                else if (arg.StartsWith("--build-menu-nav=", StringComparison.Ordinal))
                {
                    foreach (string step in arg["--build-menu-nav=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        nav.Add(step.Trim().ToLowerInvariant());
                    }

                    seen = true;
                }
            }

            return seen ? new DebugScript(show, tab, focus, pick, nav) : null;
        }

        public void Tick(BuildMenu menu)
        {
            if (menu._model is null || menu._model.Tabs.Length == 0)
            {
                return;
            }

            switch (_stage)
            {
                case 0:
                    if (_show)
                    {
                        menu.ShowMenu();
                    }

                    _stage++;
                    break;
                case 1:
                    if (_tab.HasValue)
                    {
                        menu.SetTab(Math.Clamp(_tab.Value, 0, BuildCategory.TabCount - 1), false);
                    }

                    _stage++;
                    break;
                case 2:
                    if (_focus.HasValue)
                    {
                        FocusEntry(menu, _focus.Value);
                    }

                    _stage++;
                    break;
                case 3:
                    if (_pick.HasValue)
                    {
                        bool picked = menu.Pick(_pick.Value);
                        GD.Print($"[Evenmere] BuildMenuNav: pick={_pick.Value} ok={picked} tool={menu._tools?.Mode}");
                    }

                    _stage++;
                    break;
                default:
                    TickNav(menu);
                    break;
            }
        }

        // One nav step takes several frames: parsed input sent from _Process reaches the GUI a few frames later,
        // and the shared theme keeps every button on ActionMode release, so a press alone never selects.
        private const int NavSettleFrames = 5;

        private void TickNav(BuildMenu menu)
        {
            if (_navStep >= _nav.Count)
            {
                return;
            }

            if (!_navSpeedTaken)
            {
                _navSpeedTaken = true;
                _navSpeed = menu._host?.Speed ?? SimSpeed.Paused;
                GD.Print($"[Evenmere] BuildMenuNav: start tab={menu.Tab} focus={FocusName(menu)} speed={_navSpeed}");
                if (menu._host is not null)
                {
                    menu._host.EventsDrained += LogRejected;
                }
            }

            string step = _nav[_navStep];
            switch (_navPhase)
            {
                case 0:
                    if (step.StartsWith("focus_research=", StringComparison.Ordinal))
                    {
                        FocusResearch(menu, step["focus_research=".Length..]);
                        _navPhase = 3;
                        _navWait = 0;
                    }
                    else if (step == "inspect")
                    {
                        OpenInspector(menu);
                        _navPhase = 3;
                        _navWait = 0;
                    }
                    else if (step.StartsWith("tick=", StringComparison.Ordinal)
                        && int.TryParse(step["tick=".Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int ticks)
                        && ticks > 0)
                    {
                        // Ticks are normally paced by the frame delta, but a scripted proof must not depend on the
                        // room's frame rate: RunTicksNow advances the sim synchronously (its purpose besides smoke).
                        menu._host?.RunTicksNow(Math.Min(ticks, 1200));
                        GD.Print($"[Evenmere] BuildMenuNav: tick={ticks} now={menu._host?.CurrentTick}.");
                        _navPhase = 3;
                        _navWait = 0;
                    }
                    else if (NavEvent(step, true) is { } press)
                    {
                        Input.ParseInputEvent(press);
                        _navPhase = 1;
                        _navWait = 0;
                    }
                    else
                    {
                        GD.Print($"[Evenmere] BuildMenuNav: unknown step '{step}'; want left, right, up, down, accept, dpad_left, dpad_right, dpad_up, dpad_down, pad_accept, back, focus_hud, research, menu, esc, inspect or focus_research=<tech>.");
                        _navPhase = 3;
                        _navWait = NavSettleFrames;
                    }

                    break;
                case 1:
                    _navWait++;
                    if (_navWait >= NavSettleFrames)
                    {
                        _navPhase = 2;
                    }

                    break;
                case 2:
                    if (NavEvent(step, false) is { } release)
                    {
                        Input.ParseInputEvent(release);
                    }

                    _navPhase = 3;
                    _navWait = 0;
                    break;
                default:
                    _navWait++;
                    if (_navWait < NavSettleFrames)
                    {
                        break;
                    }

                    _navPhase = 0;
                    GD.Print($"[Evenmere] BuildMenuNav: step {step} focus={FocusName(menu)} {StateLine(menu)}");
                    SimSpeed speed = menu._host?.Speed ?? SimSpeed.Paused;
                    if (!speed.Equals(_navSpeed))
                    {
                        GD.Print($"[Evenmere] BuildMenuNav: FAIL: speed changed from {_navSpeed} to {speed}.");
                    }

                    _navStep++;
                    if (_navStep < _nav.Count)
                    {
                        break;
                    }

                    if (!_done)
                    {
                        _done = true;
                        GD.Print(speed.Equals(_navSpeed)
                            ? $"[Evenmere] BuildMenuNav: done, speed unchanged ({speed})."
                            : $"[Evenmere] BuildMenuNav: done, SPEED CHANGED from {_navSpeed} to {speed}.");
                    }

                    break;
            }

            if (_done)
            {
                _doneFrames++;
                if (_doneFrames == 1)
                {
                    GD.Print($"[Evenmere] BuildMenuNav: settling focus={FocusName(menu)} {StateLine(menu)}.");
                }

                // Commands apply at the next tick (a quarter of a game second at 1x), so the settle waits long
                // enough for a target or queue change to show in the queries before quitting.
                if (_doneFrames >= 60)
                {
                    menu.GetTree().Quit(_navSpeed.Equals(menu._host?.Speed ?? SimSpeed.Paused) ? 0 : 1);
                }
            }
        }

        private static void FocusEntry(BuildMenu menu, int index)
        {
            if (menu._model is null || menu._tab < 0 || menu._tab >= menu._model.Tabs.Length)
            {
                return;
            }

            ImmutableArray<BuildMenuViewModel.BuildEntry> entries = menu._model.Tabs[menu._tab].Entries;
            if (index < 0 || index >= entries.Length)
            {
                return;
            }

            if (menu._rows.TryGetValue(entries[index].Key, out EntryRow? row))
            {
                row.Overlay.GrabFocus();
                GD.Print($"[Evenmere] BuildMenuNav: focus={entries[index].Key} locked={entries[index].Locked}");
            }
        }

        private static string FocusName(BuildMenu menu)
        {
            Control? focused = menu.GetViewport()?.GuiGetFocusOwner();
            if (focused is null)
            {
                return "none";
            }

            foreach ((string key, EntryRow row) in menu._rows)
            {
                if (ReferenceEquals(row.Overlay, focused))
                {
                    return row.Entry.Locked ? $"{key} locked" : key;
                }
            }

            return $"{focused.Name} ({focused.GetType().Name})";
        }

        private static InputEvent? NavEvent(string step, bool pressed) => step switch
        {
            // Action-level steps: exactly what the d-pad produces after Godot's built-in ui_* bindings.
            "left" => new InputEventAction { Action = "ui_left", Pressed = pressed },
            "right" => new InputEventAction { Action = "ui_right", Pressed = pressed },
            "up" => new InputEventAction { Action = "ui_up", Pressed = pressed },
            "down" => new InputEventAction { Action = "ui_down", Pressed = pressed },
            "accept" => new InputEventAction { Action = "ui_accept", Pressed = pressed },

            // Device-level steps: physical pad buttons, as the hand sends them. Before M1-23 these also fired
            // speed_1/2/3 (d-pad left/down/right); now only the built-in ui_* focus bindings answer.
            "dpad_left" => new InputEventJoypadButton { ButtonIndex = JoyButton.DpadLeft, Pressed = pressed },
            "dpad_right" => new InputEventJoypadButton { ButtonIndex = JoyButton.DpadRight, Pressed = pressed },
            "dpad_up" => new InputEventJoypadButton { ButtonIndex = JoyButton.DpadUp, Pressed = pressed },
            "dpad_down" => new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = pressed },
            "pad_accept" => new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = pressed },
            // The physical Back and B buttons: ui_focus_hud takes focus into the HUD, and B fires ui_cancel and
            // tool_cancel together (one layer must answer; see the (d) proof).
            "focus_hud" => new InputEventJoypadButton { ButtonIndex = JoyButton.Back, Pressed = pressed },
            "back" => new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = pressed },
            // The research and menu toggles are actions: the same press a keyboard T or B makes.
            "research" => new InputEventAction { Action = "ui_research", Pressed = pressed },
            "menu" => new InputEventAction { Action = InputActions.UiBuildMenu, Pressed = pressed },
            // Escape lives on both ui_cancel and tool_cancel, like the physical key.
            "esc" => new InputEventKey { Keycode = Key.Escape, Pressed = pressed },
            _ => null,
        };

        // Rejections prove a press reached the sim but the node was not available; silence plus no target
        // means the button never fired.
        private static void LogRejected(IReadOnlyList<SimEvent> events)
        {
            foreach (SimEvent @event in events)
            {
                if (@event is CommandRejected rejected)
                {
                    GD.Print($"[Evenmere] BuildMenuNav: rejected {rejected.Command.GetType().Name} code={rejected.Code}.");
                }
            }
        }
        private static string StateLine(BuildMenu menu)
        {
            SimHost? host = menu._host;
            string target = "none";
            if (host is { IsRunning: true } && host.Query.Research.Target is { } targetId)
            {
                target = host.Content.Techs.NameOf(targetId);
            }

            return $"menu={menu.Visible} tool={menu._tools?.Mode} speed={host?.Speed} inspector={menu._inspector?.Visible} target={target} tick={host?.CurrentTick}";
        }

        // Opens the inspector on the first building (the Founders' Hall), for the layer-order proof: the same
        // Open(EntityId) the tools call on click.
        private static void OpenInspector(BuildMenu menu)
        {
            if (menu._inspector is null || menu._host is not { IsRunning: true } host)
            {
                GD.Print("[Evenmere] BuildMenuNav: inspect with no inspector or world.");
                return;
            }

            foreach (BuildingInfo building in host.Query.Map.Buildings)
            {
                menu._inspector.Open(building.Id);
                GD.Print($"[Evenmere] BuildMenuNav: inspect building {building.Id.Value}.");
                return;
            }

            GD.Print("[Evenmere] BuildMenuNav: inspect found no building.");
        }

        private static void FocusResearch(BuildMenu menu, string tech)
        {
            if (menu._research is null || menu._host is not { IsRunning: true } host)
            {
                GD.Print("[Evenmere] BuildMenuNav: focus_research with no research panel or world.");
                return;
            }

            string name = new UiStrings(host.Locale).Get($"tech.{tech}.name");
            if (host.Content.Techs.TryGet(tech, out TechId techId))
            {
                GD.Print($"[Evenmere] BuildMenuNav: available={tech} {host.Query.Research.IsAvailable(techId)} done={host.Query.Research.IsDone(techId)}.");
            }
            foreach (Node node in menu._research.FindChildren("*", "Button", true, false))
            {
                if (node is Button button && button.IsVisibleInTree() && button.Text == name)
                {
                    button.GrabFocus();
                    GD.Print($"[Evenmere] BuildMenuNav: focus_research={tech} disabled={button.Disabled}.");
                    return;
                }
            }

            GD.Print($"[Evenmere] BuildMenuNav: focus_research={tech} found no button named '{name}'.");
        }

        private static bool ValueOf(string arg, string prefix, out int value)
        {
            value = 0;
            if (!arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(
                arg[prefix.Length..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }
    }

    private sealed class EntryRow
    {
        public EntryRow(
            PanelContainer root,
            Button overlay,
            PanelContainer swatch,
            Label icon,
            Label name,
            Label hotkey,
            Label cost,
            Label workforce,
            Label detail,
            Label desc,
            Label lockLine)
        {
            Root = root;
            Overlay = overlay;
            Swatch = swatch;
            Icon = icon;
            Name = name;
            Hotkey = hotkey;
            Cost = cost;
            Workforce = workforce;
            Detail = detail;
            Desc = desc;
            Lock = lockLine;
            Entry = new BuildMenuViewModel.BuildEntry(
                string.Empty, null, false, string.Empty, string.Empty, string.Empty,
                string.Empty, null, false, string.Empty, string.Empty, false, string.Empty);
        }

        public PanelContainer Root { get; }

        public Button Overlay { get; }

        public PanelContainer Swatch { get; }

        public Label Icon { get; }

        public Label Name { get; }

        public Label Hotkey { get; }

        public Label Cost { get; }

        public Label Workforce { get; }

        public Label Detail { get; }

        public Label Desc { get; }

        public Label Lock { get; }

        public BuildMenuViewModel.BuildEntry Entry { get; set; }
    }
}
