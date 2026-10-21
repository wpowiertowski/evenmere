using Evenmere.Content.Definitions;

namespace Evenmere.Game.Ui.BuildMenu;

/// <summary>
/// The five tabs of the build menu, in display order: Housing, Production, Storage &amp; Roads, Services and
/// Culture &amp; Knowledge. The order matches the card, so keyboard mnemonics and the tab bar never disagree.
/// </summary>
public enum BuildTab
{
    /// <summary>Homes for residents.</summary>
    Housing = 0,

    /// <summary>Gatherers and workshops that run recipes.</summary>
    Production = 1,

    /// <summary>Storehouses, roads and the demolish tool.</summary>
    Storage = 2,

    /// <summary>Buildings that serve residents in range.</summary>
    Services = 3,

    /// <summary>Knowledge and culture buildings.</summary>
    Culture = 4,
}

/// <summary>
/// Maps each building to its build-menu tab from the content <c>category</c> and <c>tags</c>, in one table.
/// Roads (any building the sim can lay as a road, or tagged <c>road</c>) always sit in Storage &amp; Roads, even if
/// content files them elsewhere, so there is exactly one place to look for them. Unique buildings such as the
/// Founders' Hall are not mapped: they are pre-placed and can never be built twice, so the view-model skips them.
/// </summary>
public static class BuildCategory
{
    /// <summary>Gets how many tabs the menu shows. It matches the number of <see cref="BuildTab"/> values.</summary>
    public const int TabCount = 5;

    /// <summary>
    /// Finds the tab for a building definition.
    /// </summary>
    /// <param name="definition">The building definition.</param>
    /// <returns>The tab that lists it.</returns>
    public static BuildTab TabFor(BuildingDef definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Roads first: a road-tagged or road-cost building is a road whatever its category says.
        if (definition.RoadCost.HasValue || HasTag(definition, "road"))
        {
            return BuildTab.Storage;
        }

        return definition.Category switch
        {
            BuildingCategory.Residence => BuildTab.Housing,
            BuildingCategory.Production => BuildTab.Production,
            BuildingCategory.Infrastructure => BuildTab.Storage,
            BuildingCategory.Service => BuildTab.Services,
            BuildingCategory.Culture => BuildTab.Culture,
            BuildingCategory.Knowledge => BuildTab.Culture,

            // Future categories fall back to the closest tab, so a new category never drops a building
            // from the menu: pleasant ground serves residents, monuments celebrate culture.
            BuildingCategory.Decoration => BuildTab.Services,
            BuildingCategory.Monument => BuildTab.Culture,
            BuildingCategory.Wonder => BuildTab.Culture,
            _ => BuildTab.Production,
        };
    }

    private static bool HasTag(BuildingDef definition, string tag)
    {
        foreach (string entry in definition.Tags)
        {
            if (string.Equals(entry, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
