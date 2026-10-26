namespace Evenmere.Game.Ui.Stats;

/// <summary>
/// Pure chart geometry: data bounds, data-to-pixel mapping and hover lookup. It has no Godot types (points are
/// plain tuples), so the game tests cover it on its own. The <see cref="LineChart"/> control is the only caller.
/// </summary>
public static class ChartGeometry
{
    /// <summary>
    /// Gets the axis bounds of plotted values, padded by a tenth of the range on each side so the line never
    /// touches the frame. A flat run spans one display unit each way, so its axis labels still differ.
    /// </summary>
    /// <param name="values">The plotted values.</param>
    /// <param name="floorZero">Whether the axis never drops below zero, for series that cannot be negative.</param>
    /// <param name="displayUnit">What one unit on the axis means in raw values (1 for counts, 1000 for milli-units).</param>
    /// <returns>The padded minimum and maximum; (0, 1) when there are no values.</returns>
    public static (long Min, long Max) Bounds(ReadOnlySpan<long> values, bool floorZero = false, long displayUnit = 1)
    {
        if (values.Length == 0)
        {
            return (0, 1);
        }

        long min = values[0];
        long max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }

            if (values[i] > max)
            {
                max = values[i];
            }
        }

        if (min == max)
        {
            long unit = displayUnit <= 0 ? 1 : displayUnit;
            long low = min - unit;
            long high = max + unit;
            if (floorZero && low < 0)
            {
                low = 0;
            }

            if (low >= high)
            {
                low = min - 1;
                high = max + 1;
            }

            return (low, high);
        }

        long pad = Math.Max(1, (max - min) / 10);
        long floor = min - pad;
        if (floorZero && floor < 0)
        {
            floor = 0;
        }

        return (floor, max + pad);
    }

    /// <summary>
    /// Lays legend entries into rows, in order, wrapping when the next entry would pass the plot width. An entry
    /// wider than the plot keeps a row of its own. Row heights are uniform, so the chart can reserve the strip.
    /// </summary>
    /// <param name="entryWidths">The width of each legend entry, in pixels; not negative.</param>
    /// <param name="maxWidth">The plot width in pixels; positive.</param>
    /// <returns>The row index of each entry, in order; empty when there are no entries.</returns>
    public static int[] LegendRowOf(float[] entryWidths, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(entryWidths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        int[] rows = new int[entryWidths.Length];
        int row = 0;
        float x = 0f;
        for (int i = 0; i < entryWidths.Length; i++)
        {
            float width = entryWidths[i];
            ArgumentOutOfRangeException.ThrowIfNegative(width);
            if (x > 0f && x + width > maxWidth)
            {
                row++;
                x = 0f;
            }

            rows[i] = row;
            x += width;
        }

        return rows;
    }

    /// <summary>
    /// Maps one plotted point into a plot rectangle whose origin is the top-left, as Godot draws.
    /// </summary>
    /// <param name="index">The point's position, oldest first.</param>
    /// <param name="value">The point's value.</param>
    /// <param name="count">How many points the line holds; positive.</param>
    /// <param name="min">The axis minimum, below every value.</param>
    /// <param name="max">The axis maximum, above every value.</param>
    /// <param name="plotWidth">The plot width in pixels; not negative.</param>
    /// <param name="plotHeight">The plot height in pixels; not negative.</param>
    /// <returns>The pixel position of the point within the plot rectangle.</returns>
    public static (float X, float Y) Map(
        int index, long value, int count, long min, long max, float plotWidth, float plotHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is past the line.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(plotWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(plotHeight);
        if (max <= min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "The maximum must be above the minimum.");
        }

        float x = count == 1 ? plotWidth / 2f : (float)index / (count - 1) * plotWidth;
        float y = (float)(max - value) / (max - min) * plotHeight;
        return (x, y);
    }

    /// <summary>
    /// Finds the plotted point nearest a horizontal pixel position, for hover.
    /// </summary>
    /// <param name="x">The pixel position within the plot rectangle.</param>
    /// <param name="plotWidth">The plot width in pixels; positive.</param>
    /// <param name="count">How many points the line holds; positive.</param>
    /// <returns>The nearest point's index, or -1 when the line is empty.</returns>
    public static int HoverIndex(float x, float plotWidth, int count)
    {
        if (count <= 0 || plotWidth <= 0f)
        {
            return -1;
        }

        if (count == 1)
        {
            return 0;
        }

        int index = (int)Math.Round(x / plotWidth * (count - 1), MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, count - 1);
    }
}
