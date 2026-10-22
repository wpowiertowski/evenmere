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

    private Label? _name;
    private Label? _value;
    private TrendArrow? _arrow;
    private string? _widestValue;

    /// <summary>Creates a chip.</summary>
    /// <param name="withTrend">Whether to add a trend arrow after the number.</param>
    /// <param name="widestValue">Sample text as wide as the number will get, which sets the minimum width of the number's label; <see langword="null"/> for none.</param>
    /// <returns>The chip, not yet in the tree.</returns>
    public static HudChip Create(bool withTrend, string? widestValue)
    {
        var chip = new HudChip
        {
            ThemeTypeVariation = "HudChip",
            _widestValue = widestValue,
        };

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        chip.AddChild(row);

        chip._name = new Label { ThemeTypeVariation = "ChipNameLabel", MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(chip._name);

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

    /// <summary>Sets the name, the number and the tooltip.</summary>
    /// <param name="name">The name beside the number.</param>
    /// <param name="value">The formatted number.</param>
    /// <param name="tooltip">The tooltip text.</param>
    public void SetValue(string name, string value, string tooltip)
    {
        if (_name is not null && _value is not null)
        {
            _name.Text = name;
            _value.Text = value;
            TooltipText = tooltip;
        }
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
