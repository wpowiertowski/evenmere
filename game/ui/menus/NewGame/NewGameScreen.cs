using System.Globalization;
using Evenmere.Content;
using Evenmere.Content.Locale;
using Evenmere.Content.Maps;
using Evenmere.Game.Host;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Progress;
using Evenmere.Sim.Systems.Map.Generation;
using Godot;

namespace Evenmere.Game.Ui.Menus.NewGame;

/// <summary>
/// The new-game setup screen (M2-25): a starter-island or generated-island choice, a seed field (random by
/// default, with a new-seed button), a size choice (256, 320 or 384), a preview thumbnail rendered from the
/// <see cref="MapDef"/>'s terrain colours only, and Start and Back buttons. The island is drawn off the main
/// thread (the game layer may use a <see cref="Task"/>; the sim may not), the screen shows a calm
/// "Drawing the island…" state meanwhile, and results from a superseded seed are ignored. Every control is
/// reachable by the built-in <c>ui_*</c> focus navigation with no new input actions. The screen builds itself
/// in code like the slot dialogs, so no scene change is needed to show it.
/// </summary>
public sealed partial class NewGameScreen : Control
{
    /// <summary>Raised after the new island starts, so the menu can hide itself and record the log.</summary>
    public event Action? Started;

    /// <summary>Raised when Back is chosen, so the menu can show itself again.</summary>
    public event Action? Closed;

    /// <summary>Gets or sets the simulation host new games start on. It is set before the screen enters the tree.</summary>
    public SimHost? Host { get; set; }

    /// <summary>Gets or sets the display text. It is set before the screen enters the tree in menu mode.</summary>
    public UiStrings? Strings { get; set; }

    /// <summary>Gets or sets the seed the field starts with. The menu passes its own seed row; standalone runs use a fixed one.</summary>
    public ulong InitialSeed { get; set; }

