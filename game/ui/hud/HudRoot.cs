using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Explain;
using Evenmere.Sim;
using Godot;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// The root of the HUD scene and of the shared UI theme: it carries <c>evenmere_theme.tres</c> (set on the node in
/// <c>Hud.tscn</c>, not in project.godot), so every panel placed under it, now or later, takes the same look. It owns the
/// <see cref="HudViewModel"/>, builds the speed buttons and the chips from it, and re-reads it whenever it refreshes. It also
/// applies the UI scale and turns the <c>speed_*</c> input actions into speed changes.
/// </summary>
public sealed partial class HudRoot : Control
{
    private const string PauseAction = "speed_pause";

    // Added to project.godot by M1-17 and bound to the pad's Back/Select button; the HUD ignores it while the action is missing.
    private const string FocusHudAction = "ui_focus_hud";

    // The four player-facing speeds and the input action that selects each. X16 is a debug-only speed with no button.
    private static readonly SpeedOption[] _speedOptions =
    [
        new(SimSpeed.Paused, PauseAction),
        new(SimSpeed.X1, "speed_1"),
        new(SimSpeed.X2, "speed_2"),
        new(SimSpeed.X4, "speed_3"),
    ];

    // The containers whose children Build creates, by unique name in Hud.tscn.
    private static readonly string[] _builtContainers = ["%SpeedBar", "%StatsFlow", "%GoodsFlow"];

    private readonly List<HudChip> _residentChips = [];
    private readonly List<HudChip> _goodChips = [];
    private GoodId[] _stripGoods = [];
    private PanelContainer? _explainPopup;
    private ExplanationView? _explainView;
    private PanelContainer? _workforceTipPopup;
    private Label? _workforceTipLabel;
    private string _debugWorkforceTier = string.Empty;
    private Button[] _speedButtons = [];
    private SimHost? _host;
    private ContentDb? _builtFor;
    private HudViewModel? _viewModel;
    private Label? _dateLabel;
    private HudChip? _knowledgeChip;
    private HudChip? _cultureChip;
    private SimSpeed _resumeSpeed = SimSpeed.X1;
    private float _uiScaleFactor = UiScale.Default;

    /// <summary>
    /// Gets or sets whether the keyboard 1-3 speed hotkeys stop changing the speed. The build menu sets it while
    /// open, so the number keys pick menu entries instead (M1-23). The speed buttons keep working, and so does
    /// pause on Space; only the 1-3 hotkeys are suspended.
    /// </summary>
    public static bool SpeedHotkeysSuspended { get; set; }

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../../SimHost");

    /// <summary>
    /// Gets or sets the UI scale, from <see cref="UiScale.Minimum"/> to <see cref="UiScale.Maximum"/>. It is applied to the root
    /// window's <c>content_scale_factor</c>, so it scales every control in the game. There is no settings screen yet
    /// (M4-05); until then it is set here or with the <c>--ui-scale=N</c> launch argument.
    /// </summary>
    [Export(PropertyHint.Range, "0.75,1.5,0.05")]
    public float UiScaleFactor
    {
        get => _uiScaleFactor;
        set
        {
            _uiScaleFactor = UiScale.Clamp(value);
            if (IsInsideTree())
            {
                GetWindow().ContentScaleFactor = _uiScaleFactor;
            }
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (UiScale.TryParseArgument(OS.GetCmdlineUserArgs(), out float requested))
        {
            _uiScaleFactor = requested;
        }

        GetWindow().ContentScaleFactor = _uiScaleFactor;

        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] HUD: no SimHost at '{HostPath}'; the HUD stays empty.");
            return;
        }

        _dateLabel = GetNode<Label>("%DateLabel");
        _host.EventsDrained += OnEventsDrained;

