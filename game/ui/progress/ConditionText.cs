using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// Turns an inspiration condition into a one-line sentence from locale keys, covering every condition kind
/// the content uses. It has no Godot types and reads only <see cref="UiStrings"/> and
/// <see cref="IConditionNames"/>, so the game test project can exercise it on its own.
/// </summary>
public static class ConditionText
{
    /// <summary>Formats a condition as a sentence.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="names">The content names.</param>
    /// <returns>The sentence.</returns>
    public static string Format(Condition condition, UiStrings strings, IConditionNames names)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(names);

        return condition switch
        {
            CountCondition count => FormatCount(count, strings, names),
            ProducedCondition produced => strings.Format(
                "ui.condition.produced",
                ("amount", NumberFormat.Units(produced.AtLeast)),
                ("good", names.Good(produced.Good))),
            ConsumedCondition consumed => strings.Format(
                "ui.condition.consumed",
                ("amount", NumberFormat.Units(consumed.AtLeast)),
                ("good", names.Good(consumed.Good))),
            StockCondition stock => strings.Format(
                "ui.condition.stock",
                ("amount", NumberFormat.Units(stock.AtLeast)),
                ("good", names.Good(stock.Good))),
            PopulationCondition population => population.Tier is { } tier
                ? strings.Format(
                    "ui.condition.population.tier",
                    ("count", NumberFormat.Count(population.AtLeast)),
                    ("tier", names.Tier(tier)))
                : strings.Format("ui.condition.population.total", ("count", NumberFormat.Count(population.AtLeast))),
            RoadTilesCondition roads => roads.Road is { } road
                ? strings.Format(
                    "ui.condition.roadTiles.road",
                    ("count", NumberFormat.Count(roads.AtLeast)),
                    ("road", names.Building(road)))
                : strings.Format("ui.condition.roadTiles", ("count", NumberFormat.Count(roads.AtLeast))),
            ExpeditionsCondition expeditions => strings.Format(
                "ui.condition.expeditions", ("count", NumberFormat.Count(expeditions.AtLeast))),
            FestivalsHeldCondition festivals => strings.Format(
                "ui.condition.festivals", ("count", NumberFormat.Count(festivals.AtLeast))),
            GreatWorksCondition works => works.Type is { } type
                ? strings.Format(
                    "ui.condition.greatWorks.typed",
                    ("count", NumberFormat.Count(works.AtLeast)),
                    ("type", names.GreatWorkType(type)))
                : strings.Format("ui.condition.greatWorks", ("count", NumberFormat.Count(works.AtLeast))),
            VisitorsPerDayCondition visitors => strings.Format(
                "ui.condition.visitors", ("count", NumberFormat.Count(visitors.AtLeast))),
            ResearchedCondition researched => strings.Format(
                "ui.condition.researched", ("tech", names.Tech(researched.Tech))),
            AdoptedCondition adopted => strings.Format(
                "ui.condition.adopted", ("civic", names.Civic(adopted.Civic))),
            DiscoveredCondition discovered => FormatDiscovered(discovered, strings, names),
            SeasonCondition season => strings.Format(
                "ui.condition.season", ("season", names.Season(season.Season))),
            EraCondition era => strings.Format(
                "ui.condition.era", ("era", names.Era(era.Era))),
            AllCondition all => strings.Format(
                "ui.condition.all",
                ("parts", Join(all.Conditions, strings.Get("ui.condition.list.and"), strings, names))),
            AnyCondition any => strings.Format(
                "ui.condition.any",
                ("parts", Join(any.Conditions, strings.Get("ui.condition.list.or"), strings, names))),
            NotCondition not => strings.Format(
                "ui.condition.not", ("inner", Format(not.Condition, strings, names))),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown condition kind."),
        };
    }

    private static string FormatCount(CountCondition count, UiStrings strings, IConditionNames names)
    {
        string what = count.Selector switch
        {
            BuildingTypeSelector building => names.Building(building.Building),
            BuildingTagSelector tag => names.Tag(tag.Tag),
            BuildingCategorySelector category => names.Category(category.Category),
            null => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(count), count.Selector, "Unknown building selector."),
        };

        string amount = NumberFormat.Count(count.AtLeast);
        return count.Terrain is { } terrain
            ? strings.Format(
                "ui.condition.count.terrain",
                ("count", amount),
                ("what", what),
                ("terrain", names.Terrain(terrain)))
            : strings.Format("ui.condition.count", ("count", amount), ("what", what));
    }

    private static string FormatDiscovered(DiscoveredCondition discovered, UiStrings strings, IConditionNames names) =>
        discovered.Kind is { } kind
            ? strings.Format("ui.condition.discovered.kind", ("what", names.Discovery(kind)))
            : strings.Format(
                "ui.condition.discovered.region",
                ("count", NumberFormat.Count(discovered.AtLeast)),
                ("terrain", discovered.Region is { } region ? names.Terrain(region) : string.Empty));

    private static string Join(
        System.Collections.Immutable.ImmutableArray<Condition> parts,
        string separator,
        UiStrings strings,
        IConditionNames names)
    {
        var joined = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                joined.Append(separator);
            }

            joined.Append(Format(parts[i], strings, names));
        }

        return joined.ToString();
    }
}
