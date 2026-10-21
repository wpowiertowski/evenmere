using Evenmere.Game.Host;
using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim.Persistence;
using Godot;

namespace Evenmere.Game.Ui.Menus;

/// <summary>
/// The pause menu: a small focusable Menu button in the corner while playing, opening on demand (or on
/// <c>tool_cancel</c> when no open panel or tool handled it first) into Resume, Save, Load, Settings and Quit to
/// Menu. The simulation pauses while it is open and resumes at the previous speed on resume. Saving and loading go
/// through <see cref="SaveService"/>, the slot list comes from <see cref="SaveSlots"/>, and an autosave runs every
/// <see cref="MenuText.AutosaveDays"/> in-game days into the single <c>autosave</c> slot, between ticks.
/// </summary>
public sealed partial class PauseMenu : Control
{
    private const float HashFlashSeconds = 5f;

    private SimHost? _host;
    private bool _inMenu = true;
    private bool _open;
    private SimSpeed _resumeSpeed = SimSpeed.X1;
    private bool _autosaveBaselineSet;
    private long _autosavePeriod;
    private double _hashTimeLeft;
    private UiStrings? _strings;

    private Button? _menuButton;
    private ColorRect? _dim;
    private PanelContainer? _panel;
    private Label? _panelTitle;
    private VBoxContainer? _buttons;
    private SlotListDialog? _dialog;
    private SettingsStub? _settings;
    private LoadReportNote? _report;
    private Label? _hashLabel;

    /// <summary>Gets or sets the path to the <see cref="SimHost"/> node, relative to this node.</summary>
    [Export]
    public NodePath HostPath { get; set; } = new("../../SimHost");

    /// <summary>Gets or sets the command-log recorder the saves rebind. It is set by the scene root.</summary>
    public CommandLogRecorder? Recorder { get; set; }

    /// <summary>Raised after quitting to the menu, so the scene root can show the main menu.</summary>
    public event Action? QuitToMenuRequested;

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _host = GetNodeOrNull<SimHost>(HostPath);
        if (_host is null)
        {
            GD.PushError($"[Evenmere] pause menu: no SimHost at '{HostPath}'; the menu stays empty.");
            return;
        }

