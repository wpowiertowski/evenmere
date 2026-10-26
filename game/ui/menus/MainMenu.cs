using System.Globalization;
using Evenmere.Content;
using Evenmere.Content.Locale;
using Evenmere.Content.Maps;
using Evenmere.Game.Host;
using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Menus.NewGame;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim.Persistence;
using Godot;

namespace Evenmere.Game.Ui.Menus;

/// <summary>
/// The main menu over the empty scene: the title, the seed row (shown, defaulting to 42, with a button that picks a
/// random seed with the system RNG — the game layer may use it, the sim may not), New Game on the starter island,
/// Continue for the most recent save, Load for the slot list, a settings stub and Quit. It shows nothing until the
/// scene root hands it the host; starting, continuing or loading a game raises <see cref="PlayStarted"/>.
/// </summary>
public sealed partial class MainMenu : Control
{
    private const float HashFlashSeconds = 5f;

    private SimHost? _host;
    private UiStrings? _strings;
    private double _hashTimeLeft;
    private bool _waitingForReportDismiss;

    private Label? _title;
    private Label? _subtitle;
    private Label? _seedLabel;
    private LineEdit? _seedEdit;
    private Button? _seedButton;
    private Button? _newGameButton;
    private Button? _continueButton;
    private Button? _loadButton;
    private Button? _settingsButton;
    private Button? _quitButton;
    private Label? _errorLabel;
    private Label? _hashLabel;
    private SlotListDialog? _dialog;
    private SettingsStub? _settings;
    private LoadReportNote? _report;
    private NewGameScreen? _newGame;

    /// <summary>Gets or sets the simulation host. It is set by the scene root before the menu is shown.</summary>
    public SimHost? Host { get; set; }

    /// <summary>Gets or sets the command-log recorder the saves rebind. It is set by the scene root.</summary>
    public CommandLogRecorder? Recorder { get; set; }

    /// <summary>Raised after a new game starts or a save loads, so the scene root can hide the menu.</summary>
    public event Action? PlayStarted;

    /// <inheritdoc />
    public override void _Ready()
    {
        PanelTheme.Apply(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        _host = Host ?? GetNodeOrNull<SimHost>("../../SimHost");
        LoadStrings();
        Build();
        Refresh();
        MaybeAutoOpenNewGame();
    }

    /// <summary>Refreshes the Continue button and focuses New Game, for showing the menu again after quitting.</summary>
    public void Refresh()
    {
        if (_strings is null || _continueButton is null)
        {
            return;
        }

        IReadOnlyList<SaveSlotInfo> slots = SaveSlots.ListSlots();
        _continueButton.Disabled = slots.Count == 0;
        _continueButton.Text = _strings.Get(slots.Count == 0 ? "ui.menu.continue.none" : "ui.menu.continue");
        _continueButton.TooltipText = _strings.Get(slots.Count == 0 ? "ui.menu.continue.none.tip" : "ui.menu.continue.tip");
        ShowError(null);
        _newGameButton?.GrabFocus();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_hashLabel is not null && _hashTimeLeft > 0.0)
        {
            _hashTimeLeft -= delta;
            if (_hashTimeLeft <= 0.0)
            {
                _hashLabel.Visible = false;
            }
        }
    }

