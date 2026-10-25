using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using ContentCurve = Evenmere.Content.Definitions.SeasonCurve;

namespace Evenmere.Sim.Systems.Production.Seasons;

/// <summary>
/// Reads the season yield curve from <c>RulesDef.Seasons</c>: buildings whose tags name a curve multiply their
/// productivity by that season's factor, and everything else runs at full speed. All maths is integer per-mille.
/// </summary>
public static class SeasonCurve
{
    /// <summary>Gets the productivity factor for one tag in one season, in per-mille.</summary>
    /// <param name="rules">The game rules holding the season curves, keyed by building tag.</param>
    /// <param name="tag">The building tag to look up.</param>
    /// <param name="season">The season.</param>
    /// <returns>The curve's factor for the season, or 1000 when no curve names the tag.</returns>
    public static int FactorFor(RulesDef rules, string tag, Season season)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tag);

        foreach (ContentCurve curve in rules.Seasons)
        {
            if (string.Equals(curve.Tag, tag, StringComparison.Ordinal))
            {
                return curve.Of(season).PerMille;
            }
        }

        return ProductionMath.One;
    }

    /// <summary>
    /// Builds the dense per-type per-season factor table the tick reads: one row of
    /// <see cref="GameTime.SeasonsPerYear"/> per-mille factors per building type, in building-ID order, so the
    /// per-tick path is a single array lookup with no string work. Rebuilt whenever the system is constructed,
    /// which a content remap always does because loading constructs new systems over the new content.
    /// </summary>
    /// <param name="content">The content the factors are read from.</param>
    /// <returns>The table; entry <c>type * SeasonsPerYear + (int)season</c> holds that building's factor.</returns>
    public static int[] BuildTable(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);

        int[] table = new int[content.Buildings.Count * GameTime.SeasonsPerYear];
        for (int type = 0; type < content.Buildings.Count; type++)
        {
            BuildingDef def = content.Get(BuildingTypeId.From((ushort)type));
            foreach (Season season in Enum.GetValues<Season>())
            {
                table[type * GameTime.SeasonsPerYear + (int)season] = FactorFor(content.Rules, def, season);
            }
        }

        return table;
    }

    /// <summary>Gets the productivity factor for a building in one season, in per-mille.</summary>
    /// <param name="rules">The game rules holding the season curves, keyed by building tag.</param>
    /// <param name="building">The building; its first tag that names a curve decides (curves are sorted by tag).</param>
    /// <param name="season">The season.</param>
    /// <returns>The curve's factor for the season, or 1000 when none of the building's tags names a curve.</returns>
    public static int FactorFor(RulesDef rules, BuildingDef building, Season season)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(building);

        foreach (ContentCurve curve in rules.Seasons)
        {
            foreach (string tag in building.Tags)
            {
                if (string.Equals(tag, curve.Tag, StringComparison.Ordinal))
                {
                    return curve.Of(season).PerMille;
                }
            }
        }

        return ProductionMath.One;
    }
}
