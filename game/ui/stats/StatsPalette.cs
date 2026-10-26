namespace Evenmere.Game.Ui.Stats;

/// <summary>
/// The line colours for the statistics graphs. They are the Okabe-Ito palette, which stays distinct for the
/// common forms of colour blindness (deuteranopia, protanopia, tritanopia), on the warm paper panels of
/// <c>evenmere_theme.tres</c>. No theme edit is needed: the hexes live here, next to the charts that use them.
/// </summary>
/// <remarks>
/// Order is contrast-first: blue, vermilion, bluish green, orange, sky blue and reddish purple all read clearly
/// on paper; black is the sixth series; yellow is last because it is faint on paper and is only used when a chart
/// really has eight lines. Population tiers cycle the first seven; the single-series charts take fixed roles.
/// </remarks>
public static class StatsPalette
{
    /// <summary>How many series colours the palette holds.</summary>
    public const int Count = 8;

    /// <summary>The role colour of a goods-stock line.</summary>
    public const int Stock = 0;

    /// <summary>The role colour of a net-per-day line.</summary>
    public const int Net = 1;

    /// <summary>The role colour of the knowledge-per-day line.</summary>
    public const int Knowledge = 2;

    /// <summary>The role colour of the culture-per-day line.</summary>
    public const int Culture = 5;

    private static readonly string[] _hex =
    [
        "0072B2", // blue
        "D55E00", // vermilion
        "009E73", // bluish green
        "E69F00", // orange
        "56B4E9", // sky blue
        "CC79A7", // reddish purple
        "000000", // black
        "F0E442", // yellow, last resort on paper
    ];

    /// <summary>Gets the hex colour of a palette entry, without the leading hash.</summary>
    /// <param name="index">The entry; wraps around <see cref="Count"/>.</param>
    /// <returns>The six-digit hex string.</returns>
    public static string Hex(int index) => _hex[Mod(index)];

    /// <summary>Gets the colour of a population tier line by tier order, skipping yellow until it is needed.</summary>
    /// <param name="order">The tier's order from the lowest tier up; not negative.</param>
    /// <returns>The palette index.</returns>
    public static int PopulationTier(int order)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        return order < Count - 1 ? order : Mod(order);
    }

    private static int Mod(int index) => ((index % Count) + Count) % Count;
}
