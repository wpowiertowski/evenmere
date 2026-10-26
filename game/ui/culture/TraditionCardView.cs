using Godot;

namespace Evenmere.Game.Ui.Culture;

/// <summary>
/// One tradition card: a focusable button with the card name, its slot kind, the effect lines and, for style
/// cards, the style badge. Pressing the card picks it up for the board's gamepad picker (press a slot to place
/// it); dragging it onto a slot button places it directly. Both paths raise <see cref="CardChosen"/>, and slots
/// report the drop, so mouse and gamepad reach the same command with no new input actions.
/// Until the card art pass wires IconLibrary (ART-02) in, cards show text only; its icon slot is the <see cref="Glyph"/>
/// property, which the board fills from the visuals maps when <c>IconLibrary</c> plugs in here.
/// </summary>
public sealed partial class TraditionCardView : PanelContainer
{
    /// <summary>Raised when the card is pressed (picked up) or its drag starts.</summary>
    public event Action<TraditionCardView>? CardChosen;

    /// <summary>Gets the card's tradition.</summary>
    public TraditionCardData? Card { get; private set; }

    /// <summary>Gets or sets the placeholder glyph shown before the name, empty for none.</summary>
    public string Glyph { get; set; } = string.Empty;

    private static readonly Color _ink = new(0.15f, 0.17f, 0.20f);
    private static readonly Color _muted = new(0.37f, 0.42f, 0.46f);
    private static readonly Color _picked = new(0.12f, 0.35f, 0.47f);
    private static readonly Color _dim = new(0.55f, 0.57f, 0.60f);

    private VBoxContainer? _box;
    private Button? _main;
    private Label? _slot;
    private Label? _effects;
    private Label? _status;
    private Label? _badge;
    private bool _isPicked;

    /// <summary>Shows the card's data, building the chrome on first use.</summary>
    /// <param name="card">The card to show.</param>
    public void SetCard(TraditionCardData card)
    {
        ArgumentNullException.ThrowIfNull(card);
        EnsureChrome();

        Card = card;
        string glyph = Glyph.Length > 0 ? Glyph + "  " : string.Empty;
        _main!.Text = glyph + card.Name;
        _main.TooltipText = card.EffectText;
        _slot!.Text = card.SlotText;
        _slot.AddThemeColorOverride("font_color", _muted);
        _effects!.Text = card.EffectText;
        _effects.AddThemeColorOverride("font_color", _ink);
        // A slotted card stays in the drawer as a record, dimmed and out of the focus chain: it is not
        // offered again until its slot is cleared.
        _main.FocusMode = card.SlottedElsewhere ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;
        _status!.Visible = card.Status is not null;
        _status.Text = card.Status ?? string.Empty;
        _status.AddThemeColorOverride("font_color", _muted);
        _badge!.Visible = card.StyleBadge is not null;
        _badge.Text = card.StyleBadge ?? string.Empty;
        SetPicked(false);
        Modulate = card.SlottedElsewhere ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>Marks the card as the picker's pending pick, with a coloured frame, or clears the mark.</summary>
    /// <param name="picked"><see langword="true"/> to mark it as picked up.</param>
    public void SetPicked(bool picked)
    {
        _isPicked = picked;
        EnsureChrome();
        AddThemeStyleboxOverride("panel", picked ? PickedCard() : PlainCard());
    }

    /// <inheritdoc />
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Card is null)
        {
            return default;
        }

        CardChosen?.Invoke(this);

        var preview = new Label { Text = Card.Name };
        SetDragPreview(preview);

        // The drop target resolves the content ID back to the card; the string survives the drag altar.
        return Card.ContentId;
    }

    private void EnsureChrome()
    {
        if (_box is not null)
        {
            return;
        }

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        AddChild(box);
        _box = box;

        var main = new Button
        {
            FocusMode = Control.FocusModeEnum.All,
            Alignment = HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
        };
        main.Pressed += () => CardChosen?.Invoke(this);
        box.AddChild(main);
        _main = main;

        var slot = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(slot);
        _slot = slot;

        var effects = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        effects.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(effects);
        _effects = effects;

        var status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        status.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(status);
        _status = status;

        var badge = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeFontSizeOverride("font_size", 14);
        badge.AddThemeColorOverride("font_color", _dim);
        box.AddChild(badge);
        _badge = badge;
    }

    private static StyleBoxFlat PlainCard()
    {
        var card = new StyleBoxFlat
        {
            BgColor = new Color(0.96f, 0.96f, 0.94f),
            BorderColor = new Color(0.60f, 0.62f, 0.64f),
        };
        card.SetBorderWidthAll(2);
        card.SetCornerRadiusAll(8);
        card.ContentMarginLeft = 8;
        card.ContentMarginRight = 8;
        card.ContentMarginTop = 8;
        card.ContentMarginBottom = 8;
        return card;
    }

    private static StyleBoxFlat PickedCard()
    {
        var card = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.92f, 0.96f),
            BorderColor = _picked,
        };
        card.SetBorderWidthAll(3);
        card.SetCornerRadiusAll(8);
        card.ContentMarginLeft = 8;
        card.ContentMarginRight = 8;
        card.ContentMarginTop = 8;
        card.ContentMarginBottom = 8;
        return card;
    }
}
