using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Explain;
using Evenmere.Game.Ui.Inspector;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Research;
using Godot;

namespace Evenmere.Game.Ui.Notifications;

/// <summary>
/// The calm notification feed (M1-26): toasts for what just happened, a history panel with filters, and the pinned
/// advisor notes. It replaces M1-25's <c>InspirationToast</c>: inspirations arrive here from now on.
/// <para>
/// Mapping: <c>ConstructionCompleted</c> is batched per building type per day, <c>HouseTierAdvanced</c> and
/// <c>HouseTierDeclined</c> per tier per day (declines use gentle wording), techs, civics and inspirations are single
/// entries, <c>AdviceRaised</c> shows the note's title once per advice ID, and <c>AdviceCleared</c> quietly removes the
/// pin with no toast. History is session-only (at most 200 entries, newest first) and never saved.
/// </para>
/// </summary>
public sealed partial class NotificationFeed : Control
{
    private const double ToastSeconds = 6.0;
    private const double FadeSeconds = 1.0;
    private const int MaxToasts = 3;
    private const int MaxHistory = 200;
    private const float ToastWidth = 520f;

    private readonly List<NotificationEntry> _entries = new();
    private readonly Dictionary<string, int> _batches = new();
    private readonly List<ToastView> _toasts = new();
    private readonly Dictionary<int, PanelContainer> _pins = new();
    private readonly HashSet<int> _shownAdvice = new();
    private readonly List<string> _filters = ["all", "building", "people", "progress", "advice"];

    private SimHost? _host;
    private CameraRig? _rig;
    private ContentDb? _content;
    private UiStrings? _strings;
    private long _batchesDay = -1;
    private long _lastTick = -1;
    private int _nextId = 1;
    private int _unread;
    private string _filter = "all";
    private string _pinSignature = string.Empty;
    private bool _historyDirty = true;
    private double _secondsSinceRefresh;

    private VBoxContainer? _toastBox;
    private Button? _feedButton;
    private PanelContainer? _advicePanel;
    private Control? _toggles;

    /// <summary>Gets or sets the path to the research/civics toggle buttons, whose bottom edge this column stacks under.</summary>
    [Export]
    public NodePath TogglesPath { get; set; } = new("../PanelToggles");
    private Button? _adviceHeader;
    private ScrollContainer? _adviceScroll;
    private VBoxContainer? _adviceRows;
    private bool _adviceCollapsed;
    private PanelContainer? _history;
    private Label? _historyTitle;
    private VBoxContainer? _historyRows;
    private Button[] _filterButtons = [];
    private Button? _historyClose;
    private PanelContainer? _goodPopup;
    private ExplanationView? _goodView;
    private Label? _goodTitle;
    private Button? _goodClose;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the <see cref="CameraRig"/> node, relative to this node.</summary>
    [Export]
    public NodePath RigPath { get; set; } = new("../CameraRig");

    /// <summary>Gets or sets the path to the inspector panel, relative to this node.</summary>
    [Export]
    public NodePath InspectorPath { get; set; } = new("../InspectorPanel");

    /// <summary>Gets or sets the path to the research panel, relative to this node.</summary>
    [Export]
    public NodePath ResearchPath { get; set; } = new("../ResearchPanel");

    /// <summary>Gets or sets the path to the civics panel, relative to this node.</summary>
    [Export]
    public NodePath CulturePath { get; set; } = new("../CulturePanel");

    /// <inheritdoc />
    public override void _Ready()
    {
        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = 0f;
        OffsetRight = 0f;
        OffsetTop = 0f;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Ignore;
        PanelTheme.Apply(this);

        _host = GetNodeOrNull<SimHost>(HostPath);
        _rig = GetNodeOrNull<CameraRig>(RigPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] notifications: no SimHost at '{HostPath}'; the feed stays empty.");
            return;
        }

        BuildChrome();
        _host.EventsDrained += OnEvents;
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
        TickToasts(delta);
        LayoutLeftColumn();

        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        EnsureSession(host);

        _secondsSinceRefresh += delta;
        if (_secondsSinceRefresh < ViewModel.MinRefreshIntervalSeconds)
        {
            return;
        }

        _secondsSinceRefresh = 0;
        RefreshPins(host);
        LayoutAdvice();
        if (_history is { Visible: true } && _historyDirty)
        {
            RebuildHistory(host);
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsEcho() || !InputMap.HasAction("tool_cancel") || !@event.IsActionPressed("tool_cancel"))
        {
            return;
        }

