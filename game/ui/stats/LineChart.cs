using System;
using System.Collections.Generic;
using Godot;

namespace Evenmere.Game.Ui.Stats;

/// <summary>
/// One line a <see cref="LineChart"/> draws: a legend label, plotted values oldest first, its palette colour and
/// whether the values are milli-units (rates show one decimal) or whole counts.
/// </summary>
/// <param name="Label">The legend label.</param>
/// <param name="Values">The plotted values, oldest first.</param>
/// <param name="Color">The line colour, from <see cref="StatsPalette"/>.</param>
/// <param name="IsMilli">Whether the values are milli-units.</param>
/// <param name="FloorZero">Whether the axis never drops below zero, for series that cannot be negative.</param>
public sealed record ChartLine(string Label, long[] Values, Color Color, bool IsMilli, bool FloorZero = true);

/// <summary>
/// A small multi-line chart for the statistics panel: gridlines with min/mid/max labels, one polyline per
/// series in its <see cref="StatsPalette"/> colour, day labels on the axis, and a hover marker. Hover follows
/// the mouse; keyboard and gamepad focus shows the newest point instead, so the built-in <c>ui_*</c> focus
/// actions keep working untouched (no arrow keys are consumed).
/// </summary>
public sealed partial class LineChart : Control
{
    private static readonly Color _gridColour = new(0.37f, 0.42f, 0.46f, 0.35f);
    private static readonly Color _textColour = new(0.11f, 0.17f, 0.21f, 1f);
    private static readonly Color _hoverColour = new(0.11f, 0.17f, 0.21f, 0.6f);

    private const float LineWidth = 2f;
    private const float DotRadius = 3.5f;
    private const float PadLeft = 44f;
    private const float PadRight = 8f;
    private const float PadTop = 6f;
    private const float PadBottom = 30f;
    private const float LegendSwatch = 14f;
    private const float LegendGap = 12f;

    private IReadOnlyList<ChartLine> _lines = [];
    private long _min;
    private long _max = 1;
    private string _firstAxisText = string.Empty;
    private string _lastAxisText = string.Empty;
    private int _hover = -1;
    private string _emptyText = "No data yet.";

    /// <summary>Raised when the hovered point changes, so the owning panel can show the exact values.</summary>
    public event Action? HoverChanged;

    /// <summary>Gets the hovered point's index, oldest first, or -1 for none.</summary>
    public int HoverIndex => _hover;

    /// <summary>Gets how many points the longest line holds.</summary>
    public int PointCount
    {
        get
        {
            int n = 0;
            foreach (ChartLine line in _lines)
            {
                n = Math.Max(n, line.Values.Length);
            }

            return n;
        }
    }

    /// <summary>Shows a chart.</summary>
    /// <param name="lines">The lines to draw.</param>
    /// <param name="startDay">The day number of the oldest plotted point, for axis labels.</param>
    /// <param name="dayStep">How many days one plotted point covers.</param>
    /// <param name="emptyText">What to draw when every line is empty.</param>
    public void SetData(IReadOnlyList<ChartLine> lines, int startDay, int dayStep, string emptyText) =>
        SetData(lines, DayText(startDay), DayText(startDay + ((PointCountOf(lines) - 1) * Math.Max(1, dayStep))), emptyText);

    /// <summary>Shows a chart with explicit axis labels (the long view names seasons, not days).</summary>
    /// <param name="lines">The lines to draw.</param>
    /// <param name="firstLabel">The axis label of the oldest plotted point.</param>
    /// <param name="lastLabel">The axis label of the newest plotted point.</param>
    /// <param name="emptyText">What to draw when every line is empty.</param>
    public void SetData(IReadOnlyList<ChartLine> lines, string firstLabel, string lastLabel, string emptyText)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(firstLabel);
        ArgumentNullException.ThrowIfNull(lastLabel);
        _lines = lines;
        _firstAxisText = firstLabel;
        _lastAxisText = lastLabel;
        _emptyText = emptyText;

        int total = 0;
        foreach (ChartLine line in lines)
        {
            total += line.Values.Length;
        }

        long[] joined = new long[total];
        int at = 0;
        foreach (ChartLine line in lines)
        {
            Array.Copy(line.Values, 0, joined, at, line.Values.Length);
            at += line.Values.Length;
        }