    /// <summary>Gets or sets the debug-only shot preset (<c>starter</c>, <c>generated</c> or <c>generating</c>), for screenshots.</summary>
    public string ShotMode { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the debug-only scripted focus walk runs once the first preview lands.</summary>
    public bool WalkRequested { get; set; }

    /// <summary>Gets or sets whether the debug-only auto-start chooses Start once the preview lands, runs 1200 ticks and quits, proving the start path without a running game.</summary>
    public bool AutoStartRequested { get; set; }

    /// <summary>Gets whether a preview island is loaded and Start may be chosen.</summary>
    public bool IsReady => _ready;

    /// <summary>Gets the current island choice for the walk report: <c>starter</c> or <c>generated</c>.</summary>
    public string ModeName => _generated ? "generated" : "starter";

    private static readonly int[] _sizes = [256, 320, 384];

    private SimHost? _host;
    private UiStrings? _strings;
    private ContentDb? _content;
    private string _contentRoot = string.Empty;
    private bool _built;
    private bool _begun;
    private bool _generated = true;
    private bool _ready;
    private int _generation;
    private MapDef? _pendingMap;
    private bool _pendingGenerated = true;
    private string _starterSizeText = string.Empty;
    private ulong _seed;

    private Button? _starterButton;
    private Button? _generatedButton;
    private Label? _titleLabel;
    private Label? _islandLabel;
    private Label? _seedLabel;
    private Label? _sizeLabel;
    private Label? _captionLabel;
    private Button? _startButton;
    private Button? _backButton;
    private LineEdit? _seedEdit;
    private Button? _newSeedButton;
    private OptionButton? _sizeOption;
    private Label? _starterSizeLabel;
    private TextureRect? _previewRect;
    private Label? _statusLabel;

    /// <inheritdoc />
    public override void _Ready()
    {
        // Standalone mode (screenshots and the scripted walk run the scene directly, outside Main.tscn): build a
        // host for content and generation, since the menu is not there to inject one. Debug builds only.
        if (Host is null && OS.IsDebugBuild())
        {
            string[] args = OS.GetCmdlineUserArgs();
            string? shot = ShotValue(args);
            bool walk = args.Contains("--newgame-walk", StringComparer.Ordinal);
            if (shot is not null || walk)
            {
                var host = new SimHost();
                AddChild(host);
                Host = host;
                ShotMode = shot ?? string.Empty;
                WalkRequested = walk;
                AutoStartRequested = args.Contains("--newgame-start", StringComparer.Ordinal);
            }
        }

        Build();
        Begin();
    }

    /// <summary>Chooses the starter island, through the same path as its button, so the walk can drive both modes.</summary>
    public void SelectStarter()
    {
        if (_starterButton is not null && !_starterButton.ButtonPressed)
        {
            // Setting the button fires Toggled, which refreshes; the explicit refresh below covers the
            // already-pressed case, and the superseded generation is dropped when it lands.
            _starterButton.ButtonPressed = true;
        }
        else if (_starterButton is null)
        {
            _generated = false;
        }

        RefreshPreview();
    }

    /// <summary>Chooses a generated island with the given seed and size, through the same path as its button.</summary>
    /// <param name="seed">The world seed.</param>
    /// <param name="size">The island size in tiles: 256, 320 or 384.</param>
    public void SelectGenerated(ulong seed, int size)
    {
        if (_seedEdit is not null)
        {
            _seedEdit.Text = seed.ToString(CultureInfo.InvariantCulture);
        }

        if (_sizeOption is not null)
        {
            for (int i = 0; i < _sizeOption.ItemCount; i++)
            {
                if (_sizeOption.GetItemId(i) == size)
                {
                    _sizeOption.Selected = i;
                    break;
                }
            }
        }

        if (_generatedButton is not null && !_generatedButton.ButtonPressed)
        {
            // Setting the button fires Toggled, which refreshes; the explicit refresh below covers the
            // already-pressed case, and the superseded generation is dropped when it lands.
            _generatedButton.ButtonPressed = true;
        }
        else if (_generatedButton is null)
        {
            _generated = true;
        }

        RefreshPreview();
    }

    private void Build()
    {
        if (_built)
        {
            return;
        }

        _built = true;
        Name = "NewGameScreen";
        PanelTheme.Apply(this);
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Opaque (not translucent): the menu's own bar would otherwise ghost through, and standalone runs have
        // nothing behind the screen at all.
        var dim = new ColorRect { Color = new Color(0.10f, 0.14f, 0.18f, 1f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        margin.AddChild(row);

        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 10);
        form.CustomMinimumSize = new Vector2(360f, 0);
        row.AddChild(form);

        var title = new Label { HorizontalAlignment = HorizontalAlignment.Left };
        title.AddThemeFontSizeOverride("font_size", 40);
        form.AddChild(title);
        _titleLabel = title;

        var islandLabel = new Label();
        form.AddChild(islandLabel);
        _islandLabel = islandLabel;

        var islandRow = new HBoxContainer();
        islandRow.AddThemeConstantOverride("separation", 8);
        form.AddChild(islandRow);

        var group = new ButtonGroup { AllowUnpress = false };
        var starter = new Button
        {
            Name = "StarterButton",
            FocusMode = FocusModeEnum.All,
            ToggleMode = true,
            ButtonGroup = group,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44f),
        };
        starter.Toggled += pressed => { if (pressed) OnModeChosen(false); };
        islandRow.AddChild(starter);
        _starterButton = starter;

        var generated = new Button
        {
            Name = "GeneratedButton",
            FocusMode = FocusModeEnum.All,
            ToggleMode = true,
            ButtonGroup = group,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44f),
        };
        generated.Toggled += pressed => { if (pressed) OnModeChosen(true); };
        islandRow.AddChild(generated);
        _generatedButton = generated;

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        form.AddChild(seedRow);

        var seedLabel = new Label();
        seedRow.AddChild(seedLabel);
        _seedLabel = seedLabel;

        var seedEdit = new LineEdit
        {
            Name = "SeedEdit",
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(160f, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = 20,
        };
        StyleInput(seedEdit);
        seedEdit.TextSubmitted += _ => RefreshPreview();
        seedRow.AddChild(seedEdit);
        _seedEdit = seedEdit;

        var newSeed = new Button { Name = "NewSeedButton", FocusMode = FocusModeEnum.All };
        newSeed.Pressed += PickRandomSeed;
        seedRow.AddChild(newSeed);
        _newSeedButton = newSeed;

        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 8);
        form.AddChild(sizeRow);

        var sizeLabel = new Label();
        sizeRow.AddChild(sizeLabel);
        _sizeLabel = sizeLabel;

        var sizeOption = new OptionButton
        {
            Name = "SizeOption",
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        foreach (int size in _sizes)
        {
            sizeOption.AddItem(SizeText(size), size);
            if (size == IslandGenerator.DefaultSize)
            {
                sizeOption.Selected = sizeOption.ItemCount - 1;
            }
        }

        sizeOption.ItemSelected += _ => RefreshPreview();
        sizeRow.AddChild(sizeOption);
        _sizeOption = sizeOption;

        // In starter mode the dropdown is replaced by the starter island's own size, read from its MapDef.
        var starterSize = new Label { VerticalAlignment = VerticalAlignment.Center };
        sizeRow.AddChild(starterSize);
        _starterSizeLabel = starterSize;

        var status = new Label
        {
            Name = "Status",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 48f),
        };
        form.AddChild(status);
        _statusLabel = status;

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 8);
        form.AddChild(buttonRow);

