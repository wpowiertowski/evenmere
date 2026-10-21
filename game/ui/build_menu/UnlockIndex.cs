using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;

namespace Evenmere.Game.Ui.BuildMenu;

/// <summary>
/// View-side reverse lookup from a building to what unlocks it. <see cref="ContentDb"/> has no reverse
/// "unlocked by" query (unlocks live in tech and civic <c>effects</c>, and <c>start: true</c> marks what is
/// available from the start), so the menu builds this index once per world from the content and re-reads only the
/// locked or unlocked state through <c>IProgressionQueries</c> on refresh.
/// </summary>
public sealed class UnlockIndex
{
    /// <summary>Whether the unlock comes from the tech tree or the civics tree.</summary>
    public enum SourceKind
    {
        /// <summary>Unlocked by researching a tech.</summary>
        Tech,

        /// <summary>Unlocked by adopting a civic.</summary>
        Civic,
    }

    /// <summary>
    /// What unlocks one building: either it is available from the start, or one tech or civic lists it in its
    /// effects. Content lint (EVL005) guarantees exactly one unlock source per item; if two sources ever name one
    /// building, the first one in content order wins.
    /// </summary>
    /// <param name="FromStart">Whether the building is available from the start.</param>
    /// <param name="Kind">For a locked building, whether a tech or a civic unlocks it.</param>
    /// <param name="NodeName">For a locked building, the string ID of the tech or civic (for locale lookup).</param>
    public readonly record struct UnlockSource(bool FromStart, SourceKind Kind, string NodeName);

    private readonly Dictionary<BuildingTypeId, UnlockSource> _sources = new();

    private UnlockIndex()
    {
    }

    /// <summary>
    /// Scans every tech and civic effect for <c>unlockBuilding</c>, plus <c>start: true</c> for the buildings
    /// available from the start.
    /// </summary>
    /// <param name="content">The content database.</param>
    /// <returns>The index for that content.</returns>
    public static UnlockIndex Build(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var index = new UnlockIndex();

        foreach (TechDef tech in content.Techs.Items)
        {
            foreach (Effect effect in tech.Effects)
            {
                if (effect is UnlockBuildingEffect unlock)
                {
                    index._sources.TryAdd(unlock.Building, new UnlockSource(false, SourceKind.Tech, content.Techs.NameOf(tech.Id)));
                }
            }
        }

        foreach (CivicDef civic in content.Civics.Items)
        {
            foreach (Effect effect in civic.Effects)
            {
                if (effect is UnlockBuildingEffect unlock)
                {
                    index._sources.TryAdd(unlock.Building, new UnlockSource(false, SourceKind.Civic, content.Civics.NameOf(civic.Id)));
                }
            }
        }

        foreach (BuildingDef building in content.Buildings.Items)
        {
            if (building.Start)
            {
                index._sources[building.Id] = new UnlockSource(true, SourceKind.Tech, string.Empty);
            }
        }

        return index;
    }

    /// <summary>
    /// Finds what unlocks a building.
    /// </summary>
    /// <param name="building">The building type.</param>
    /// <param name="source">The source, when one is known.</param>
    /// <returns><see langword="true"/> when the content names a source for the building.</returns>
    public bool TryGet(BuildingTypeId building, out UnlockSource source) => _sources.TryGetValue(building, out source);
}