        Build();
        RefreshTexts();
        CloseAll();
    }

    /// <summary>Tells the menu whether the game is at the main menu: at the menu the corner button hides.</summary>
    /// <param name="inMenu"><see langword="true"/> at the main menu, <see langword="false"/> while playing.</param>
    public void SetMenuMode(bool inMenu)
    {
        _inMenu = inMenu;
        _autosaveBaselineSet = false;
        if (inMenu)
        {
            CloseAll();
        }

        UpdateMenuButton();
    }

    /// <summary>Flashes the debug state-hash label after a save, in debug builds only.</summary>
    /// <param name="hash">The state hash, as 16 lowercase hex digits.</param>
    /// <param name="strings">The display text.</param>
    public void NotifySaved(string hash, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(strings);

        _autosavePeriod = AutosavePeriod();
        FlashHash(strings.Format("ui.hash.saved", ("hash", hash)));
    }

    /// <summary>Flashes the debug state-hash label after a load and shows the load report when it is not clean.</summary>
    /// <param name="hash">The state hash, as 16 lowercase hex digits.</param>
    /// <param name="report">What loading changed or dropped.</param>
    /// <param name="strings">The display text.</param>
    public void NotifyLoaded(string hash, SaveReport report, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(strings);

        _autosavePeriod = AutosavePeriod();
        _autosaveBaselineSet = true;
        FlashHash(strings.Format("ui.hash.loaded", ("hash", hash)));
        _report?.ShowReport(strings, report);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        UpdateMenuButton();
        if (_hashLabel is not null && _hashTimeLeft > 0.0)
        {
            _hashTimeLeft -= delta;
            if (_hashTimeLeft <= 0.0)
            {
                _hashLabel.Visible = false;
            }
        }

        RunAutosave();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_inMenu || _host is not { IsRunning: true } || @event.IsEcho())
        {
            return;
        }

        if (InputMap.HasAction("tool_cancel") && @event.IsActionPressed("tool_cancel"))
        {
            if (_open)
            {
                Resume();
            }
            else
            {
                Open();
            }

            GetViewport().SetInputAsHandled();
        }
    }

    private void Build()
    {
        var menuButton = new Button { FocusMode = FocusModeEnum.All };
        menuButton.SetAnchorsPreset(LayoutPreset.BottomRight);
        menuButton.OffsetLeft = -140f;
        menuButton.OffsetTop = -64f;
        menuButton.OffsetRight = -16f;
        menuButton.OffsetBottom = -16f;
        menuButton.Pressed += Open;
        AddChild(menuButton);
        _menuButton = menuButton;

        var dim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.45f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        _dim = dim;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.CustomMinimumSize = new Vector2(320f, 0);
        panel.Visible = false;
        AddChild(panel);
        _panel = panel;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);

        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        margin.AddChild(top);

        var title = new Label();
        top.AddChild(title);
        _panelTitle = title;

        var buttons = new VBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        top.AddChild(buttons);
        _buttons = buttons;

        foreach (string name in new[] { "Resume", "Save", "Load", "Settings", "QuitToMenu" })
        {
            var button = new Button { Name = name, FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            buttons.AddChild(button);
        }

        GetButton("Resume").Pressed += Resume;
        GetButton("Save").Pressed += OpenSave;
        GetButton("Load").Pressed += OpenLoad;
        GetButton("Settings").Pressed += OpenSettings;
        GetButton("QuitToMenu").Pressed += QuitToMenu;

        var dialog = new SlotListDialog();
        AddChild(dialog);
        dialog.LoadRequested += OnLoadSlot;
        dialog.SaveRequested += OnSaveSlot;
        _dialog = dialog;

        var settings = new SettingsStub();
        AddChild(settings);
        _settings = settings;

        var report = new LoadReportNote();
        AddChild(report);
        _report = report;

        var hash = new Label
        {
            Visible = false,
            Modulate = new Color(1f, 1f, 1f, 0.9f),
        };
        hash.SetAnchorsPreset(LayoutPreset.BottomLeft);
        hash.OffsetLeft = 16f;
        hash.OffsetTop = -48f;
        hash.OffsetRight = 600f;
        hash.OffsetBottom = -16f;
        AddChild(hash);
        _hashLabel = hash;
    }

    private Button GetButton(string name) => _buttons?.GetNode<Button>(name) ?? throw new InvalidOperationException("The pause menu is not built.");

    private void RefreshTexts()
    {
        // The strings follow the running content's locale; while at the menu (no simulation) the texts wait for the
        // first game to start, since the menu button is hidden there anyway.
        if (_host is not { IsRunning: true } host || _menuButton is null)
        {
            return;
        }

        _strings = new UiStrings(host.Locale);

        UiStrings strings = _strings;
        _menuButton.Text = strings.Get("ui.pause.menu");
        _menuButton.TooltipText = strings.Get("ui.pause.menu.tip");
        if (_panelTitle is not null)
        {
            _panelTitle.Text = strings.Get("ui.pause.title");
        }
        SetButton("Resume", "ui.pause.resume", "ui.pause.resume.tip");
        SetButton("Save", "ui.pause.save", "ui.pause.save.tip");
        SetButton("Load", "ui.pause.load", "ui.pause.load.tip");
        SetButton("Settings", "ui.pause.settings", "ui.pause.settings.tip");
        SetButton("QuitToMenu", "ui.pause.quit_to_menu", "ui.pause.quit_to_menu.tip");
    }

    private void SetButton(string name, string textKey, string tipKey)
    {
        if (_strings is null)
        {
            return;
        }

        Button button = GetButton(name);
        button.Text = _strings.Get(textKey);
        button.TooltipText = _strings.Get(tipKey);
    }

    private void UpdateMenuButton()
    {
        if (_menuButton is null)
        {
            return;
        }

        _menuButton.Visible = !_inMenu && _host is { IsRunning: true } && !_open;
    }

    private void Open()
    {
        if (_host is not { IsRunning: true })
        {
            return;
        }

        RefreshTexts();
        _resumeSpeed = _host.Speed;
        _host.Speed = SimSpeed.Paused;
        _open = true;
        if (_dim is not null)
        {
            _dim.Visible = true;
        }

        if (_panel is not null)
        {
            _panel.Visible = true;
        }

        UpdateMenuButton();
        GetButton("Resume").GrabFocus();
    }

    private void Resume()
    {
        _open = false;
        CloseOverlays();
        if (_host is { IsRunning: true } host)
        {
            host.Speed = _resumeSpeed == SimSpeed.Paused ? SimSpeed.X1 : _resumeSpeed;
        }

        UpdateMenuButton();
        _menuButton?.GrabFocus();
    }

    private void CloseOverlays()
    {
        if (_dim is not null)
        {
            _dim.Visible = false;
        }

        if (_panel is not null)
        {
            _panel.Visible = false;
        }

        _dialog?.Dismiss();
        _settings?.Dismiss();
        _report?.Dismiss();
    }

    private void CloseAll()
    {
        _open = false;
        CloseOverlays();
    }

    private UiStrings Strings() => _strings ?? new UiStrings(_host!.Locale);

    private void OpenSave()
    {
        if (_host is not { IsRunning: true } || _dialog is null)
        {
            return;
        }

        if (_panel is not null)
        {
            _panel.Visible = false;
        }

        _dialog.ShowSave(Strings(), SaveSlots.ListSlots(), MenuText.SuggestName(_host, Strings()));
    }

    private void OpenLoad()
    {
        if (_dialog is null)
        {
            return;
        }

        if (_panel is not null)
        {
            _panel.Visible = false;
        }

        _dialog.ShowLoad(Strings(), SaveSlots.ListSlots());
    }

    private void OpenSettings() => _settings?.ShowSettings(Strings());

    private void OnSaveSlot(string slotId, string name)
    {
        if (_host is not { IsRunning: true } || Recorder is null || _dialog is null)
        {
            return;
        }

        try
        {
            string hash = SaveService.Save(_host, Recorder, slotId, name);
            _dialog.Dismiss();
            _dialog.RefreshSlots(SaveSlots.ListSlots());
            NotifySaved(hash, Strings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PushWarning($"[Evenmere] save failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private void OnLoadSlot(string slotId)
    {
        if (_host is null || Recorder is null || _dialog is null)
        {
            return;
        }

        try
        {
            (string hash, SaveReport report) = SaveService.Load(_host, Recorder, slotId);
            _dialog.Dismiss();
            Resume();
            NotifyLoaded(hash, report, Strings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PushWarning($"[Evenmere] load failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private void QuitToMenu()
    {
        Recorder?.EndSlot();
        _host?.UnloadGame();
        CloseAll();
        SetMenuMode(true);
        QuitToMenuRequested?.Invoke();
    }

    private void FlashHash(string text)
    {
        if (!OS.IsDebugBuild() || _hashLabel is null)
        {
            return;
        }

        _hashLabel.Text = text;
        _hashLabel.Visible = true;
        _hashTimeLeft = HashFlashSeconds;
    }

    private long AutosavePeriod() =>
        _host is { IsRunning: true } host ? host.Clock.Day / MenuText.AutosaveDays : 0;

    // The autosave writes the single autosave slot between ticks (this runs in _Process, never mid-tick), naming it
    // like a manual save. It never fires in the menu or while paused in the pause menu.
    private void RunAutosave()
    {
        if (_inMenu || _open || _host is not { IsRunning: true } || Recorder is null)
        {
            return;
        }

        long period = AutosavePeriod();
        if (!_autosaveBaselineSet)
        {
            _autosavePeriod = period;
            _autosaveBaselineSet = true;
            return;
        }

        if (period <= _autosavePeriod)
        {
            return;
        }

        try
        {
            string name = Strings().Get("ui.slot.autosave") + " — " + MenuText.SuggestName(_host, Strings());
            string hash = SaveService.Save(_host, Recorder, SaveSlots.AutosaveSlot, name);
            _autosavePeriod = period;
            FlashHash(Strings().Format("ui.hash.saved", ("hash", hash)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PushWarning($"[Evenmere] autosave failed: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }
}
