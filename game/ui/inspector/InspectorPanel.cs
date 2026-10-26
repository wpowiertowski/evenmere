using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Game.Controls;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Chains;
using Evenmere.Game.Ui.Events;
using Evenmere.Game.Ui.Exploration;
using Evenmere.Game.Ui.Monuments;
using Evenmere.Game.Rendering.Camera;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Explain;
using Evenmere.Game.Ui.Hud;
using Evenmere.Game.Ui.Progress;
using Evenmere.Game.Ui.Tools;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Exploration;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Production.Commands;
using Evenmere.Sim.Systems.Workforce.Commands;
using Godot;

namespace Evenmere.Game.Ui.Inspector;

/// <summary>
/// The building and house inspector: a docked panel on the right, opened from
/// <c>ToolController.BuildingClicked</c> while idle. It shows the name, state and
/// productivity breakdown (<see cref="IExplainQueries.ExplainBuilding"/>), buffers,
/// workforce fill (naming substitutes from above) and storehouse reach, plus pause and priority toggles and
/// demolish and upgrade buttons. Houses show tier, residents, morale, needs, advance and
/// decline (<see cref="IExplainQueries.ExplainHouse"/>). It refreshes at most
/// four times a second and closes on <c>tool_cancel</c> or its Close button.
/// </summary>
public sealed partial class InspectorPanel : PanelContainer
{
    private const float PanelWidth = 416f;
    private const float PanelTop = 150f;
    private const float PanelMargin = 16f;

    // The pause menu's Menu button sits bottom-right (124 by 48 at a 16 margin); the panel ends above it with the
    // same margin, so the inspector's own buttons stay fully visible and clickable at every window size.
    private const float MenuReserve = 64f + PanelMargin;

    // Debug screenshots only: --inspector-open=<entity id> opens the inspector on that building once the world is
    // running (M1-37 evidence), so a workplace or house can be captured without clicking it.
    private int _debugOpenEntity;
    private bool _debugOpenChain;
    private SimHost? _host;
    private CameraRig? _rig;
    private ContentDb? _builtFor;
    private UiStrings? _strings;
    private EntityId _target = EntityId.None;
    private bool _hasTarget;
    private double _secondsSinceRefresh;
    private bool _everRefreshed;

    private Label? _title;
    private Label? _stateLabel;
    private Label? _workforceLabel;
    private Label? _moraleLabel;
    private Label? _expeditionLabel;
    private ExplanationView? _explain;
    private Button? _closeButton;
    private Button? _pauseButton;
    private Button? _priorityButton;
    private Button? _demolishButton;
    private Button? _upgradeButton;
    private Button? _festivalsButton;
    private Button? _expeditionButton;
    private Button? _lockButton;
    private Button? _chainButton;
    private MonumentSection? _monumentSection;
    private ConfirmationDialog? _demolishConfirm;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../SimHost");

    /// <summary>Gets or sets the path to the <see cref="CameraRig"/> node, relative to this node.</summary>
    [Export]
    public NodePath RigPath { get; set; } = new("../CameraRig");

    /// <summary>Gets or sets the path to the tools node that raises <c>BuildingClicked</c>, relative to this node.</summary>
    [Export]
    public NodePath ToolsPath { get; set; } = new("../Tools");

    /// <summary>Gets or sets the path to the chain viewer, opened for a producer's output good.</summary>
    [Export]
    public NodePath ChainPath { get; set; } = new("../ChainViewer");

    /// <summary>Gets or sets the path to the festival panel, opened for a festival ground.</summary>
    [Export]
    public NodePath FestivalPath { get; set; } = new("../FestivalPanel");

    /// <summary>Gets or sets the path to the expedition picker, opened for a Scout's Lodge.</summary>
    [Export]
    public NodePath ExpeditionPath { get; set; } = new("../ExpeditionPanel");

    /// <summary>Gets or sets the path to the research panel, hidden while the inspector is open.</summary>
    [Export]
    public NodePath ResearchPath { get; set; } = new("../ResearchPanel");

    /// <summary>Gets or sets the path to the civics panel, hidden while the inspector is open.</summary>
    [Export]
    public NodePath CulturePath { get; set; } = new("../CulturePanel");

