using Evenmere.Core.Time;

namespace Evenmere.Game.Rendering.Seasons;

/// <summary>
/// One season's look as plain floats: a terrain tint multiplier, a snow cover weight, a blossom-petal weight and
/// the key-light colour. It stays Godot-free so the blend maths is unit-tested; the renderer turns it into colours.
/// </summary>
/// <param name="TintR">The terrain red multiplier.</param>
/// <param name="TintG">The terrain green multiplier.</param>
/// <param name="TintB">The terrain blue multiplier.</param>
/// <param name="Snow">The snow cover, 0 (bare) to 1 (full winter white).</param>
/// <param name="Blossom">The blossom-petal weight, 0 (none) to 1 (full spring drift).</param>
/// <param name="LightR">The key-light red, sRGB.</param>
/// <param name="LightG">The key-light green, sRGB.</param>
/// <param name="LightB">The key-light blue, sRGB.</param>
public readonly record struct SeasonTint(
    float TintR,
    float TintG,
    float TintB,
    float Snow,
    float Blossom,
    float LightR,
    float LightG,
    float LightB)
{
    /// <summary>Lerps two tints component-wise.</summary>
    /// <param name="from">The outgoing season's tint.</param>
    /// <param name="to">The incoming season's tint.</param>
    /// <param name="t">The blend, 0 (fully <paramref name="from"/>) to 1 (fully <paramref name="to"/>).</param>
    /// <returns>The blended tint.</returns>
    public static SeasonTint Lerp(SeasonTint from, SeasonTint to, float t)
    {
        float clamped = Math.Clamp(t, 0f, 1f);
        return new SeasonTint(
            from.TintR + ((to.TintR - from.TintR) * clamped),
            from.TintG + ((to.TintG - from.TintG) * clamped),
            from.TintB + ((to.TintB - from.TintB) * clamped),
            from.Snow + ((to.Snow - from.Snow) * clamped),
            from.Blossom + ((to.Blossom - from.Blossom) * clamped),
            from.LightR + ((to.LightR - from.LightR) * clamped),
            from.LightG + ((to.LightG - from.LightG) * clamped),
            from.LightB + ((to.LightB - from.LightB) * clamped));
    }
}

/// <summary>
/// The seasonal palette (M2-24): what each season looks like, and the two-game-day blend between seasons.
/// Targets are gentle multipliers around 1.0 so the hand-drawn calibration (M2-32, ±8% of the tokens) survives:
/// autumn reads golden, winter pale and snowy, spring fresh with blossom drift, summer neutral and warm.
/// </summary>
public static class SeasonPalette
{
    /// <summary>How many game days a season change blends over.</summary>
    public const int BlendDays = 2;

    /// <summary>The snow-mix ceiling in the terrain shader (M2-24 round 2): a light tint, never a whiteout.</summary>
    public const float SnowCeiling = 0.22f;

    /// <summary>Gets the season before the given one, wrapping winter back to autumn's predecessor correctly.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The previous season in calendar order.</returns>
    public static Season Previous(Season season) => (Season)(((int)season + 3) % GameTime.SeasonsPerYear);

    /// <summary>Gets the full-strength look of one season.</summary>
    /// <param name="season">The season.</param>
    /// <returns>Its tint.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The season is not a known season.</exception>
    public static SeasonTint Target(Season season) => season switch
    {
        // Spring is the fresh one: a lighter yellow-green lift over the summer base, which stays the neutral 1.0.
        Season.Spring => new SeasonTint(1.04f, 1.07f, 0.93f, 0f, 1f, 1.00f, 0.88f, 0.72f),
        Season.Summer => new SeasonTint(1.00f, 1.00f, 1.00f, 0f, 0f, 1.00f, 0.90f, 0.68f),
        Season.Autumn => new SeasonTint(1.07f, 1.00f, 0.86f, 0f, 0f, 1.00f, 0.80f, 0.57f),
        // Winter stays warm: a faint warm-grey grade under a light paper-white cover. Never a cool cast.
        Season.Winter => new SeasonTint(1.02f, 0.99f, 0.94f, 1f, 0f, 1.00f, 0.95f, 0.88f),
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "The season is not known."),
    };

    /// <summary>
    /// Samples the look for a day inside a season: the previous season's target blended toward this season's over
    /// the first <see cref="BlendDays"/> game days, then the full target.
    /// </summary>
    /// <param name="season">The current season.</param>
    /// <param name="dayOfSeason">The day within the season, 0 to <c>DaysPerSeason − 1</c>.</param>
    /// <returns>The blended tint.</returns>
    public static SeasonTint Sample(Season season, int dayOfSeason)
    {
        if (dayOfSeason >= BlendDays)
        {
            return Target(season);
        }

        float t = Math.Clamp(dayOfSeason, 0, BlendDays) / (float)BlendDays;
        return SeasonTint.Lerp(Target(Previous(season)), Target(season), t);
    }
}