        if (_goodPopup is { Visible: true })
        {
            _goodPopup.Visible = false;
            GetViewport().SetInputAsHandled();
        }
        else if (_history is { Visible: true })
        {
            _history.Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the history panel with a filter, for the debug screenshots.</summary>
    /// <param name="filter">One of <c>all</c>, <c>building</c>, <c>people</c>, <c>progress</c> or <c>advice</c>.</param>
    public void DebugShowHistory(string filter)
    {
        if (_history is null || _host is not { IsRunning: true } host)
        {
            return;
        }

        if (_filters.Contains(filter))
        {
            _filter = filter;
        }

        _history.Visible = true;
        _historyDirty = true;
        _unread = 0;
        RefreshFeedButton();
        RebuildHistory(host);
        GD.Print($"[Evenmere] Notifications: debug opened history with filter '{_filter}'.");
    }

    /// <summary>Collapses or expands the pinned-notes panel, for the debug screenshots.</summary>
    /// <param name="collapsed"><see langword="true"/> to show only the header chip.</param>
    public void DebugSetAdviceCollapsed(bool collapsed)
    {
        _adviceCollapsed = collapsed;
        RetitleAdvice();
        LayoutAdvice();
        GD.Print($"[Evenmere] Notifications: debug advice collapsed={collapsed}.");
    }

    /// <summary>Focuses the newest entry that points at a building, for the click evidence log.</summary>
    public void DebugClickNewestBuilding()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Subject.Kind == SubjectKind.Building)
            {
                GD.Print($"[Evenmere] Notifications: debug clicked entry {_entries[i].Id} '{_entries[i].Title}'.");
                FocusEntry(_entries[i]);
                return;
            }
        }

        GD.Print("[Evenmere] Notifications: debug click found no building entry.");
    }

    private void OnEvents(IReadOnlyList<SimEvent> events)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        EnsureSession(host);
        if (_strings is null)
        {
            return;
        }

        foreach (SimEvent @event in events)
        {
            switch (@event)
            {
                case ConstructionCompleted completed:
                    AddCompletion(completed.Tick, completed.Type, completed.Building);
                    break;
                case HouseTierAdvanced advanced:
                    AddTierMove(advanced.Tick, advanced.House, advanced.ToTier, declining: false);
                    break;
                case HouseTierDeclined declined:
                    AddTierMove(declined.Tick, declined.House, declined.ToTier, declining: true);
                    break;
                case TechResearched researched:
                    AddSingle(researched.Tick, NotificationCategory.Progress, TechTitle(researched.Tech), note: null, SubjectLink.None, tech: researched.Tech);
                    break;
                case CivicAdopted adopted:
                    AddSingle(adopted.Tick, NotificationCategory.Progress, CivicTitle(adopted.Civic), note: null, SubjectLink.None, civic: adopted.Civic);
                    break;
                case InspirationTriggered inspiration:
                    AddInspiration(inspiration);
                    break;
                case AdviceRaised raised:
                    AddAdvice(raised);
                    break;
                case AdviceCleared cleared:
                    ClearPin(cleared.AdviceId);
                    break;
            }
        }

        RefreshPins(host);
        if (_history is { Visible: true })
        {
            RebuildHistory(host);
        }
        else
        {
            _historyDirty = true;
        }
    }

    private void AddCompletion(long tick, BuildingTypeId type, EntityId building)
    {
        if (_strings is null)
        {
            return;
        }

        long day = DayOf(tick);
        DropStaleBatches(day);
        string key = $"build:{type.Value}:{day}";
        string name = BuildingDisplayName(type);
        if (_batches.TryGetValue(key, out int id) && FindEntry(id) is { } growing)
        {
            // A batch that grows while visible updates its count in place instead of stacking a new toast.
            NotificationEntry updated = growing with
            {
                Tick = tick,
                Count = growing.Count + 1,
                Title = BatchTitle("ui.notifications.construction", name, growing.Count + 1),
                Subject = SubjectLink.ForBuilding(building),
            };
            ReplaceEntry(updated);
            RefreshToast(updated);
        }
        else
        {
            var entry = new NotificationEntry(_nextId++, tick, NotificationCategory.Building, BatchTitle("ui.notifications.construction", name, 1), Note: null, SubjectLink.ForBuilding(building), null, null, 1);
            PushEntry(entry);
            _batches[key] = entry.Id;
        }
    }

    private void AddTierMove(long tick, EntityId house, TierId tier, bool declining)
    {
        if (_strings is null)
        {
            return;
        }

        long day = DayOf(tick);
        DropStaleBatches(day);
        string key = $"{(declining ? "decline" : "advance")}:{tier.Value}:{day}";
        string name = _strings.TierName(TierNameOf(tier));
        string prefix = declining ? "ui.notifications.decline" : "ui.notifications.advance";
        string? note = declining ? _strings.Get("ui.notifications.decline.note") : null;
        if (_batches.TryGetValue(key, out int id) && FindEntry(id) is { } growing)
        {
            NotificationEntry updated = growing with
            {
                Tick = tick,
                Count = growing.Count + 1,
                Title = BatchTitle(prefix, name, growing.Count + 1),
                Subject = SubjectLink.ForBuilding(house),
            };
            ReplaceEntry(updated);
            RefreshToast(updated);
        }
        else
        {
            var entry = new NotificationEntry(_nextId++, tick, NotificationCategory.People, BatchTitle(prefix, name, 1), Note: note, SubjectLink.ForBuilding(house), null, null, 1);
            PushEntry(entry);
            _batches[key] = entry.Id;
        }
    }

    private void AddSingle(long tick, NotificationCategory category, string title, string? note, SubjectLink subject, TechId? tech = null, CivicId? civic = null)
    {
        PushEntry(NotificationEntry.Create(_nextId++, tick, category, title, note, subject, tech, civic));
    }

    private void AddInspiration(InspirationTriggered inspiration)
    {
        if (_strings is null)
        {
            return;
        }

        if (inspiration.Tech is { } tech)
        {
            string name = TechDisplayName(tech);
            AddSingle(inspiration.Tick, NotificationCategory.Progress, _strings.Format("ui.notifications.inspiration.tech", ("name", name)), note: null, SubjectLink.None, tech: tech);
        }
        else if (inspiration.Civic is { } civic)
        {
            string name = CivicDisplayName(civic);
            AddSingle(inspiration.Tick, NotificationCategory.Progress, _strings.Format("ui.notifications.inspiration.civic", ("name", name)), note: null, SubjectLink.None, civic: civic);
        }
    }

    private void AddAdvice(AdviceRaised raised)
    {
        if (_strings is null)
        {
            return;
        }

        string kind = AdviceKey(raised.Advice.Kind);
        string title = _strings.Get($"advisor.{kind}.title");
        string body = _strings.Get($"advisor.{kind}.body");
        string subject = SubjectDisplayName(raised.Advice.Subject);
        string text = string.IsNullOrEmpty(subject) ? title : $"{title} — {subject}";

        // No repeated toasts for the same advice ID: a re-raised note only refreshes its pin.
        if (_shownAdvice.Add(raised.Advice.Id))
        {
            PushEntry(NotificationEntry.Create(_nextId++, raised.Tick, NotificationCategory.Advice, text, body, raised.Advice.Subject));
        }
    }

    private void PushEntry(NotificationEntry entry)
    {
        _entries.Add(entry);
        while (_entries.Count > MaxHistory)
        {
            _entries.RemoveAt(0);
        }

        ShowToast(entry);
        if (_history is not { Visible: true })
        {
            _unread++;
        }

        RefreshFeedButton();
        _historyDirty = true;
        GD.Print($"[Evenmere] Notifications: '{entry.Title}'.");
    }

    private void ReplaceEntry(NotificationEntry entry)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Id == entry.Id)
            {
                _entries[i] = entry;
                break;
            }
        }

        _historyDirty = true;
        GD.Print($"[Evenmere] Notifications: '{entry.Title}'.");
    }

    private NotificationEntry? FindEntry(int id)
    {
        foreach (NotificationEntry entry in _entries)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }

        return null;
    }

    // Points the feed at the running world, restarting the session-only rows when the world changes
    // (new content) or when ticks jump back (a save loaded in-game).
    private void EnsureSession(SimHost host)
    {
        if (!ReferenceEquals(_content, host.Content))
        {
            _content = host.Content;
            _strings = new UiStrings(host.Locale);
            ResetSession(host);
        }
        else if (host.CurrentTick < _lastTick)
        {
            ResetSession(host);
        }

        _lastTick = host.CurrentTick;
    }

    private static long DayOf(long tick) => tick / GameTime.TicksPerDay;

    private void DropStaleBatches(long day)
    {
        if (_batchesDay != day)
        {
            _batches.Clear();
            _batchesDay = day;
        }
    }

    private void ResetSession(SimHost host)
    {
        _entries.Clear();
        _batches.Clear();
        _batchesDay = -1;
        _shownAdvice.Clear();
        _nextId = 1;
        _unread = 0;
        _pinSignature = string.Empty;
        foreach (PanelContainer pin in _pins.Values)
        {
            pin.QueueFree();
        }

        _pins.Clear();
        if (_advicePanel is not null)
        {
            _advicePanel.Visible = false;
        }
        foreach (ToastView toast in _toasts)
        {
            toast.Button.QueueFree();
        }

        _toasts.Clear();
        _historyDirty = true;
        RefreshFeedButton();
        RefreshPins(host);
    }

    private string BatchTitle(string prefix, string name, int count)
    {
        UiStrings strings = _strings!;
        return count <= 1
            ? strings.Format(prefix + ".one", ("name", name), ("tier", name))
            : strings.Format(prefix + ".many", ("count", count.ToString()), ("name", name), ("tier", name));
    }

    private string TechTitle(TechId tech)
    {
        UiStrings strings = _strings!;
        return strings.Format("ui.notifications.tech", ("name", TechDisplayName(tech)));
    }

    private string CivicTitle(CivicId civic)
    {
        UiStrings strings = _strings!;
        return strings.Format("ui.notifications.civic", ("name", CivicDisplayName(civic)));
    }

    private string BuildingDisplayName(BuildingTypeId type)
    {
        UiStrings strings = _strings!;
        ContentDb content = _content!;
        string key = $"building.{content.NameOf(type)}.name";
        string text = strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(type) : text;
    }

    private string TechDisplayName(TechId tech)
    {
        UiStrings strings = _strings!;
        ContentDb content = _content!;
        string key = $"tech.{content.NameOf(tech)}.name";
        string text = strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(tech) : text;
    }

    private string CivicDisplayName(CivicId civic)
    {
        UiStrings strings = _strings!;
        ContentDb content = _content!;
        string key = $"civic.{content.NameOf(civic)}.name";
        string text = strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(civic) : text;
    }

    private string TierNameOf(TierId tier) => _content!.NameOf(tier);

    private string SubjectDisplayName(SubjectLink subject)
    {
        if (_strings is null || _content is null)
        {
            return string.Empty;
        }

        return subject.Kind switch
        {
            SubjectKind.Building => BuildingNameOf(subject.AsBuilding()),
            SubjectKind.Good => _strings.GoodName(_content.NameOf(subject.AsGood())),
            SubjectKind.Tier => _strings.TierName(_content.NameOf(subject.AsTier())),
            SubjectKind.BuildingType => BuildingDisplayName(subject.AsBuildingType()),
            _ => string.Empty,
        };
    }

    private string BuildingNameOf(EntityId id)
    {
        if (_host is { IsRunning: true } host && _strings is not null
            && host.Query.Map.TryGetBuilding(id, out BuildingInfo info))
        {
            return BuildingDisplayName(info.Type);
        }

        return $"#{id.Value}";
    }

    private static string AdviceKey(AdviceKind kind) => kind switch
    {
        AdviceKind.NoRoadAccess => "no_road_access",
        AdviceKind.OutsideStorehouseReach => "outside_storehouse_reach",
        AdviceKind.GoodShortage => "good_shortage",
        AdviceKind.HouseCannotAdvance => "house_cannot_advance",
        AdviceKind.NoWorkforce => "no_workforce",
        AdviceKind.ResearchIdle => "research_idle",
        AdviceKind.CivicIdle => "civic_idle",
        _ => "no_road_access",
    };

    // Matches the HUD's date (HudViewModel.DateOf): the day within the season from 1, and years from 1.
    private string DateOf(long tick)
    {
        UiStrings strings = _strings!;
        var clock = new GameClock(tick);
        string season = strings.Get(SeasonKey(clock.Season));
        return strings.Format(
            "ui.notifications.date",
            ("day", (clock.DayOfSeason + 1).ToString()),
            ("season", season),
            ("year", (clock.Year + 1).ToString()));
    }

    private static string SeasonKey(Season season) => season switch
    {
        Season.Spring => "ui.hud.season.spring",
        Season.Summer => "ui.hud.season.summer",
        Season.Autumn => "ui.hud.season.autumn",
        Season.Winter => "ui.hud.season.winter",
        _ => "ui.hud.season.spring",
    };

    private void FocusEntry(NotificationEntry entry)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (entry.Tech is not null)
        {
            OpenPanel(ResearchPath, CulturePath);
            GD.Print($"[Evenmere] Notifications: subject link tech {host.Content.NameOf(entry.Tech.Value)} followed; research panel opened.");
            return;
        }

        if (entry.Civic is not null)
        {
            OpenPanel(CulturePath, ResearchPath);
            GD.Print($"[Evenmere] Notifications: subject link civic {host.Content.NameOf(entry.Civic.Value)} followed; civics panel opened.");
            return;
        }

        switch (entry.Subject.Kind)
        {
            case SubjectKind.Building:
                {
                    EntityId id = entry.Subject.AsBuilding();
                    if (host.Query.Map.TryGetBuilding(id, out BuildingInfo info))
                    {
                        _rig?.FocusOnTile(new Vector2I(info.Origin.X, info.Origin.Y));
                        GetNodeOrNull<InspectorPanel>(InspectorPath)?.Open(id);
                        GD.Print($"[Evenmere] Notifications: subject link building {id.Value} followed; camera focused, inspector opened.");
                    }

                    break;
                }

            case SubjectKind.Good:
                {
                    // Goods open as a small popup with their explanation, the same compact view the HUD chips use.
                    GoodId good = entry.Subject.AsGood();
                    ShowGoodPopup(host, good);
                    GD.Print($"[Evenmere] Notifications: subject link good {host.Content.NameOf(good)} followed; explanation popup opened.");
                    break;
                }

            case SubjectKind.Tier:
                {
                    TierId tier = entry.Subject.AsTier();
                    if (TryFindHouseOfTier(host, tier, out EntityId house)
                        && host.Query.Map.TryGetBuilding(house, out BuildingInfo info))
                    {
                        _rig?.FocusOnTile(new Vector2I(info.Origin.X, info.Origin.Y));
                        GetNodeOrNull<InspectorPanel>(InspectorPath)?.Open(house);
                        GD.Print($"[Evenmere] Notifications: subject link tier {host.Content.NameOf(tier)} followed; camera focused, inspector opened on house {house.Value}.");
                    }
                    else
                    {
                        GD.Print($"[Evenmere] Notifications: subject link tier {host.Content.NameOf(tier)} has no house to focus.");
                    }

                    break;
                }

            default:
                GD.Print($"[Evenmere] Notifications: entry '{entry.Title}' has nothing to focus.");
                break;
        }
    }

    private static bool TryFindHouseOfTier(SimHost host, TierId tier, out EntityId house)
    {
        foreach (BuildingInfo building in host.Query.Map.ActiveBuildings)
        {
            if (host.Query.Population.TryGetHouse(building.Id, out HouseInfo info) && info.Tier.Equals(tier))
            {
                house = building.Id;
                return true;
            }
        }

        house = EntityId.None;
        return false;
    }

    private void OpenPanel(NodePath show, NodePath hide)
    {
        if (GetNodeOrNull<Control>(hide) is { } other)
        {
            other.Visible = false;
        }

        if (GetNodeOrNull<Control>(show) is { } panel)
        {
            panel.Visible = true;
        }
    }

    private void ShowGoodPopup(SimHost host, GoodId good)
    {
        if (_goodPopup is null || _goodView is null || _goodTitle is null || _strings is null || _content is null)
        {
            return;
        }

        _goodTitle.Text = _strings.GoodName(host.Content.NameOf(good));
        _goodView.Setup(host.Content, _strings, host.Query.Explain.ExplainGood, BuildingNameOf, null);
        _goodView.MaxDepth = 2;
        _goodView.ShowRoot(host.Query.Explain.ExplainGood(good));
        _goodPopup.Visible = true;
    }

    private void BuildChrome()
    {
        BuildToasts();
        BuildFeedButton();
        BuildAdvice();
        BuildHistory();
        BuildGoodPopup();
    }

    private void BuildToasts()
    {
        var box = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        box.AnchorLeft = 0.5f;
        box.AnchorRight = 0.5f;
        box.AnchorTop = 1f;
        box.AnchorBottom = 1f;
        box.OffsetLeft = -ToastWidth / 2f;
        box.OffsetRight = ToastWidth / 2f;
        box.OffsetTop = -200f;
        box.OffsetBottom = -80f;
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);
        _toastBox = box;
    }

    private void BuildFeedButton()
    {
        var button = new Button
        {
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(204f, 44f),
        };
        button.AnchorLeft = 0f;
        button.AnchorRight = 0f;
        button.AnchorTop = 0f;
        button.AnchorBottom = 0f;
        button.OffsetLeft = 16f;
        button.OffsetTop = 208f;
        button.OffsetRight = 220f;
        button.OffsetBottom = 252f;
        button.Pressed += ToggleHistory;
        AddChild(button);
        _feedButton = button;
        RefreshFeedButton();
    }

    // The pinned-notes panel sizes to its content (never covering the HUD toggles or the Notices button),
    // caps at 40% of the screen height with a scroll beyond that, and collapses to a one-line chip.
    private void BuildAdvice()
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 0f;
        panel.AnchorRight = 0f;
        panel.AnchorTop = 0f;
        panel.AnchorBottom = 0f;
        panel.OffsetLeft = 16f;
        panel.OffsetRight = 352f;
        panel.OffsetTop = 262f;
        panel.OffsetBottom = 362f;
        AddChild(panel);
        _advicePanel = panel;
        panel.Visible = false;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        margin.AddChild(top);

        var header = new Button { FocusMode = Control.FocusModeEnum.All };
        header.Pressed += () =>
        {
            _adviceCollapsed = !_adviceCollapsed;
            LayoutAdvice();
        };
        top.AddChild(header);
        _adviceHeader = header;

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        top.AddChild(scroll);
        _adviceScroll = scroll;

        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(rows);
        _adviceRows = rows;

        RetitleAdvice();
    }

    // Stacks the Notices button and the pinned-notes panel under the HUD toggles' actual bottom edge, so a
    // taller HUD bar (more tiers, longer workforce text) pushes the whole left column down instead of
    // overlapping it. Falls back to the placed offsets while the toggles are missing. The panel keeps its
    // current height when shifted; LayoutAdvice recomputes it from the new top on its own cadence.
    private void LayoutLeftColumn()
    {
        const float TogglesGap = 8f;
        const float FeedButtonHeight = 44f;
        const float AdviceGap = 10f;

        if (_toggles is null)
        {
            _toggles = GetNodeOrNull<Control>(TogglesPath);
            if (_toggles is null)
            {
                return;
            }
        }

        float buttonTop = _toggles.GetGlobalRect().End.Y + TogglesGap;
        if (_feedButton is not null && Math.Abs(_feedButton.OffsetTop - buttonTop) > 0.5f)
        {
            _feedButton.OffsetTop = buttonTop;
            _feedButton.OffsetBottom = buttonTop + FeedButtonHeight;
        }

        if (_advicePanel is not null)
        {
            float panelTop = (_feedButton?.OffsetBottom ?? buttonTop + FeedButtonHeight) + AdviceGap;
            if (Math.Abs(_advicePanel.OffsetTop - panelTop) > 0.5f)
            {
                float height = _advicePanel.OffsetBottom - _advicePanel.OffsetTop;
                _advicePanel.OffsetTop = panelTop;
                _advicePanel.OffsetBottom = panelTop + height;
            }
        }
    }

    // Sizes the panel to its content, capped at 40% of the viewport height; beyond that the rows scroll.
    // Collapsed, only the header chip shows. Runs on the refresh throttle so wrapped-label sizes have settled.
    private void LayoutAdvice()
    {
        if (_advicePanel is null || _adviceHeader is null || _adviceScroll is null || !_advicePanel.Visible)
        {
            return;
        }

        const float Margins = 32f;
        const float Spacing = 6f;
        float headerH = _adviceHeader.GetCombinedMinimumSize().Y;
        float chrome = Margins + headerH + Spacing;
        if (_adviceCollapsed)
        {
            _adviceScroll.Visible = false;
            _advicePanel.OffsetBottom = _advicePanel.OffsetTop + chrome;
            return;
        }

        _adviceScroll.Visible = true;
        float rowsH = 0f;
        int count = 0;
        foreach (PanelContainer row in _pins.Values)
        {
            rowsH += row.GetCombinedMinimumSize().Y;
            count++;
        }

        if (count > 1)
        {
            rowsH += Spacing * (count - 1);
        }

        float budget = Math.Max(80f, GetViewportRect().Size.Y * 0.4f - chrome);
        _advicePanel.OffsetBottom = _advicePanel.OffsetTop + chrome + Math.Min(rowsH, budget);
    }

    private void BuildHistory()
    {
        var panel = new PanelContainer { Visible = false };
        panel.AnchorLeft = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorTop = 0.5f;
        panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -320f;
        panel.OffsetRight = 200f;
        panel.OffsetTop = -230f;
        panel.OffsetBottom = 230f;
        AddChild(panel);
        _history = panel;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        margin.AddChild(top);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        var title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.SetMeta("role", "history_title");
        header.AddChild(title);
        _historyTitle = title;

        var close = new Button { FocusMode = Control.FocusModeEnum.All };
        close.Pressed += () => panel.Visible = false;
        header.AddChild(close);
        _historyClose = close;

        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 6);
        top.AddChild(filters);

        var group = new ButtonGroup();
        var buttons = new Button[_filters.Count];
        for (int i = 0; i < _filters.Count; i++)
        {
            string name = _filters[i];
            var chip = new Button
            {
                Text = name,
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = Control.FocusModeEnum.All,
            };
            chip.Pressed += () =>
            {
                _filter = name;
                _historyDirty = true;
                if (_host is { IsRunning: true } host)
                {
                    RebuildHistory(host);
                }
            };
            filters.AddChild(chip);
            buttons[i] = chip;
        }

        _filterButtons = buttons;

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        top.AddChild(scroll);

        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(rows);
        _historyRows = rows;

        RetitleHistory();
    }

    private void BuildGoodPopup()
    {
        var popup = new PanelContainer { Visible = false };
        popup.AnchorLeft = 0.5f;
        popup.AnchorRight = 0.5f;
        popup.AnchorTop = 0.5f;
        popup.AnchorBottom = 0.5f;
        popup.OffsetLeft = -230f;
        popup.OffsetRight = 230f;
        popup.OffsetTop = -190f;
        popup.OffsetBottom = 190f;
        AddChild(popup);
        _goodPopup = popup;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        popup.AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        margin.AddChild(top);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        top.AddChild(header);

        var title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(title);
        _goodTitle = title;

        var close = new Button { FocusMode = Control.FocusModeEnum.All };
        close.Pressed += () => popup.Visible = false;
        header.AddChild(close);
        _goodClose = close;

        var view = new ExplanationView { MaxDepth = 2, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        view.BuildingLinkActivated += OnPopupBuildingLink;
        top.AddChild(view);
        _goodView = view;
        RetitlePopup();
    }

    private void OnPopupBuildingLink(EntityId building)
    {
        if (_host is not { IsRunning: true } host)
        {
            return;
        }

        if (host.Query.Map.TryGetBuilding(building, out BuildingInfo info))
        {
            _rig?.FocusOnTile(new Vector2I(info.Origin.X, info.Origin.Y));
            GetNodeOrNull<InspectorPanel>(InspectorPath)?.Open(building);
            GD.Print($"[Evenmere] Notifications: subject link building {building.Value} followed; camera focused, inspector opened.");
        }
    }

    private void RetitleAdvice()
    {
        if (_strings is null || _adviceHeader is null)
        {
            return;
        }

        string marker = _adviceCollapsed ? "\u25b8 " : "\u25be ";
        _adviceHeader.Text = marker + _strings.Format("ui.notifications.advice.pinned.count", ("count", _pins.Count.ToString()));
        _adviceHeader.TooltipText = _strings.Get("ui.notifications.advice.toggle.tip");
    }

    private void RetitleHistory()
    {
        if (_strings is null)
        {
            return;
        }

        if (_historyTitle is not null)
        {
            _historyTitle.Text = _strings.Get("ui.notifications.history.title");
        }

        if (_historyClose is not null)
        {
            _historyClose.Text = _strings.Get("ui.notifications.close");
            _historyClose.TooltipText = _strings.Get("ui.notifications.close.tip");
        }

        string[] keys = ["ui.notifications.filter.all", "ui.notifications.filter.building", "ui.notifications.filter.people", "ui.notifications.filter.progress", "ui.notifications.filter.advice"];
        for (int i = 0; i < _filterButtons.Length && i < keys.Length; i++)
        {
            _filterButtons[i].Text = _strings.Get(keys[i]);
        }

        for (int i = 0; i < _filterButtons.Length && i < _filters.Count; i++)
        {
            _filterButtons[i].SetPressedNoSignal(string.Equals(_filters[i], _filter, StringComparison.Ordinal));
        }
    }

    private void RetitlePopup()
    {
        if (_strings is null)
        {
            return;
        }

        if (_goodClose is not null)
        {
            _goodClose.Text = _strings.Get("ui.notifications.close");
            _goodClose.TooltipText = _strings.Get("ui.notifications.close.tip");
        }
    }

    private void RefreshFeedButton()
    {
        if (_feedButton is null || _strings is null)
        {
            return;
        }

        _feedButton.Text = _unread > 0
            ? _strings.Format("ui.notifications.open.count", ("count", _unread.ToString()))
            : _strings.Get("ui.notifications.open");
        _feedButton.TooltipText = _strings.Get("ui.notifications.open.tip");
    }

    private void ToggleHistory()
    {
        if (_history is null || _host is not { IsRunning: true } host)
        {
            return;
        }

        _history.Visible = !_history.Visible;
        if (_history.Visible)
        {
            _unread = 0;
            RefreshFeedButton();
            _historyDirty = true;
            RebuildHistory(host);
            _historyClose?.GrabFocus();
        }
    }

    private void ShowToast(NotificationEntry entry)
    {
        if (_toastBox is null)
        {
            return;
        }

        // At most 3 visible: the oldest toast goes back to history (where every entry already lives).
        while (_toasts.Count >= MaxToasts)
        {
            ToastView oldest = _toasts[0];
            _toasts.RemoveAt(0);
            _toastBox.RemoveChild(oldest.Button);
            oldest.Button.QueueFree();
        }

        var button = new Button
        {
            Text = entry.Title,
            TooltipText = entry.Note ?? entry.Title,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(ToastWidth, 44f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        int id = entry.Id;
        button.Pressed += () =>
        {
            if (FindEntry(id) is { } live)
            {
                FocusEntry(live);
            }
        };
        _toastBox.AddChild(button);
        _toasts.Add(new ToastView(entry.Id, button, ToastSeconds));
    }

    private void RefreshToast(NotificationEntry entry)
    {
        foreach (ToastView toast in _toasts)
        {
            if (toast.EntryId == entry.Id)
            {
                toast.Button.Text = entry.Title;
                toast.Button.TooltipText = entry.Note ?? entry.Title;
                toast.SecondsLeft = ToastSeconds;
                toast.Button.Modulate = new Color(1f, 1f, 1f, 1f);
                return;
            }
        }

        // The batch is no longer showing (it aged out): show the grown count as a fresh toast.
        ShowToast(entry);
    }

    private void TickToasts(double delta)
    {
        if (_toastBox is null)
        {
            return;
        }

        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            ToastView toast = _toasts[i];
            toast.SecondsLeft -= delta;
            if (toast.SecondsLeft <= 0.0)
            {
                _toasts.RemoveAt(i);
                _toastBox.RemoveChild(toast.Button);
                toast.Button.QueueFree();
            }
            else if (toast.SecondsLeft < FadeSeconds)
            {
                float alpha = (float)(toast.SecondsLeft / FadeSeconds);
                toast.Button.Modulate = new Color(1f, 1f, 1f, alpha);
            }
        }
    }

    private void RebuildHistory(SimHost host)
    {
        if (_historyRows is null || _strings is null)
        {
            return;
        }

        _historyDirty = false;
        foreach (Node child in _historyRows.GetChildren())
        {
            _historyRows.RemoveChild(child);
            child.QueueFree();
        }

        RetitleHistory();
        RetitleAdvice();
        RetitlePopup();
        RefreshFeedButton();

        int shown = 0;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            NotificationEntry entry = _entries[i];
            if (!PassesFilter(entry))
            {
                continue;
            }

            NotificationEntry captured = entry;
            var row = new Button
            {
                Text = $"{DateOf(entry.Tick)} — {entry.Title}",
                TooltipText = entry.Note ?? entry.Title,
                FocusMode = Control.FocusModeEnum.All,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            row.Pressed += () => FocusEntry(captured);
            _historyRows.AddChild(row);
            shown++;
        }

        if (shown == 0)
        {
            var empty = new Label
            {
                Text = _strings.Get("ui.notifications.history.empty"),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _historyRows.AddChild(empty);
        }
    }

    private bool PassesFilter(NotificationEntry entry) => _filter switch
    {
        "building" => entry.Category == NotificationCategory.Building,
        "people" => entry.Category == NotificationCategory.People,
        "progress" => entry.Category == NotificationCategory.Progress,
        "advice" => entry.Category == NotificationCategory.Advice,
        _ => true,
    };

    private void RefreshPins(SimHost host)
    {
        if (_adviceRows is null || _strings is null || _content is null)
        {
            return;
        }

        RetitleAdvice();
        ReadOnlySpan<AdviceView> active = host.Query.Advisors.Active();

        string signature = string.Empty;
        foreach (AdviceView advice in active)
        {
            signature += $"{advice.Id}:{advice.LastSeenTick};";
        }

        if (string.Equals(signature, _pinSignature, StringComparison.Ordinal))
        {
            return;
        }

        _pinSignature = signature;

        var seen = new HashSet<int>();
        foreach (AdviceView advice in active)
        {
            seen.Add(advice.Id);
            if (!_pins.TryGetValue(advice.Id, out PanelContainer? row))
            {
                row = MakePin(host, advice);
                _adviceRows.AddChild(row);
                _pins[advice.Id] = row;
            }
        }

        var gone = new List<int>();
        foreach (int id in _pins.Keys)
        {
            if (!seen.Contains(id))
            {
                gone.Add(id);
            }
        }

        foreach (int id in gone)
        {
            _pins[id].QueueFree();
            _pins.Remove(id);
        }

        if (_advicePanel is not null)
        {
            _advicePanel.Visible = _pins.Count > 0;
        }

        RetitleAdvice();
        LayoutAdvice();
    }

    private PanelContainer MakePin(SimHost host, AdviceView advice)
    {
        UiStrings strings = _strings!;
        string kind = AdviceKey(advice.Kind);
        string titleText = strings.Get($"advisor.{kind}.title");
        string bodyText = strings.Get($"advisor.{kind}.body");

        var row = new PanelContainer();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        row.AddChild(box);

        var title = new Label
        {
            Text = titleText,
            TooltipText = titleText,
            ClipText = true,
            MaxLinesVisible = 1,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(title);

        var body = new Label
        {
            Text = bodyText,
            TooltipText = bodyText,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddChild(body);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 6);
        box.AddChild(line);

        string subject = SubjectDisplayName(advice.Subject);
        var about = new Label
        {
            Text = subject,
            TooltipText = subject,
            ClipText = true,
            MaxLinesVisible = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.AddChild(about);

        int id = advice.Id;
        var dismiss = new Button
        {
            Text = strings.Get("ui.notifications.advice.dismiss"),
            TooltipText = strings.Get("ui.notifications.advice.dismiss.tip"),
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(76f, 30f),
        };
        dismiss.Pressed += () =>
        {
            host.Enqueue(new DismissAdvice(id));
            GD.Print($"[Evenmere] Notifications: DismissAdvice {id} enqueued; note gone.");
            if (_pins.TryGetValue(id, out PanelContainer? pin))
            {
                _pins.Remove(id);
                pin.QueueFree();
            }

            if (_advicePanel is not null)
            {
                _advicePanel.Visible = _pins.Count > 0;
            }

            _pinSignature = string.Empty;
            RetitleAdvice();
            LayoutAdvice();
        };
        line.AddChild(dismiss);
        return row;
    }

    private void ClearPin(int adviceId)
    {
        if (_pins.TryGetValue(adviceId, out PanelContainer? pin))
        {
            _pins.Remove(adviceId);
            pin.QueueFree();
            if (_advicePanel is not null)
            {
                _advicePanel.Visible = _pins.Count > 0;
            }

            RetitleAdvice();
            LayoutAdvice();
            GD.Print($"[Evenmere] Notifications: advice {adviceId} cleared; pin removed quietly.");
        }

        _pinSignature = string.Empty;
    }

    private sealed class ToastView
    {
        /// <summary>Initializes a new instance of the <see cref="ToastView"/> class.</summary>
        /// <param name="entryId">The history entry the toast shows.</param>
        /// <param name="button">The toast control.</param>
        /// <param name="secondsLeft">How long the toast stays visible.</param>
        public ToastView(int entryId, Button button, double secondsLeft)
        {
            EntryId = entryId;
            Button = button;
            SecondsLeft = secondsLeft;
        }

        /// <summary>Gets the history entry the toast shows.</summary>
        public int EntryId { get; }

        /// <summary>Gets the toast control.</summary>
        public Button Button { get; }

        /// <summary>Gets or sets how long the toast stays visible.</summary>
        public double SecondsLeft { get; set; }
    }
}
