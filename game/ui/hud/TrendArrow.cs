using Evenmere.Game.Ui.Common;
using Godot;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// A small arrow drawn with polygons: a triangle up or down, or a short bar for "no change". It is drawn rather than typed
/// because the HUD font has no arrow glyphs, and it means the same thing in shape as in colour (the colours come from
/// the theme type <c>TrendArrow</c>: <c>up</c>, <c>down</c> and <c>flat</c>), so it does not rely on colour alone.
/// </summary>
public sealed partial class TrendArrow : Control
{
    private const float ArrowSize = 18f;

    private Trend _trend = Trend.Flat;

    /// <summary>Initializes a new instance of the <see cref="TrendArrow"/> class.</summary>
    public TrendArrow()
    {
        CustomMinimumSize = new Vector2(ArrowSize, ArrowSize);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    /// <summary>Gets or sets the direction to draw.</summary>
    public Trend Trend
    {
        get => _trend;
        set
        {
            if (_trend == value)
            {
                return;
            }

            _trend = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        const float inset = 2f;
        float width = ArrowSize;
        float height = ArrowSize;

        switch (_trend)
        {
            case Trend.Up:
                DrawColoredPolygon(
                    [new Vector2(width / 2f, inset + 1f), new Vector2(width - inset, height - inset - 1f), new Vector2(inset, height - inset - 1f)],
                    GetThemeColor("up", "TrendArrow"));
                break;
            case Trend.Down:
                DrawColoredPolygon(
                    [new Vector2(inset, inset + 1f), new Vector2(width - inset, inset + 1f), new Vector2(width / 2f, height - inset - 1f)],
                    GetThemeColor("down", "TrendArrow"));
                break;
            default:
                DrawRect(new Rect2(inset, (height / 2f) - 1.5f, width - (2f * inset), 3f), GetThemeColor("flat", "TrendArrow"));
                break;
        }
    }
}
