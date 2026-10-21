using Evenmere.Content;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
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
    private Button[] _speedButtons = [];
    private SimHost? _host;
    private ContentDb? _builtFor;
    private HudViewModel? _viewModel;
    private Label? _dateLabel;
    private HudChip? _knowledgeChip;
    private HudChip? _cultureChip;
    private SimSpeed _resumeSpeed = SimSpeed.X1;
    private float _uiScaleFactor = UiScale.Default;

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
        foreach (SpeedOption option in _speedOptions)
        {
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
        BuildChips(strings, _viewModel);
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

    private void BuildChips(UiStrings strings, HudViewModel viewModel)
    {
        HFlowContainer stats = GetNode<HFlowContainer>("%StatsFlow");
        HFlowContainer goods = GetNode<HFlowContainer>("%GoodsFlow");

        // Sample numbers as wide as the real ones will get, so a chip keeps its width as digits change.
        const string WidestCount = "8,888";
        string widestRate = strings.Format("ui.hud.per_day", ("value", "888.8"));

        for (int i = 0; i < viewModel.Residents.Length; i++)
        {
            var chip = HudChip.Create(withTrend: false, WidestCount);
            stats.AddChild(chip);
            _residentChips.Add(chip);
        }

        _knowledgeChip = HudChip.Create(withTrend: false, widestRate);
        stats.AddChild(_knowledgeChip);
        _cultureChip = HudChip.Create(withTrend: false, widestRate);
        stats.AddChild(_cultureChip);

        for (int i = 0; i < viewModel.Goods.Length; i++)
        {
            var chip = HudChip.Create(withTrend: true, WidestCount);
            goods.AddChild(chip);
            _goodChips.Add(chip);
        }

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
            }
            else
            {
                _residentChips[i].Visible = false;
            }
        }

        _knowledgeChip.SetValue(viewModel.Knowledge.Label, viewModel.Knowledge.Value, viewModel.Knowledge.Tooltip);
        _cultureChip.SetValue(viewModel.Culture.Label, viewModel.Culture.Value, viewModel.Culture.Tooltip);

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

    private readonly record struct SpeedOption(SimSpeed Speed, string Action);
}