    private void LoadStrings()
    {
        try
        {
            LocaleLoadResult locale = LocaleTable.Load(ContentPaths.Resolve(), SimHost.DefaultLanguage);
            foreach (ContentDiagnostic diagnostic in locale.Diagnostics)
            {
                GD.PushWarning($"[Evenmere] menu locale: {diagnostic}");
            }

            _strings = new UiStrings(locale.Table);
        }
        catch (Exception ex) when (ex is ContentLoadException or DirectoryNotFoundException or IOException)
        {
            GD.PushError($"[Evenmere] menu: cannot load the display text: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private UiStrings Strings() => _strings ?? throw new InvalidOperationException("The main menu has no display text.");

    private void Build()
    {
        // Opaque (not translucent): with no simulation running the HUD's empty bar would otherwise ghost through,
        // and the HUD's own files are owned by another task.
        var dim = new ColorRect { Color = new Color(0.10f, 0.14f, 0.18f, 1f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        // The menu sits on the theme's light panel, the way the slot dialogs do, so every text element reads
        // clearly against it with the theme's own colours.
        var panel = new PanelContainer();
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        column.CustomMinimumSize = new Vector2(340f, 0);
        margin.AddChild(column);

        var title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 64);
        column.AddChild(title);
        _title = title;

        var subtitle = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(subtitle);
        _subtitle = subtitle;

        var gap = new Control { CustomMinimumSize = new Vector2(0, 12f) };
        column.AddChild(gap);

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        seedRow.Alignment = BoxContainer.AlignmentMode.Center;
        column.AddChild(seedRow);

        var seedLabel = new Label();
        seedRow.AddChild(seedLabel);
        _seedLabel = seedLabel;

        var seedEdit = new LineEdit
        {
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(200f, 0),
            MaxLength = 20,
            Text = LaunchOptions.DefaultSeed.ToString(CultureInfo.InvariantCulture),
        };
        seedEdit.TextSubmitted += _ => ShowNewGame();
        NewGameScreen.StyleInput(seedEdit);
        seedRow.AddChild(seedEdit);
        _seedEdit = seedEdit;

        var seedButton = new Button { FocusMode = FocusModeEnum.All };
        seedButton.Pressed += PickRandomSeed;
        seedRow.AddChild(seedButton);
        _seedButton = seedButton;

        Button newGame = MakeButton(column);
        newGame.Pressed += ShowNewGame;
        _newGameButton = newGame;

        Button cont = MakeButton(column);
        cont.Pressed += ContinueGame;
        _continueButton = cont;

        Button load = MakeButton(column);
        load.Pressed += () => _dialog?.ShowLoad(Strings(), SaveSlots.ListSlots());
        _loadButton = load;

        Button settings = MakeButton(column);
        settings.Pressed += () =>
        {
            if (_strings is not null)
            {
                _settings?.ShowSettings(_strings);
            }
        };
        _settingsButton = settings;

        Button quit = MakeButton(column);
        quit.Pressed += () => GetTree().Quit();
        _quitButton = quit;

        var error = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        column.AddChild(error);
        _errorLabel = error;

        var hash = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        column.AddChild(hash);
        _hashLabel = hash;

        ApplyTexts();

        var dialog = new SlotListDialog();
        AddChild(dialog);
        dialog.LoadRequested += OnLoadSlot;
        dialog.Closed += OnDialogClosed;
        _dialog = dialog;

        var settingsStub = new SettingsStub();
        AddChild(settingsStub);
        _settings = settingsStub;

        var report = new LoadReportNote();
        AddChild(report);
        report.Dismissed += OnReportDismissed;
        _report = report;
    }

    private static Button MakeButton(VBoxContainer parent)
    {
        var button = new Button
        {
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44f),
        };
        parent.AddChild(button);
        return button;
    }

    private void ApplyTexts()
    {
        if (_strings is null)
        {
            return;
        }

        UiStrings strings = _strings;
        if (_title is not null)
        {
            _title.Text = strings.Get("ui.menu.title");
        }

        if (_subtitle is not null)
        {
            _subtitle.Text = strings.Get("ui.menu.subtitle");
        }

        if (_seedLabel is not null)
        {
            _seedLabel.Text = strings.Get("ui.menu.seed");
            _seedLabel.TooltipText = strings.Get("ui.menu.seed.tip");
        }

        if (_seedButton is not null)
        {
            _seedButton.Text = strings.Get("ui.menu.seed.new");
            _seedButton.TooltipText = strings.Get("ui.menu.seed.new.tip");
        }

        SetButton(_newGameButton, "ui.menu.new_game", "ui.menu.new_game.tip");
        SetButton(_loadButton, "ui.menu.load", "ui.menu.load.tip");
        SetButton(_settingsButton, "ui.menu.settings", "ui.menu.settings.tip");
        SetButton(_quitButton, "ui.menu.quit", "ui.menu.quit.tip");
        Refresh();
    }

    private void SetButton(Button? button, string textKey, string tipKey)
    {
        if (button is null || _strings is null)
        {
            return;
        }

        button.Text = _strings.Get(textKey);
        button.TooltipText = _strings.Get(tipKey);
    }

    private void PickRandomSeed()
    {
        if (_seedEdit is null)
        {
            return;
        }

        _seedEdit.Text = Random.Shared.NextInt64(0, long.MaxValue).ToString(CultureInfo.InvariantCulture);
        _seedEdit.GrabFocus();
        _seedEdit.SelectAll();
    }

    private ulong InitialSeedOrRandom()
    {
        if (_seedEdit is not null
            && ulong.TryParse(_seedEdit.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            return seed;
        }

        // Random by default (the game layer may use the system RNG; the sim may not): every new island differs.
        return (ulong)Random.Shared.NextInt64(0, long.MaxValue);
    }

    private void ShowNewGame() => ShowNewGame(string.Empty, walk: false);

    private void ShowNewGame(string shotMode, bool walk)
    {
        if (_host is null || _strings is null || _newGame is not null)
        {
            return;
        }

        ShowError(null);
        var screen = new NewGameScreen
        {
            Host = _host,
            Strings = _strings,
            InitialSeed = InitialSeedOrRandom(),
            ShotMode = shotMode,
            WalkRequested = walk,
        };
        AddChild(screen);
        screen.Started += OnNewGameStarted;
        screen.Closed += OnNewGameClosed;
        _newGame = screen;
    }

    private void OnNewGameStarted()
    {
        if (_newGame is not null)
        {
            _newGame.QueueFree();
            _newGame = null;
        }

        Recorder?.EndSlot();
        PlayStarted?.Invoke();
    }

    private void OnNewGameClosed()
    {
        _newGame = null;
        Refresh();
    }

    private void MaybeAutoOpenNewGame()
    {
        // Debug only (M2-25): `--newgame-shot=&lt;mode&gt;` opens the new-game screen at startup for screenshots,
        // and `--newgame-walk` opens it with the scripted ui_* reachability probe, because --focus-walk starts a
        // game directly and never shows the menu. Release builds ignore both flags.
        if (!OS.IsDebugBuild())
        {
            return;
        }

        string[] args = OS.GetCmdlineUserArgs();
        string shotMode = string.Empty;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--newgame-shot=", StringComparison.Ordinal))
            {
                string tail = arg["--newgame-shot=".Length..];
                if (tail is "starter" or "generated" or "generating")
                {
                    shotMode = tail;
                }
            }
        }

        bool walk = args.Contains("--newgame-walk", StringComparer.Ordinal);
        if (shotMode.Length > 0 || walk)
        {
            ShowNewGame(shotMode, walk);
        }
    }

    private void ContinueGame()
    {
        IReadOnlyList<SaveSlotInfo> slots = SaveSlots.ListSlots();
        if (slots.Count == 0)
        {
            return;
        }

        OnLoadSlot(slots[0].SlotId);
    }

    private void OnLoadSlot(string slotId)
    {
        if (_host is null || Recorder is null)
        {
            return;
        }

        try
        {
            ShowError(null);
            (string hash, SaveReport report) = SaveService.Load(_host, Recorder, slotId);
            _dialog?.Dismiss();
            FlashHash(Strings().Format("ui.hash.loaded", ("hash", hash)));
            if (report.IsClean)
            {
                PlayStarted?.Invoke();
            }
            else
            {
                _waitingForReportDismiss = true;
                _report?.ShowReport(Strings(), report);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowError(Strings().Format("ui.load.failed", ("reason", ex.Message.ReplaceLineEndings(" "))));
        }
    }

    private void OnDialogClosed()
    {
        // Returning from the slot list goes back to the menu's focus chain.
        _loadButton?.GrabFocus();
    }

    private void OnReportDismissed()
    {
        if (_waitingForReportDismiss)
        {
            _waitingForReportDismiss = false;
            PlayStarted?.Invoke();
        }
    }

    private void ShowError(string? message)
    {
        if (_errorLabel is null)
        {
            return;
        }

        _errorLabel.Visible = message is not null;
        _errorLabel.Text = message ?? string.Empty;
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
}