        var start = new Button
        {
            Name = "StartButton",
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44f),
        };
        start.Pressed += StartGame;
        buttonRow.AddChild(start);
        _startButton = start;

        var back = new Button
        {
            Name = "BackButton",
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44f),
        };
        back.Pressed += GoBack;
        buttonRow.AddChild(back);
        _backButton = back;

        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 8);
        row.AddChild(side);

        var caption = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        side.AddChild(caption);
        _captionLabel = caption;

        var preview = new TextureRect
        {
            Name = "Preview",
            CustomMinimumSize = new Vector2(384f, 384f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        side.AddChild(preview);
        _previewRect = preview;

        ApplyTexts();
    }

    private void Begin()
    {
        if (_begun)
        {
            return;
        }

        _begun = true;
        _host = Host;
        _strings = Strings ?? LoadStandaloneStrings();

        if (_host is null || _strings is null)
        {
            ShowStatus("ui.newgame.failed");
            return;
        }

        try
        {
            _content = _host.LoadContent();
            _contentRoot = ContentPaths.Resolve();
        }
        catch (Exception ex) when (ex is ContentLoadException or DirectoryNotFoundException or IOException)
        {
            ShowStatus("ui.newgame.failed", reason: ex.Message.ReplaceLineEndings(" "));
            return;
        }

        // The shot presets pin the mode, seed and size so screenshots frame the same island every run.
        if (OS.IsDebugBuild() && ShotMode == "starter")
        {
            InitialSeed = 7;
            _generated = false;
        }
        else if (OS.IsDebugBuild() && (ShotMode == "generated" || ShotMode == "generating"))
        {
            InitialSeed = 7;
            _generated = true;
        }

        if (_seedEdit is not null)
        {
            _seedEdit.Text = InitialSeed.ToString(CultureInfo.InvariantCulture);
        }

        if (_starterButton is not null && _generatedButton is not null)
        {
            _starterButton.SetPressedNoSignal(!_generated);
            _generatedButton.SetPressedNoSignal(_generated);
        }

        if (ShotMode == "generating" && _sizeOption is not null)
        {
            // The largest island draws slowest, so the generating state is still up when the shot lands.
            for (int i = 0; i < _sizeOption.ItemCount; i++)
            {
                if (_sizeOption.GetItemId(i) == IslandGenerator.MaxSize)
                {
                    _sizeOption.Selected = i;
                    break;
                }
            }
        }

        ApplyTexts();
        UpdateFocusNeighbors();
        UpdateSizeRow();
        RefreshPreview();
        if (_generated)
        {
            _generatedButton?.GrabFocus();
        }
        else
        {
            _starterButton?.GrabFocus();
        }

        if (OS.IsDebugBuild() && WalkRequested)
        {
            AddChild(new NewGameWalk(this));
        }
    }

    private UiStrings? LoadStandaloneStrings()
    {
        if (Strings is not null)
        {
            return Strings;
        }

        try
        {
            LocaleLoadResult locale = LocaleTable.Load(ContentPaths.Resolve(), SimHost.DefaultLanguage);
            foreach (ContentDiagnostic diagnostic in locale.Diagnostics)
            {
                GD.PushWarning($"[Evenmere] new game: menu locale: {diagnostic}");
            }

            return new UiStrings(locale.Table);
        }
        catch (Exception ex) when (ex is ContentLoadException or DirectoryNotFoundException or IOException)
        {
            GD.PushError($"[Evenmere] new game: cannot load the display text: {ex.Message.ReplaceLineEndings(" ")}");
            return null;
        }
    }

    private void ApplyTexts()
    {
        if (_strings is null)
        {
            return;
        }

        UiStrings strings = _strings;
        if (_titleLabel is not null)
        {
            _titleLabel.Text = strings.Get("ui.newgame.title");
        }

        if (_islandLabel is not null)
        {
            _islandLabel.Text = strings.Get("ui.newgame.island");
        }

        SetButton(_starterButton, strings.Get("ui.newgame.island.starter"), strings.Get("ui.newgame.island.starter.tip"));
        SetButton(_generatedButton, strings.Get("ui.newgame.island.generated"), strings.Get("ui.newgame.island.generated.tip"));
        if (_seedLabel is not null)
        {
            _seedLabel.Text = strings.Get("ui.newgame.seed");
        }

        if (_seedEdit is not null)
        {
            _seedEdit.TooltipText = strings.Get("ui.newgame.seed.tip");
        }

        SetButton(_newSeedButton, strings.Get("ui.newgame.seed.new"), strings.Get("ui.newgame.seed.new.tip"));
        if (_sizeLabel is not null)
        {
            _sizeLabel.Text = strings.Get("ui.newgame.size");
        }

        if (_sizeOption is not null)
        {
            _sizeOption.TooltipText = strings.Get("ui.newgame.size.tip");
            for (int i = 0; i < _sizeOption.ItemCount; i++)
            {
                _sizeOption.SetItemText(i, SizeText(_sizeOption.GetItemId(i)));
            }
        }

        if (_captionLabel is not null)
        {
            _captionLabel.Text = strings.Get("ui.newgame.preview");
        }

        SetButton(_startButton, strings.Get("ui.newgame.start"), strings.Get("ui.newgame.start.tip"));
        SetButton(_backButton, strings.Get("ui.newgame.back"), strings.Get("ui.newgame.back.tip"));
    }

    private static string SizeText(int size) => SizeText(size, size);

    private static string SizeText(int width, int height) => $"{width} × {height}";

    private static void SetButton(Button? button, string text, string tip)
    {
        if (button is null)
        {
            return;
        }

        button.Text = text;
        button.TooltipText = tip;
    }

    private void OnModeChosen(bool generated)
    {
        _generated = generated;
        UpdateFocusNeighbors();
        UpdateSizeRow();
        RefreshPreview();
    }

    // The starter island has no size choice: the row shows the loaded island's own size as plain text, read from
    // its MapDef. Before the first starter preview lands the text is an ellipsis, beside the drawing status.
    private void UpdateSizeRow()
    {
        if (_sizeOption is not null)
        {
            _sizeOption.Visible = _generated;
        }

        if (_starterSizeLabel is not null)
        {
            _starterSizeLabel.Visible = !_generated;
            _starterSizeLabel.Text = _starterSizeText.Length > 0 ? _starterSizeText : "\u2026";
        }
    }

    /// <summary>
    /// Styles a text input from the shared UI theme's own tokens (M2-25 round 2): the theme styles buttons and
    /// labels but has no <c>LineEdit</c> section, so an unstyled field falls back to the engine's dark box. The
    /// box mirrors the button style (cream fill, tan border, radius 10) with the amber focus ring, read-only use
    /// of <c>game/ui/theme/evenmere_theme.tres</c> that never edits it.
    /// </summary>
    /// <param name="field">The input to style.</param>
    internal static void StyleInput(LineEdit field)
    {
        ArgumentNullException.ThrowIfNull(field);

        // The exact tokens of evenmere_theme.tres: ButtonNormal's fill, border and radius; the Focus ring's amber.
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.929412f, 0.894118f, 0.815686f),
            BorderColor = new Color(0.839216f, 0.788235f, 0.682353f),
        };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(10);
        normal.ContentMarginLeft = 14f;
        normal.ContentMarginRight = 14f;
        normal.ContentMarginTop = 6f;
        normal.ContentMarginBottom = 6f;

        var focused = (StyleBoxFlat)normal.Duplicate();
        focused.BorderColor = new Color(0.909804f, 0.537255f, 0.047059f);
        focused.SetBorderWidthAll(2);

        var ink = new Color(0.113725f, 0.168627f, 0.211765f);
        field.AddThemeStyleboxOverride("normal", normal);
        field.AddThemeStyleboxOverride("focus", focused);
        field.AddThemeColorOverride("font_color", ink);
        field.AddThemeColorOverride("font_selected_color", ink);
        field.AddThemeColorOverride("caret_color", ink);
        field.AddThemeColorOverride("selection_color", new Color(0.862745f, 0.917647f, 0.941176f));
        field.AddThemeColorOverride(
            "font_placeholder_color", new Color(0.368627f, 0.423529f, 0.458824f, 0.7f));
    }

    // The disabled size control in starter mode breaks automatic ui_down navigation to the buttons below it
    // (the finder stops at the disabled control), so the form wires its own neighbours and rewires the rows
    // around the size control whenever the mode changes. Unset sides keep automatic navigation. The seed field
    // keeps caret movement: ui_left and ui_right never leave a LineEdit, so every row stays reachable vertically.
    private void UpdateFocusNeighbors()
    {
        if (_starterButton is null
            || _generatedButton is null
            || _seedEdit is null
            || _newSeedButton is null
            || _sizeOption is null
            || _startButton is null
            || _backButton is null
            || !IsInsideTree())
        {
            return;
        }

        _starterButton.FocusNeighborRight = _starterButton.GetPathTo(_generatedButton);
        _starterButton.FocusNeighborBottom = _starterButton.GetPathTo(_seedEdit);
        _generatedButton.FocusNeighborLeft = _generatedButton.GetPathTo(_starterButton);
        _generatedButton.FocusNeighborBottom = _generatedButton.GetPathTo(_newSeedButton);
        _seedEdit.FocusNeighborTop = _seedEdit.GetPathTo(_starterButton);
        _seedEdit.FocusNeighborBottom = _seedEdit.GetPathTo(_generated ? _sizeOption : _startButton);
        _newSeedButton.FocusNeighborLeft = _newSeedButton.GetPathTo(_seedEdit);
        _newSeedButton.FocusNeighborTop = _newSeedButton.GetPathTo(_generatedButton);
        _newSeedButton.FocusNeighborBottom = _newSeedButton.GetPathTo(_generated ? _sizeOption : _backButton);
        _sizeOption.FocusNeighborTop = _sizeOption.GetPathTo(_seedEdit);
        _sizeOption.FocusNeighborBottom = _sizeOption.GetPathTo(_startButton);
        _startButton.FocusNeighborTop = _startButton.GetPathTo(_generated ? _sizeOption : _seedEdit);
        _startButton.FocusNeighborRight = _startButton.GetPathTo(_backButton);
        _backButton.FocusNeighborLeft = _backButton.GetPathTo(_startButton);
        _backButton.FocusNeighborTop = _backButton.GetPathTo(_generated ? _sizeOption : _newSeedButton);
    }

    private void PickRandomSeed()
    {
        if (_seedEdit is null)
        {
            return;
        }

        _seedEdit.Text = Random.Shared.NextInt64(0, long.MaxValue).ToString(CultureInfo.InvariantCulture);
        RefreshPreview();
        _seedEdit.GrabFocus();
        _seedEdit.SelectAll();
    }

    private void RefreshPreview()
    {
        if (!_built || _host is null || _strings is null || _content is null)
        {
            return;
        }

        if (_seedEdit is null
            || !ulong.TryParse(_seedEdit.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            _ready = false;
            _pendingMap = null;
            _generation++;
            SetStartEnabled(false);
            ShowStatusRaw(_strings.Get("ui.newgame.seed.invalid"));
            return;
        }

        int size = SelectedSize();

        _seed = seed;
        _ready = false;
        _pendingMap = null;
        SetStartEnabled(false);
        ShowStatusRaw(_strings.Get("ui.newgame.drawing"));

        // The island draws off the main thread; a superseded seed's result is dropped when it lands.
        int generation = ++_generation;
        bool generated = _generated;
        ContentDb content = _content;
        string root = _contentRoot;
        RefreshPreviewAsync(generation, generated, seed, size, content, root);
    }

    private int SelectedSize()
    {
        if (_sizeOption is not null)
        {
            int id = _sizeOption.GetSelectedId();
            if (id is 256 or 320 or 384)
            {
                return id;
            }
        }

        return IslandGenerator.DefaultSize;
    }

    private async void RefreshPreviewAsync(int generation, bool generated, ulong seed, int size, ContentDb content, string root)
    {
        MapDef? map = null;
        string? error = null;
        try
        {
            map = await Task.Run(() => LoadIsland(generated, seed, size, content, root));
        }
        catch (Exception ex) when (ex is ContentLoadException or MapLoadException or IOException or InvalidOperationException)
        {
            error = ex.Message.ReplaceLineEndings(" ");
        }

        if (generation != _generation)
        {
            // A newer seed or size has already taken over; this island belongs to yesterday's form.
            return;
        }

        if (map is null)
        {
            if (error is not null && _strings is not null)
            {
                ShowStatus("ui.newgame.failed", reason: error);
            }
            else
            {
                ShowStatusRaw(_strings?.Get("ui.newgame.failed") ?? "ui.newgame.failed");
            }

            return;
        }

        byte[] rgba = NewGamePreview.Render(map, content);
        var image = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rgba8, rgba);
        if (image is null || _strings is null)
        {
            ShowStatus("ui.newgame.failed");
            return;
        }

        if (_previewRect is not null)
        {
            _previewRect.Texture = ImageTexture.CreateFromImage(image);
            _previewRect.TooltipText = _strings.Get("ui.newgame.preview.tip");
        }

        _pendingMap = map;
        _pendingGenerated = generated;
        if (!generated)
        {
            _starterSizeText = SizeText(map.Width, map.Height);
            UpdateSizeRow();
        }

        _ready = true;
        SetStartEnabled(true);
        ShowStatus(generated ? "ui.newgame.ready" : "ui.newgame.ready.starter", seed, size);

        // Debug only: prove the start path (the ready-MapDef SimHost overload) without a running game.
        if (OS.IsDebugBuild() && AutoStartRequested)
        {
            AutoStartRequested = false;
            AutoStartAndQuit();
        }
    }

    private static MapDef LoadIsland(bool generated, ulong seed, int size, ContentDb content, string root)
    {
        if (!generated)
        {
            return MapLoader.Load([root], LaunchOptions.DefaultMap, content).GetMapOrThrow();
        }

        return IslandGenerator.Generate(NewGamePreview.GeneratorSeed(seed), size, content);
    }

    private void StartGame()
    {
        if (_host is null || _strings is null || !_ready || _pendingMap is null)
        {
            return;
        }

        try
        {
            _host.StartNewGame(_seed, _pendingMap);
            Started?.Invoke();
        }
        catch (Exception ex) when (ex is ContentLoadException or MapLoadException or IOException)
        {
            _ready = false;
            SetStartEnabled(false);
            ShowStatus("ui.newgame.failed", reason: ex.Message.ReplaceLineEndings(" "));
        }
    }

    private void GoBack()
    {
        Closed?.Invoke();
        QueueFree();
    }

    // Debug only (--newgame-start with a --newgame-shot preset): chooses Start once the preview lands, runs
    // 1200 ticks through the new ready-MapDef overload and quits, printing the hash. A superseded preview never
    // reaches here: AutoStartRequested is consumed by the generation it was set for.
    private void AutoStartAndQuit()
    {
        try
        {
            StartGame();
            if (_host is null || !_host.IsRunning)
            {
                throw new InvalidOperationException("the new game did not start.");
            }

            _host.RunTicksNow(1200);
            GD.Print($"[Evenmere] newgame-start: START OK mode={ModeName} seed={_seed} ticks={_host.CurrentTick} hash={_host.ComputeStateHash():x16}");
            GetTree().Quit(0);
        }
        catch (Exception ex) when (ex is ContentLoadException or MapLoadException or IOException or InvalidOperationException)
        {
            GD.Print($"[Evenmere] newgame-start FAIL: {ex.Message.ReplaceLineEndings(" ")}");
            GetTree().Quit(1);
        }
    }

    private void SetStartEnabled(bool enabled)
    {
        if (_startButton is not null)
        {
            _startButton.Disabled = !enabled;
        }
    }

    private void ShowStatus(string key, ulong seed = 0, int size = 0, string reason = "")
    {
        if (_statusLabel is null || _strings is null)
        {
            return;
        }

        _statusLabel.Text = _strings.Format(
            key,
            ("seed", seed.ToString(CultureInfo.InvariantCulture)),
            ("size", size.ToString(CultureInfo.InvariantCulture)),
            ("reason", reason));
    }

    private void ShowStatusRaw(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = text;
        }
    }

    private static string? ShotValue(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--newgame-shot=", StringComparison.Ordinal))
            {
                string value = arg["--newgame-shot=".Length..];
                if (value is "starter" or "generated" or "generating")
                {
                    return value;
                }
            }
        }

        return null;
    }
}