        (_min, _max) = ChartGeometry.Bounds(
            joined,
            _lines.Count > 0 && _lines.All(static line => line.FloorZero),
            _lines.Count > 0 && _lines[0].IsMilli ? 1000L : 1L);
        if (_hover >= PointCount)
        {
            _hover = -1;
            HoverChanged?.Invoke();
        }

        QueueRedraw();
    }

    /// <summary>Clears the hover marker.</summary>
    public void ClearHover()
    {
        if (_hover >= 0)
        {
            _hover = -1;
            HoverChanged?.Invoke();
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        MouseExited += ClearHover;
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion)
        {
            Vector2 local = GetLocalMousePosition();
            int count = PointCount;
            float plotWidth = Size.X - PadLeft - PadRight;
            int index = ChartGeometry.HoverIndex(local.X - PadLeft, plotWidth, count);
            if (index != _hover)
            {
                _hover = index;
                HoverChanged?.Invoke();
                QueueRedraw();
            }
        }
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationFocusEnter)
        {
            int count = PointCount;
            int newest = count == 0 ? -1 : count - 1;
            if (newest != _hover)
            {
                _hover = newest;
                HoverChanged?.Invoke();
                QueueRedraw();
            }
        }
        else if (what == NotificationFocusExit)
        {
            ClearHover();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        Font font = GetThemeDefaultFont();
        int fontSize = GetThemeDefaultFontSize();
        float plotWidth = Size.X - PadLeft - PadRight;
        if (plotWidth <= 0f)
        {
            return;
        }

        int count = PointCount;
        if (count == 0)
        {
            DrawEmptyText(font, fontSize, plotWidth);
            return;
        }

        // The legend gets its own strip above the plot, so swatches and labels never cover the lines.
        float[] entryWidths = new float[_lines.Count];
        for (int i = 0; i < _lines.Count; i++)
        {
            entryWidths[i] = LegendSwatch + 4f + font.GetStringSize(_lines[i].Label, fontSize: fontSize).X + LegendGap;
        }

        int[] rows = ChartGeometry.LegendRowOf(entryWidths, plotWidth);
        int rowCount = 0;
        foreach (int row in rows)
        {
            rowCount = Math.Max(rowCount, row + 1);
        }

        float rowHeight = fontSize + 6f;
        float plotTop = PadTop + (rowCount * rowHeight) + 4f;
        float plotHeight = Size.Y - plotTop - PadBottom;
        if (plotHeight <= 0f)
        {
            return;
        }

        DrawLegendStrip(font, fontSize, rows, rowHeight);
        DrawGrid(font, fontSize, plotWidth, plotHeight, plotTop, count);

        int shownHover = _hover >= 0 && _hover < count ? _hover : -1;
        if (shownHover >= 0)
        {
            (float hx, _) = ChartGeometry.Map(shownHover, _min, count, _min, _max, plotWidth, plotHeight);
            DrawLine(
                new Vector2(PadLeft + hx, plotTop),
                new Vector2(PadLeft + hx, plotTop + plotHeight),
                _hoverColour,
                1f);
        }

        foreach (ChartLine line in _lines)
        {
            DrawSeries(line, count, plotWidth, plotHeight, plotTop);
        }

        if (shownHover >= 0)
        {
            foreach (ChartLine line in _lines)
            {
                if (shownHover < line.Values.Length)
                {
                    (float x, float y) = ChartGeometry.Map(shownHover, line.Values[shownHover], line.Values.Length, _min, _max, plotWidth, plotHeight);
                    DrawCircle(new Vector2(PadLeft + x, plotTop + y), DotRadius, line.Color);
                }
            }
        }
    }

    private void DrawGrid(Font font, int fontSize, float plotWidth, float plotHeight, float plotTop, int count)
    {
        long[] levels = [_min, (_min + _max) / 2, _max];
        string[] texts = [AxisText(_min), AxisText((_min + _max) / 2), AxisText(_max)];
        for (int i = 0; i < levels.Length; i++)
        {
            // Map through the geometry helper so the grid and the series share one mapping.
            (float _, float y) = ChartGeometry.Map(0, levels[i], count, _min, _max, plotWidth, plotHeight);
            float lineY = plotTop + y;
            DrawLine(new Vector2(PadLeft, lineY), new Vector2(PadLeft + plotWidth, lineY), _gridColour, 1f);
            DrawString(font, new Vector2(2f, lineY + (fontSize / 2f) - 1f), texts[i], fontSize: fontSize, modulate: _textColour);
        }

        string first = _firstAxisText;
        string last = _lastAxisText;
        float baseY = plotTop + plotHeight + fontSize + 4f;
        DrawString(font, new Vector2(PadLeft, baseY), first, fontSize: fontSize, modulate: _textColour);
        Vector2 lastSize = font.GetStringSize(last, fontSize: fontSize);
        DrawString(
            font,
            new Vector2(PadLeft + plotWidth - lastSize.X, baseY),
            last,
            fontSize: fontSize,
            modulate: _textColour);
    }

    private void DrawSeries(ChartLine line, int count, float plotWidth, float plotHeight, float plotTop)
    {
        if (line.Values.Length == 0)
        {
            return;
        }

        if (line.Values.Length == 1)
        {
            (float x, float y) = ChartGeometry.Map(0, line.Values[0], 1, _min, _max, plotWidth, plotHeight);
            DrawCircle(new Vector2(PadLeft + x, plotTop + y), DotRadius, line.Color);
            return;
        }

        var points = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            long value = line.Values[Math.Min(i, line.Values.Length - 1)];
            (float x, float y) = ChartGeometry.Map(i, value, count, _min, _max, plotWidth, plotHeight);
            points[i] = new Vector2(PadLeft + x, plotTop + y);
        }

        DrawPolyline(points, line.Color, LineWidth, true);
    }

    private void DrawLegendStrip(Font font, int fontSize, int[] rows, float rowHeight)
    {
        int rowCount = 0;
        foreach (int row in rows)
        {
            rowCount = Math.Max(rowCount, row + 1);
        }

        float[] cursors = new float[Math.Max(1, rowCount)];
        for (int i = 0; i < cursors.Length; i++)
        {
            cursors[i] = PadLeft;
        }

        for (int i = 0; i < _lines.Count; i++)
        {
            float x = cursors[rows[i]];
            float textY = PadTop + (rows[i] * rowHeight) + fontSize;
            DrawRect(new Rect2(x, textY - fontSize + ((fontSize - 4f) / 2f), LegendSwatch, 4f), _lines[i].Color);
            DrawString(font, new Vector2(x + LegendSwatch + 4f, textY), _lines[i].Label, fontSize: fontSize, modulate: _textColour);
            cursors[rows[i]] = x + LegendSwatch + 4f + font.GetStringSize(_lines[i].Label, fontSize: fontSize).X + LegendGap;
        }
    }

    private void DrawEmptyText(Font font, int fontSize, float plotWidth)
    {
        // The empty note wraps inside the plot, so longer calm messages never clip at narrow widths.
        float y = PadTop + (fontSize * 1.5f);
        foreach (string line in WrapEmptyText(_emptyText, plotWidth, word => font.GetStringSize(word, fontSize: fontSize).X))
        {
            DrawString(font, new Vector2(PadLeft, y), line, fontSize: fontSize, modulate: _textColour);
            y += fontSize + 2f;
        }
    }

    /// <summary>Wraps an empty-chart note to lines that fit the plot width, breaking at spaces.</summary>
    /// <param name="text">The note.</param>
    /// <param name="maxWidth">The plot width in pixels.</param>
    /// <param name="measure">Measures a string's width in pixels.</param>
    /// <returns>The lines, joined with single spaces they reproduce <paramref name="text"/>.</returns>
    internal static List<string> WrapEmptyText(string text, float maxWidth, Func<string, float> measure)
    {
        var lines = new List<string>();
        int start = 0;
        while (start < text.Length)
        {
            int end = text.Length;
            while (end > start && measure(text[start..end]) > maxWidth)
            {
                int space = text.LastIndexOf(' ', end - 1, end - start);
                if (space <= start)
                {
                    break;
                }

                end = space;
            }

            if (end == start)
            {
                end = text.Length;
            }

            lines.Add(text[start..end].TrimEnd());
            start = end;
            while (start < text.Length && text[start] == ' ')
            {
                start++;
            }
        }

        return lines;
    }

    private string AxisText(long raw)
    {
        if (_lines.Count == 0)
        {
            return raw.ToString();
        }

        return StatsViewModel.FormatValue(raw, _lines[0].IsMilli);
    }

    private static string DayText(long day) => $"Day {day}";

    private static int PointCountOf(IReadOnlyList<ChartLine> lines)
    {
        int n = 0;
        foreach (ChartLine line in lines)
        {
            n = Math.Max(n, line.Values.Length);
        }

        return n;
    }
}