    /// <summary>Gets the inspected building, or <see cref="EntityId.None"/> when closed.</summary>
    public EntityId Target => _hasTarget ? _target : EntityId.None;

    /// <inheritdoc />
    public override void _Ready()
    {
        Place();
        PanelTheme.Apply(this);
        _host = GetNodeOrNull<SimHost>(HostPath);
        _rig = GetNodeOrNull<CameraRig>(RigPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] inspector: no SimHost at '{HostPath}'; the inspector stays empty.");
            return;
        }

        if (OS.IsDebugBuild())
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--inspector-open=", StringComparison.Ordinal)
                    && int.TryParse(arg["--inspector-open=".Length..], out int id))
                {
                    _debugOpenEntity = id;
                }

                // Debug screenshots only: --chain-open presses the Production chain button once the
                // inspector has a target, so the M2-21 shots open the viewer through the button itself.
                if (arg == "--chain-open")
                {
                    _debugOpenChain = true;
                }
            }
        }

        BuildChrome();
        if (GetNodeOrNull<ToolController>(ToolsPath) is { } tools)
        {
            tools.BuildingClicked += OnBuildingClicked;
        }

        _host.EventsDrained += OnEvents;
        Visible = false;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (GetNodeOrNull<ToolController>(ToolsPath) is { } tools)
        {
            tools.BuildingClicked -= OnBuildingClicked;
        }

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
            RebuildFor(host);
        }

        if (_debugOpenEntity > 0)
        {
            var target = new EntityId(_debugOpenEntity);
            if (host.Query.Map.TryGetBuilding(target, out _))
            {
                Open(target);
                _debugOpenEntity = 0;
            }
        }

        if (_debugOpenChain && _hasTarget)
        {
            _debugOpenChain = false;
            OpenChain();
        }

        // A research or civics panel opened elsewhere hides this one, so at most one
        // docked panel is open (the inspector hides them when it opens).
        if (Visible && OtherPanelOpen())
        {
            Visible = false;
            _hasTarget = false;
        }

        if (!Visible || !_hasTarget)
        {
            return;
        }

        _secondsSinceRefresh += delta;
        if (_everRefreshed && _secondsSinceRefresh < ViewModel.MinRefreshIntervalSeconds)
        {
            return;
        }

        _secondsSinceRefresh = 0;
        _everRefreshed = true;
        Refresh();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        // Unhandled, matching the pause menu: while a tool is active the tools see Esc first and
        // cancel the tool, and only an idle Esc closes this panel.
        if (!Visible || @event.IsEcho())
        {
            return;
        }

        if (InputMap.HasAction("tool_cancel") && @event.IsActionPressed("tool_cancel"))
        {
            Visible = false;
            _hasTarget = false;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the inspector on a building.</summary>
    /// <param name="building">The building's entity ID.</param>
    public void Open(EntityId building)
    {
        _target = building;
        _hasTarget = true;
        HideOthers();
        Visible = true;
        _secondsSinceRefresh = double.MaxValue;
        GD.Print($"[Evenmere] Inspector: opened on building {building.Value}.");
    }

    /// <summary>Closes the inspector.</summary>
    public void Close()
    {
        Visible = false;
        _hasTarget = false;
    }

    private void OnBuildingClicked(EntityId building) => Open(building);

    private void OnEvents(IReadOnlyList<SimEvent> events) => _secondsSinceRefresh = double.MaxValue;

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
        MouseFilter = Control.MouseFilterEnum.Stop;
    }

    private void RebuildFor(SimHost host)
    {
        _builtFor = host.Content;
        _strings = new UiStrings(host.Locale);
        _explain?.ResetContent(host.Content, _strings, host.Query.Explain.ExplainGood, BuildingNameOf, StorehouseReachOf);
        Retitle();
        _secondsSinceRefresh = double.MaxValue;
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

        _title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(_title);

        _closeButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _closeButton.Pressed += Close;
        header.AddChild(_closeButton);

        _stateLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_stateLabel);

        _workforceLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_workforceLabel);

        _moraleLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_moraleLabel);

        _expeditionLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_expeditionLabel);

        _explain = new ExplanationView { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _explain.BuildingLinkActivated += OnLinkBuilding;
        top.AddChild(_explain);

        // The monument section reads as part of the building's information, above the actions: hidden for
        // anything but a monument, so a normal building's inspector is unchanged.
        _monumentSection = new MonumentSection { Visible = false };
        top.AddChild(_monumentSection);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        top.AddChild(buttons);

        _pauseButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _pauseButton.Pressed += TogglePause;
        buttons.AddChild(_pauseButton);

        _priorityButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _priorityButton.Pressed += TogglePriority;
        buttons.AddChild(_priorityButton);

        var second = new HBoxContainer();
        second.AddThemeConstantOverride("separation", 8);
        top.AddChild(second);

        _demolishButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _demolishButton.Pressed += AskDemolish;
        second.AddChild(_demolishButton);

        _upgradeButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _upgradeButton.Pressed += DoUpgrade;
        second.AddChild(_upgradeButton);

        _festivalsButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _festivalsButton.Pressed += OpenFestivals;
        second.AddChild(_festivalsButton);

        _expeditionButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _expeditionButton.Pressed += OpenExpeditions;
        second.AddChild(_expeditionButton);

        _lockButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _lockButton.Pressed += ToggleLock;
        second.AddChild(_lockButton);

        _chainButton = new Button { FocusMode = Control.FocusModeEnum.All };
        _chainButton.Pressed += OpenChain;
        second.AddChild(_chainButton);

        _demolishConfirm = new ConfirmationDialog();
        AddChild(_demolishConfirm);
        _demolishConfirm.Confirmed += DoDemolish;
    }

    private void Retitle()
    {
        if (_strings is null || _closeButton is null)
        {
            return;
        }

        _closeButton.Text = _strings.Get("ui.inspector.close");
        _closeButton.TooltipText = _strings.Get("ui.inspector.close.tip");
    }

    private bool OtherPanelOpen()
    {
        return (GetNodeOrNull<Control>(ResearchPath) is { Visible: true })
            || (GetNodeOrNull<Control>(CulturePath) is { Visible: true });
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

        // Opening by click leaves the demolish or build tool running; the inspector
        // is for looking, so tools go back to idle through Cancel.
        if (GetNodeOrNull<ToolController>(ToolsPath) is { } tools)
        {
            tools.Cancel();
        }
    }

    private void Refresh()
    {
        if (_host is not { IsRunning: true } host || _strings is null || _explain is null)
        {
            return;
        }

        UiStrings strings = _strings;
        if (!host.Query.Map.TryGetBuilding(_target, out BuildingInfo building))
        {
            if (_title is not null)
            {
                _title.Text = strings.Get("ui.inspector.title");
            }

            if (_stateLabel is not null)
            {
                _stateLabel.Text = strings.Get("ui.inspector.unknown");
            }

            _explain.Clear();
            SetButtonsVisible(pause: false, priority: false, demolish: false, upgrade: false, festivals: false, expedition: false, lockToggle: false, chain: false);
            if (_expeditionLabel is not null)
            {
                _expeditionLabel.Visible = false;
            }
            _monumentSection?.HideSection();
            return;
        }

        ContentDb content = host.Content;

        bool isHouse = host.Query.Population.TryGetHouse(_target, out HouseInfo house);
        string state = building.State switch
        {
            BuildingState.Site => strings.Get("ui.inspector.state.site"),
            BuildingState.Active => strings.Get("ui.inspector.state.active"),
            BuildingState.Paused => strings.Get("ui.inspector.state.paused"),
            BuildingState.Ruin => strings.Get("ui.inspector.state.ruin"),
            _ => building.State.ToString(),
        };
        if (isHouse)
        {
            // The tier is the headline; the building name stays as the small subtitle.
            string tierName = strings.TierName(content.NameOf(house.Tier));
            if (_title is not null)
            {
                _title.Text = strings.Format("ui.inspector.house.title", ("tier", tierName));
            }

            if (_stateLabel is not null)
            {
                _stateLabel.Text = $"{BuildingDisplayName(content, strings, building.Type)} · {state}";
            }
        }
        else
        {
            if (_title is not null)
            {
                _title.Text = BuildingDisplayName(content, strings, building.Type);
            }

            if (_stateLabel is not null)
            {
                _stateLabel.Text = state;
            }
        }

        if (_workforceLabel is not null)
        {
            if (isHouse)
            {
                string tierName = strings.TierName(content.NameOf(house.Tier));
                _workforceLabel.Text = strings.Format(
                    "ui.inspector.house.residents",
                    ("residents", NumberFormat.Count(house.Residents)),
                    ("capacity", NumberFormat.Count(house.Capacity)),
                    ("target", NumberFormat.Count(house.TargetResidents)));
                _workforceLabel.TooltipText = strings.Format("ui.inspector.house.tier", ("tier", tierName));
                ShowMorale(strings, house);
            }
            else
            {
                Evenmere.Core.Numerics.Ratio fill = host.Query.Workforce.FillOf(_target);
                string fillText = ExplainFormat.FormatPercent(fill);
                int substituted = host.Query.Workforce.SubstitutedWorkers(_target);
                if (substituted > 0)
                {
                    // Higher-tier workers stepped down here (ADR-0016): name the nearest source tier.
                    string sourceName = strings.TierName(content.NameOf(host.Query.Workforce.SubstituteSourceTier(_target)));
                    _workforceLabel.Text = strings.Format(
                        "ui.inspector.workforce.substituted",
                        ("fill", fillText),
                        ("count", NumberFormat.Count(substituted)),
                        ("tier", sourceName));
                }
                else
                {
                    _workforceLabel.Text = strings.Format(
                        "ui.inspector.workforce",
                        ("fill", fillText));
                }

                if (_moraleLabel is not null)
                {
                    _moraleLabel.Visible = false;
                }
            }
        }

        Explanation explanation = isHouse
            ? host.Query.Explain.ExplainHouse(_target)
            : host.Query.Explain.ExplainBuilding(_target);
        _explain.Setup(content, strings, host.Query.Explain.ExplainGood, BuildingNameOf, StorehouseReachOf);
        _explain.ShowRoot(explanation);

        UpdateButtons(host, strings, content, building, isHouse, house);
        if (_monumentSection is { Visible: true })
        {
            _monumentSection.Refresh();
        }
        GD.Print($"[Evenmere] Inspector: refreshed {_target.Value} ({content.NameOf(building.Type)}) state={building.State}.");
    }

    // The morale line for an occupied house: "Morale 100%" in the normal colour at full morale, and the
    // underemployed count in the warning tint below that, derived from morale the way ExplainService does.
    // Hidden for an empty house; workplaces never show it.
    private void ShowMorale(UiStrings strings, HouseInfo house)
    {
        if (_moraleLabel is null)
        {
            return;
        }

        if (house.Residents <= 0)
        {
            _moraleLabel.Visible = false;
            return;
        }

        _moraleLabel.Visible = true;
        string percent = NumberFormat.Count(house.MoralePerMille / 10);
        if (house.MoralePerMille >= 1000)
        {
            _moraleLabel.Text = strings.Format("ui.inspector.house.morale", ("percent", percent));
            _moraleLabel.RemoveThemeColorOverride("font_color");
            _moraleLabel.TooltipText = string.Empty;
        }
        else
        {
            int underemployed = WorkforceSummary.UnderemployedCount(house.Residents, house.MoralePerMille);
            string key = underemployed == 1 ? "ui.inspector.house.morale.below.one" : "ui.inspector.house.morale.below.other";
            _moraleLabel.Text = strings.Format(
                key,
                ("percent", percent),
                ("count", NumberFormat.Count(underemployed)));
            _moraleLabel.AddThemeColorOverride("font_color", HudChip.WarningTint);
            _moraleLabel.TooltipText = strings.Get("ui.inspector.house.morale.tip");
        }
    }

    private void UpdateButtons(SimHost host, UiStrings strings, ContentDb content, BuildingInfo building, bool isHouse, HouseInfo house)
    {
        bool isSite = building.State == BuildingState.Site;
        bool paused = building.State == BuildingState.Paused;
        bool isProducer = host.Query.Production.TryGet(_target, out _);
        bool needsWorkers = content.Get(building.Type).Workforce is not null;
        bool highPriority = host.Query.Workforce.IsHighPriority(_target);

        if (_pauseButton is not null)
        {
            _pauseButton.Visible = isProducer && !isSite;
            _pauseButton.Text = paused ? strings.Get("ui.inspector.resume") : strings.Get("ui.inspector.pause");
            _pauseButton.TooltipText = paused ? strings.Get("ui.inspector.resume.tip") : strings.Get("ui.inspector.pause.tip");
        }

        if (_priorityButton is not null)
        {
            _priorityButton.Visible = needsWorkers && !isSite && !isHouse;
            _priorityButton.Text = (highPriority ? "[x] " : "[ ] ") + strings.Get("ui.inspector.priority");
            _priorityButton.TooltipText = strings.Get("ui.inspector.priority.tip");
            _priorityButton.Disabled = false;
        }

        if (_demolishButton is not null)
        {
            _demolishButton.Visible = true;
            _demolishButton.Text = strings.Get("ui.inspector.demolish");
            _demolishButton.TooltipText = strings.Get("ui.inspector.demolish.tip");
        }

        BuildingTypeId? upgrade = FindUpgrade(content, building.Type);
        if (_upgradeButton is not null)
        {
            _upgradeButton.Visible = upgrade.HasValue && !isSite;
            if (upgrade.HasValue)
            {
                string name = BuildingDisplayName(content, strings, upgrade.Value);
                _upgradeButton.Text = strings.Format("ui.inspector.upgrade", ("name", name));
                _upgradeButton.TooltipText = strings.Get("ui.inspector.upgrade.tip");
            }
        }

        if (_lockButton is not null)
        {
            _lockButton.Visible = isHouse;
            if (isHouse)
            {
                _lockButton.Text = house.UpgradeLocked
                    ? strings.Get("ui.inspector.house.unlock")
                    : strings.Get("ui.inspector.house.lock");
                _lockButton.TooltipText = house.UpgradeLocked
                    ? strings.Get("ui.inspector.house.unlock.tip")
                    : strings.Get("ui.inspector.house.lock.tip");
            }
        }

        if (_chainButton is not null)
        {
            _chainButton.Visible = isProducer && !isSite;
            _chainButton.Text = strings.Get("ui.chains.open");
            _chainButton.TooltipText = strings.Get("ui.chains.open.tip");
        }

        // Festival grounds open the festival panel by category tag, never by content ID: any finished
        // building the tags mark as a ground qualifies, whatever the trees add later.
        bool isGround = !isSite && HasTag(content.Get(building.Type).Tags, "festival_ground");
        if (_festivalsButton is not null)
        {
            _festivalsButton.Visible = isGround;
            _festivalsButton.Text = strings.Get("ui.events.inspector.festivals");
            _festivalsButton.TooltipText = strings.Get("ui.events.inspector.festivals.tip");
        }

        // Scout's Lodges open the expedition picker by category tag, never by content ID: any finished
        // building the tags mark as a lodge qualifies, whatever the trees add later.
        bool isLodge = !isSite && ExpeditionPanel.IsLodge(content, building);
        if (_expeditionButton is not null)
        {
            _expeditionButton.Visible = isLodge;
            _expeditionButton.Text = strings.Get("ui.exploration.inspector.expedition");
            _expeditionButton.TooltipText = strings.Get("ui.exploration.inspector.expedition.tip");
        }

        if (_expeditionLabel is not null)
        {
            ExpeditionView? underway = host.Query.Exploration.ExpeditionForLodge(_target);
            if (isLodge && underway is { } active)
            {
                _expeditionLabel.Visible = true;
                string days = active.DaysLeft == 1
                    ? strings.Get("ui.exploration.expedition.days.one")
                    : strings.Format("ui.exploration.expedition.days.other", ("count", NumberFormat.Count(active.DaysLeft)));
                _expeditionLabel.Text = strings.Format(
                    "ui.exploration.inspector.expedition.underway",
                    ("region", RegionNames.Resolve(strings.Get, RegionNames.ForMap(host), active.TargetRegion)),
                    ("days", days));
            }
            else
            {
                _expeditionLabel.Visible = false;
            }
        }

        // Monument sites and finished monuments show the stage section; anything else hides it.
        // A monument's explanation has nothing to say (no production, no house), so its view collapses
        // and the section takes the expanding slot instead: the section follows the building's information
        // directly and the actions stay at the bottom. Anything else keeps the expanding explanation.
        bool isMonument = content.TryGetMonumentOf(building.Type, out _);
        if (_explain is not null)
        {
            _explain.SizeFlagsVertical = isMonument
                ? Control.SizeFlags.Fill
                : Control.SizeFlags.ExpandFill;
        }

        if (_monumentSection is not null)
        {
            _monumentSection.SizeFlagsVertical = isMonument
                ? Control.SizeFlags.ExpandFill
                : Control.SizeFlags.Fill;
            if (host is not null)
            {
                _monumentSection.Setup(host);
            }

            if (isMonument)
            {
                _monumentSection.ShowFor(_target);
            }
            else
            {
                _monumentSection.HideSection();
            }
        }
    }

    private void SetButtonsVisible(bool pause, bool priority, bool demolish, bool upgrade, bool festivals, bool expedition, bool lockToggle, bool chain)
    {
        if (_pauseButton is not null)
        {
            _pauseButton.Visible = pause;
        }

        if (_priorityButton is not null)
        {
            _priorityButton.Visible = priority;
        }

        if (_demolishButton is not null)
        {
            _demolishButton.Visible = demolish;
        }

        if (_upgradeButton is not null)
        {
            _upgradeButton.Visible = upgrade;
        }

        if (_festivalsButton is not null)
        {
            _festivalsButton.Visible = festivals;
        }

        if (_expeditionButton is not null)
        {
            _expeditionButton.Visible = expedition;
        }

        if (_lockButton is not null)
        {
            _lockButton.Visible = lockToggle;
        }

        if (_chainButton is not null)
        {
            _chainButton.Visible = chain;
        }
    }

    private void TogglePause()
    {
        if (_host is not { IsRunning: true } host || !_hasTarget)
        {
            return;
        }

        if (!host.Query.Map.TryGetBuilding(_target, out BuildingInfo building))
        {
            return;
        }

        bool paused = building.State == BuildingState.Paused;
        host.Enqueue(new SetBuildingPaused(_target, !paused));
        _secondsSinceRefresh = double.MaxValue;
    }

    private void TogglePriority()
    {
        if (_host is not { IsRunning: true } host || !_hasTarget)
        {
            return;
        }

        bool high = host.Query.Workforce.IsHighPriority(_target);
        host.Enqueue(new SetWorkforcePriority(_target, !high));
        _secondsSinceRefresh = double.MaxValue;
    }

    private void AskDemolish()
    {
        if (_host is not { IsRunning: true } host || !_hasTarget || _strings is null || _demolishConfirm is null)
        {
            return;
        }

        string name = host.Query.Map.TryGetBuilding(_target, out BuildingInfo building)
            ? BuildingDisplayName(host.Content, _strings, building.Type)
            : _strings.Get("ui.inspector.unknown");
        _demolishConfirm.Title = _strings.Format("ui.inspector.demolish.confirm_title", ("name", name));
        _demolishConfirm.DialogText = _strings.Get("ui.inspector.demolish.confirm_text");
        _demolishConfirm.GetOkButton().Text = _strings.Get("ui.inspector.demolish.confirm_ok");
        _demolishConfirm.GetCancelButton().Text = _strings.Get("ui.inspector.demolish.cancel");
        _demolishConfirm.PopupCentered();
    }

    private void DoDemolish()
    {
        if (_host is not { IsRunning: true } host || !_hasTarget)
        {
            return;
        }

        host.Enqueue(new Demolish(_target));
        GD.Print($"[Evenmere] Inspector: enqueued Demolish {_target.Value}.");
        Close();
    }

    private void DoUpgrade()
    {
        if (_host is not { IsRunning: true } host || !_hasTarget)
        {
            return;
        }

        if (!host.Query.Map.TryGetBuilding(_target, out BuildingInfo building))
        {
            return;
        }

        if (FindUpgrade(host.Content, building.Type) is { } upgrade)
        {
            host.Enqueue(new UpgradeBuilding(_target, upgrade));
            GD.Print($"[Evenmere] Inspector: enqueued UpgradeBuilding {_target.Value} to {host.Content.NameOf(upgrade)}.");
        }

        _secondsSinceRefresh = double.MaxValue;
    }

    private void ToggleLock()
    {
        if (_host is not { IsRunning: true } host || !_hasTarget)
        {
            return;
        }

        if (!host.Query.Population.TryGetHouse(_target, out HouseInfo house))
        {
            return;
        }

        host.Enqueue(new SetHouseUpgradeLock(_target, !house.UpgradeLocked));
        _secondsSinceRefresh = double.MaxValue;
    }

    /// <summary>Opens the festival panel on the inspected ground (the handler behind the Festivals button).</summary>
    private void OpenFestivals()
    {
        if (!_hasTarget)
        {
            return;
        }

        if (GetNodeOrNull<FestivalPanel>(FestivalPath) is { } festivals)
        {
            festivals.Open(_target);
            GD.Print($"[Evenmere] Inspector: opened festivals on ground {_target.Value}.");
        }
    }

    /// <summary>Opens the expedition picker on the inspected lodge (the handler behind the expedition button).</summary>
    private void OpenExpeditions()
    {
        if (!_hasTarget)
        {
            return;
        }

        if (GetNodeOrNull<ExpeditionPanel>(ExpeditionPath) is { } expeditions)
        {
            expeditions.Open(_target);
            GD.Print($"[Evenmere] Inspector: opened expeditions on lodge {_target.Value}.");
        }
    }

    private static bool HasTag(System.Collections.Immutable.ImmutableArray<string> tags, string tag)
    {
        foreach (string candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
    private void OpenChain()
    {
        if (_host is not { IsRunning: true } host || !_hasTarget)
        {
            return;
        }

        if (!host.Query.Production.TryGet(_target, out ProductionInfo production))
        {
            return;
        }

        if (production.Recipe.Value >= host.Content.Recipes.Count)
        {
            return;
        }

        ImmutableArray<GoodAmount> outputs = host.Content.Get(production.Recipe).Outputs;
        if (outputs.IsEmpty)
        {
            return;
        }

        if (GetNodeOrNull<ChainViewer>(ChainPath) is { } chains)
        {
            chains.Show(outputs[0].Good);
            GD.Print($"[Evenmere] Inspector: opened chain of {host.Content.NameOf(outputs[0].Good)} from building {_target.Value}.");
        }
    }

    /// <summary>Names a building for its subject-link button, falling back to the raw ID when it is gone.</summary>
    private string BuildingNameOf(EntityId id)
    {
        if (_host is { IsRunning: true } host && _strings is not null
            && host.Query.Map.TryGetBuilding(id, out BuildingInfo info))
        {
            return BuildingDisplayName(host.Content, _strings, info.Type);
        }

        return $"#{id.Value}";
    }

    /// <summary>Reads a storehouse's reach in tiles from content, or 0 when it is gone.</summary>
    private int StorehouseReachOf(EntityId storehouse)
    {
        if (_host is { IsRunning: true } host && host.Query.Map.TryGetBuilding(storehouse, out BuildingInfo info))
        {
            return host.Content.Get(info.Type).Storage?.Reach ?? 0;
        }

        return 0;
    }

    private void OnLinkBuilding(EntityId building)
    {
        _rig?.FocusOnTile(new Vector2I(0, 0));
        if (_host is { IsRunning: true } host && host.Query.Map.TryGetBuilding(building, out BuildingInfo info))
        {
            _rig?.FocusOnTile(new Vector2I(info.Origin.X, info.Origin.Y));
            GD.Print($"[Evenmere] Inspector: subject link to building {building.Value}; camera focused, inspector opened.");
        }

        Open(building);
    }

    private static BuildingTypeId? FindUpgrade(ContentDb content, BuildingTypeId current)
    {
        for (int i = 0; i < content.Buildings.Count; i++)
        {
            var candidate = BuildingTypeId.From(checked((ushort)i));
            BuildingDef def = content.Buildings.Get(candidate);
            if (def.UpgradeOf.HasValue && def.UpgradeOf.Value.Equals(current))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string BuildingDisplayName(ContentDb content, UiStrings strings, BuildingTypeId type)
    {
        string key = $"building.{content.NameOf(type)}.name";
        string text = strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? content.NameOf(type) : text;
    }
}
