using Evenmere.Game.Ui.Common;
using Godot;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// A rounded chip with a name and a number, and optionally a trend arrow: one good in the strip, or one stat in the top bar.
/// It is built by <see cref="Create"/> and filled by <see cref="SetValue"/> and <see cref="SetTrend"/>. The number sits in a
/// right-aligned label as wide as a wide number would be, so chips do not jump around as the digits change.
/// <see cref="SetWarning"/> tints the number in a calm ochre (the same hue as the build menu's locked line) while a tier
/// is short of workers: noticeable beside the ink numbers, but never alarming red.
/// </summary>
public sealed partial class HudChip : PanelContainer
{
    /// <summary>The calm warning tint for a short workforce, shared with the build menu's workforce line.</summary>
    public static readonly Color WarningTint = new(0.66f, 0.46f, 0.15f);

    /// <summary>The ART-02 icon edge, in logical points: the 256 master scales to fit.</summary>
    private const float IconEdge = 24f;

    private TextureRect? _icon;
    private Label? _name;
    private Label? _value;
    private TrendArrow? _arrow;
    private string? _widestValue;

    /// <summary>Creates a chip.</summary>
    /// <param name="withTrend">Whether to add a trend arrow after the number.</param>
    /// <param name="widestValue">Sample text as wide as the number will get, which sets the minimum width of the number's label; <see langword="null"/> for none.</param>
    /// <param name="withName">Whether to show the name beside the number; goods chips hide it (icon, amount and trend only) and carry the name in the tooltip instead, so thirteen goods fit one row.</param>
    /// <returns>The chip, not yet in the tree.</returns>
    public static HudChip Create(bool withTrend, string? widestValue, bool withName = true)
    {
        var chip = new HudChip
        {
            ThemeTypeVariation = "HudChip",
            _widestValue = widestValue,
        };

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 3);
        chip.AddChild(row);

        // The ART-02 icon, hidden until SetIcon provides one: text-only chips keep their old layout.
        chip._icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(IconEdge, IconEdge),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        row.AddChild(chip._icon);

        chip._name = withName
            ? new Label { ThemeTypeVariation = "ChipNameLabel", MouseFilter = MouseFilterEnum.Ignore }
            : null;
        if (chip._name is not null)
        {
            row.AddChild(chip._name);
        }

        chip._value = new Label
        {
            ThemeTypeVariation = "ChipValueLabel",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddChild(chip._value);

        if (withTrend)
        {
            chip._arrow = new TrendArrow();
            row.AddChild(chip._arrow);
        }

        return chip;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        // The label's font only resolves through the theme once the chip is in the tree, so the width is measured here.
        if (_widestValue is not null && _value is not null)
        {
            Font font = _value.GetThemeFont("font");
            int fontSize = _value.GetThemeFontSize("font_size");
            _value.CustomMinimumSize = new Vector2(font.GetStringSize(_widestValue, HorizontalAlignment.Left, -1, fontSize).X, 0f);
        }
    }

    /// <summary>Sets the name, the number and the tooltip. Nameless (compact) chips keep the name for the tooltip only.</summary>
    /// <param name="name">The name beside the number.</param>
    /// <param name="value">The formatted number.</param>
    /// <param name="tooltip">The tooltip text.</param>
    public void SetValue(string name, string value, string tooltip)
    {
        if (_name is not null)
        {
            _name.Text = name;
        }

        if (_value is not null)
        {
            _value.Text = value;
            TooltipText = tooltip;
        }
    }

    /// <summary>Sets the ART-02 icon beside the name, or hides it for a <see langword="null"/> texture.</summary>
    /// <param name="texture">The icon texture, or <see langword="null"/> to keep the text-only chip.</param>
    public void SetIcon(Texture2D? texture)
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Texture = texture;
        _icon.Visible = texture is not null;
    }

    /// <summary>Sets the trend arrow, if the chip has one.</summary>
    /// <param name="trend">The direction.</param>
    public void SetTrend(Trend trend)
    {
        if (_arrow is not null)
        {
            _arrow.Trend = trend;
        }
    }

    /// <summary>Sets whether the number shows the warning tint: a tier short of workers.</summary>
    /// <param name="warning"><see langword="true"/> for the calm ochre tint; <see langword="false"/> for the theme's ink.</param>
    public void SetWarning(bool warning)
    {
        if (_value is null)
        {
            return;
        }

        if (warning)
        {
            _value.AddThemeColorOverride("font_color", WarningTint);
        }
        else
        {
            _value.RemoveThemeColorOverride("font_color");
        }
    }
}