        // Debug screenshots only: --hud-workforce-tip=<tier> shows that tier's workforce tooltip in a popup
        // once the HUD is built (M1-31 evidence), so the tooltip can be captured without hovering the mouse.
        if (OS.IsDebugBuild())
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--hud-workforce-tip=", StringComparison.Ordinal))
                {
                    _debugWorkforceTier = arg["--hud-workforce-tip=".Length..];
                }
            }
        }

        // The host starts the world in Main._Ready, which runs after this node's, so the HUD is built from the host's content
        // and locale on the first frame the simulation is running (see _Process).
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_host is not null)
        {
            _host.EventsDrained -= OnEventsDrained;
        }

        if (_viewModel is not null)
        {
            _viewModel.Refreshed -= OnRefreshed;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Built again if the host starts another world, because that loads its content afresh.
        if (_host is { IsRunning: true } host && !ReferenceEquals(_builtFor, host.Content))
        {
            Build(host);
        }

        _viewModel?.Advance(delta);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        Control? focused = GetViewport().GuiGetFocusOwner();
        if (@event.IsEcho() || focused is LineEdit or TextEdit)
        {
            return;
        }

        // Handled here rather than in _UnhandledInput: after a click, a speed button holds focus and would otherwise take
        // Space (ui_accept) for itself, so the pause key would press that button instead of pausing.
        // The 1-3 hotkeys rest while the build menu is open (SpeedHotkeysSuspended): the menu uses those keys to
        // pick entries, and the speed buttons stay clickable throughout.
        foreach (SpeedOption option in _speedOptions)
        {
            if (SpeedHotkeysSuspended && option.Speed is SimSpeed.X1 or SimSpeed.X2 or SimSpeed.X4)
            {
                continue;
            }
            if (InputMap.HasAction(option.Action) && @event.IsActionPressed(option.Action))
            {
                SelectSpeed(option.Speed);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Keyboard and gamepad focus. ui_focus_hud (the pad's Back/Select) enters the HUD, or leaves it if it already has focus.
        // Besides that, with nothing focused ui_focus_next (Tab) enters the HUD, and ui_cancel (Escape, pad B) leaves it.
        Control? hudFocus = focused is not null && IsAncestorOf(focused) ? focused : null;
        if (InputMap.HasAction(FocusHudAction) && @event.IsActionPressed(FocusHudAction))
        {
            if (hudFocus is not null)
            {
                hudFocus.ReleaseFocus();
            }
            else
            {
                FocusHud();
            }

            GetViewport().SetInputAsHandled();
        }
        else if (focused is null && @event.IsActionPressed("ui_focus_next"))
        {
            FocusHud();
            GetViewport().SetInputAsHandled();
        }
        else if (hudFocus is not null && @event.IsActionPressed("ui_cancel"))
        {
            hudFocus.ReleaseFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Gives the keyboard and gamepad focus to the HUD, on the button of the current speed.</summary>
    public void FocusHud()
    {
        if (_speedButtons.Length == 0)
        {
            return;
        }

        Button target = Array.Find(_speedButtons, static button => button.ButtonPressed) ?? _speedButtons[0];
        target.GrabFocus();
    }

    private void OnEventsDrained(IReadOnlyList<SimEvent> events) => _viewModel?.MarkDirty();

    /// <summary>Builds the view-model, the speed buttons and the chips from the host's content and locale, replacing any earlier build.</summary>
    private void Build(SimHost host)
    {
        ClearBuilt();

        var strings = new UiStrings(host.Locale);
        _builtFor = host.Content;
        _viewModel = new HudViewModel(new SimHostHudSource(host), host.Content, strings);

        BuildSpeedButtons(strings);
        BuildChips(host, strings, _viewModel);
        _viewModel.Refreshed += OnRefreshed;
    }

    private void ClearBuilt()
    {
        if (_viewModel is not null)
        {
            _viewModel.Refreshed -= OnRefreshed;
            _viewModel = null;
        }

        foreach (string container in _builtContainers)
        {
            Node node = GetNode(container);
            foreach (Node child in node.GetChildren())
            {
                node.RemoveChild(child);
                child.QueueFree();
            }
        }

        _residentChips.Clear();
        _goodChips.Clear();
        _stripGoods = [];
        if (_workforceTipPopup is not null)
        {
            RemoveChild(_workforceTipPopup);
            _workforceTipPopup.QueueFree();
            _workforceTipPopup = null;
            _workforceTipLabel = null;
        }
        if (_explainPopup is not null)
        {
            RemoveChild(_explainPopup);
            _explainPopup.QueueFree();
            _explainPopup = null;
            _explainView = null;
        }

        _speedButtons = [];
        _knowledgeChip = null;
        _cultureChip = null;
    }

    private void BuildSpeedButtons(UiStrings strings)
    {
        HBoxContainer speedBar = GetNode<HBoxContainer>("%SpeedBar");
        _speedButtons = new Button[_speedOptions.Length];

        for (int i = 0; i < _speedOptions.Length; i++)
        {
            SpeedOption option = _speedOptions[i];
            string key = HudViewModel.SpeedKey(option.Speed);

            var button = new Button
            {
                Name = $"Speed_{key}",
                Text = strings.Get($"ui.hud.speed.{key}"),
                ToggleMode = true,
                FocusMode = FocusModeEnum.All,
                CustomMinimumSize = new Vector2(option.Speed == SimSpeed.Paused ? 84f : 56f, 44f),
                TooltipText = TooltipFor(strings, option, key),
            };

            SimSpeed speed = option.Speed;
            button.Pressed += () => SelectSpeed(speed);
            speedBar.AddChild(button);
            _speedButtons[i] = button;
        }
    }

    private static string TooltipFor(UiStrings strings, SpeedOption option, string key)
    {
        string tip = strings.Get($"ui.hud.speed.tip.{key}");
        if (!InputMap.HasAction(option.Action))
        {
            return tip;
        }

        // Show the keyboard key if the action has one, otherwise whatever it is bound to (a pad button).
        Godot.Collections.Array<InputEvent> events = InputMap.ActionGetEvents(option.Action);
        InputEvent? hint = events.FirstOrDefault(static e => e is InputEventKey) ?? events.FirstOrDefault();
        return hint is null
            ? tip
            : strings.Format("ui.hud.speed.tip_with_key", ("tip", tip), ("key", KeyHint(hint)));
    }

    private static string KeyHint(InputEvent inputEvent) => inputEvent switch
    {
        InputEventKey { PhysicalKeycode: not Key.None } key => key.AsTextPhysicalKeycode(),
        InputEventKey key => key.AsTextKeycode(),
        _ => inputEvent.AsText(),
    };

    private void BuildChips(SimHost host, UiStrings strings, HudViewModel viewModel)
    {
        HFlowContainer stats = GetNode<HFlowContainer>("%StatsFlow");
        HFlowContainer goods = GetNode<HFlowContainer>("%GoodsFlow");

        // Sample numbers as wide as the real ones will get, so a chip keeps its width as digits change. Resident
        // values carry the workforce, sampled at slice scale ("88 · 88 free / 88 jobs · 88 in lower-tier jobs"):
        // big enough to hold the longest new form (a free line with workers below) steady, small enough that both
        // tier chips share the top row with the date and speed buttons at 1280 × 800 instead of wrapping the bar
        // to three rows. Shorter forms (short, covered, idle) leave spare room inside the same width.
        // The top row holds the date, the speeds and the tier chips only; Knowledge and Culture sit at the head of
        // the goods row, so the bar stays two rows tall at 1280 × 800 with two tiers unlocked.
        const string WidestResidents = "88 · 88 free / 88 jobs · 88 in lower-tier jobs";
        // Goods stay plain counts, sampled at slice scale ("88"): the names dominate these chips, so the
        // sample only steadies two-digit stocks; with Knowledge and Culture heading the goods row, a wider
        // sample would wrap the strip to a third bar row at 1280 × 800. Three-digit stocks widen their chip
        // past the sample instead of jumping the whole row.
        const string WidestCount = "88";
        string widestRate = strings.Format("ui.hud.per_day", ("value", "88.8"));

        for (int i = 0; i < viewModel.Residents.Length; i++)
        {
            var chip = HudChip.Create(withTrend: false, WidestResidents);
            stats.AddChild(chip);
            _residentChips.Add(chip);
        }

        _knowledgeChip = HudChip.Create(withTrend: false, widestRate);
        goods.AddChild(_knowledgeChip);
        _cultureChip = HudChip.Create(withTrend: false, widestRate);
        goods.AddChild(_cultureChip);

        for (int i = 0; i < viewModel.Goods.Length; i++)
        {
            var chip = HudChip.Create(withTrend: true, WidestCount);
            chip.FocusMode = FocusModeEnum.All;
            goods.AddChild(chip);
            _goodChips.Add(chip);
        }

        BuildExplainPopup(host, strings);
        HookGoodChips();
        BuildWorkforceTipPopup();

        OnRefreshed();
    }

    private void OnRefreshed()
    {
        HudViewModel? viewModel = _viewModel;
        if (viewModel is null || _dateLabel is null || _knowledgeChip is null || _cultureChip is null)
        {
            return;
        }

        _dateLabel.Text = viewModel.DateText;
        ShowSpeed(viewModel.Speed);
        if (viewModel.Speed != SimSpeed.Paused)
        {
            _resumeSpeed = viewModel.Speed;
        }

        // The chips are built for every tier, but the view-model lists only the unlocked ones; the rest stay hidden.
        for (int i = 0; i < _residentChips.Count; i++)
        {
            if (i < viewModel.Residents.Length)
            {
                HudStat stat = viewModel.Residents[i];
                _residentChips[i].Visible = true;
                _residentChips[i].SetValue(stat.Label, stat.Value, stat.Tooltip);
                _residentChips[i].SetWarning(stat.IsWarning);
            }
            else
            {
                _residentChips[i].Visible = false;
            }
        }

        _knowledgeChip.SetValue(viewModel.Knowledge.Label, viewModel.Knowledge.Value, viewModel.Knowledge.Tooltip);
        _cultureChip.SetValue(viewModel.Culture.Label, viewModel.Culture.Value, viewModel.Culture.Tooltip);
        ShowDebugWorkforceTip();

        for (int i = 0; i < _goodChips.Count; i++)
        {
            HudGood good = viewModel.Goods[i];
            _goodChips[i].SetValue(good.Name, good.Stock, good.Tooltip);
            _goodChips[i].SetTrend(good.Trend);
        }
    }

    /// <summary>Changes the game speed. Choosing pause while already paused resumes the speed the game had before.</summary>
    private void SelectSpeed(SimSpeed speed)
    {
        if (_host is null)
        {
            return;
        }

        SimSpeed target = speed == SimSpeed.Paused && _host.Speed == SimSpeed.Paused ? _resumeSpeed : speed;
        _host.Speed = target;
        if (target != SimSpeed.Paused)
        {
            _resumeSpeed = target;
        }

        // Show it at once; the view-model catches up on its next refresh (at most a quarter of a second later).
        ShowSpeed(_host.Speed);
        _viewModel?.MarkDirty();
    }

    private void ShowSpeed(SimSpeed speed)
    {
        for (int i = 0; i < _speedButtons.Length; i++)
        {
            _speedButtons[i].SetPressedNoSignal(_speedOptions[i].Speed == speed);
        }
    }

    // The goods tooltip (M1-24): hovering or focusing a chip shows ExplainGood in a compact
    // two-level ExplanationView. The popup is built once per world and hidden on leave.
    private void BuildExplainPopup(SimHost host, UiStrings strings)
    {
        _stripGoods = [.. HudViewModel.DefaultStripGoods(host.Content)];
        _explainPopup = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(400f, 280f),
            Position = new Vector2(420f, 140f),
        };
        _explainView = new ExplanationView { MaxDepth = 2 };
        _explainPopup.AddChild(_explainView);
        AddChild(_explainPopup);
        _explainView.Setup(host.Content, strings, host.Query.Explain.ExplainGood);
    }

    private void HookGoodChips()
    {
        for (int i = 0; i < _goodChips.Count; i++)
        {
            int index = i;
            HudChip chip = _goodChips[i];
            chip.MouseEntered += () => ShowGoodTip(index);
            chip.MouseExited += HideGoodTip;
            chip.FocusEntered += () => ShowGoodTip(index);
            chip.FocusExited += HideGoodTip;
        }
    }

    private void ShowGoodTip(int index)
    {
        if (_host is not { IsRunning: true } host || _explainPopup is null || _explainView is null)
        {
            return;
        }

        if ((uint)index >= (uint)_stripGoods.Length)
        {
            return;
        }

        GoodId good = _stripGoods[index];
        _explainView.Setup(host.Content, new UiStrings(host.Locale), host.Query.Explain.ExplainGood);
        _explainView.MaxDepth = 2;
        _explainView.ShowRoot(host.Query.Explain.ExplainGood(good));
        _explainPopup.Visible = true;
        GD.Print($"[Evenmere] HUD: explain tooltip for {host.Content.NameOf(good)}.");
    }

    private void HideGoodTip()
    {
        if (_explainPopup is not null)
        {
            _explainPopup.Visible = false;
        }
    }

    private void BuildWorkforceTipPopup()
    {
        _workforceTipPopup = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(460f, 0f),
            Position = new Vector2(420f, 140f),
        };
        _workforceTipLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _workforceTipPopup.AddChild(_workforceTipLabel);
        AddChild(_workforceTipPopup);
    }

    // Shows the --hud-workforce-tip tier's tooltip text in the popup, refreshed with the HUD so it always
    // reads the live supply and demand. Nothing when the flag is empty or the tier is hidden (locked, or no jobs
    // and no residents).
    private void ShowDebugWorkforceTip()
    {
        if (_debugWorkforceTier.Length == 0 || _viewModel is null
            || _workforceTipPopup is null || _workforceTipLabel is null || _host is not { IsRunning: true } host)
        {
            return;
        }

        string wanted = new UiStrings(host.Locale).TierName(_debugWorkforceTier);
        foreach (HudStat stat in _viewModel.Residents)
        {
            if (string.Equals(stat.Label, wanted, StringComparison.Ordinal))
            {
                _workforceTipLabel.Text = stat.Tooltip;
                _workforceTipPopup.Visible = true;
                return;
            }
        }

        _workforceTipPopup.Visible = false;
    }

    /// <summary>Shows the goods tooltip for a named good (debug screenshots only).</summary>
    /// <param name="goodName">The string ID, such as <c>bread</c>.</param>
    public void DebugShowGoodTip(string goodName)
    {
        for (int i = 0; i < _stripGoods.Length; i++)
        {
            if (_host is { IsRunning: true } host && string.Equals(host.Content.NameOf(_stripGoods[i]), goodName, StringComparison.Ordinal))
            {
                ShowGoodTip(i);
                return;
            }
        }
    }

    private readonly record struct SpeedOption(SimSpeed Speed, string Action);
}
