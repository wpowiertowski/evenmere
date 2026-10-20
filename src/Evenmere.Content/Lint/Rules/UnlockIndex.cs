using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Lint.Rules;

/// <summary>Where an unlock comes from.</summary>
internal enum UnlockSourceKind
{
    /// <summary>The item sets <c>"start": true</c>.</summary>
    Start,

    /// <summary>An <c>unlockX</c> effect of a tech.</summary>
    Tech,

    /// <summary>An <c>unlockX</c> effect of a civic.</summary>
    Civic,

    /// <summary>An <c>unlockX</c> effect of a monument.</summary>
    Monument,
}

/// <summary>One way an item becomes available.</summary>
/// <param name="Kind">What kind of source it is.</param>
/// <param name="Id">The ID of the tech, civic or monument; the item's own ID for <see cref="UnlockSourceKind.Start"/>.</param>
/// <param name="Era">The era number the source belongs to; 1 for <see cref="UnlockSourceKind.Start"/>.</param>
/// <param name="File">The file that holds the source.</param>
/// <param name="Pointer">The pointer to the effect (or to <c>start</c>) inside that file.</param>
internal sealed record UnlockSource(UnlockSourceKind Kind, string Id, int Era, string File, string Pointer)
{
    /// <summary>Describes the source for a message, with its location.</summary>
    public string Describe() => Kind == UnlockSourceKind.Start
        ? $"\"start\": true ({File}#{Pointer})"
        : $"{Kind.ToString().ToLowerInvariant()} '{Id}' ({File}#{Pointer})";
}

/// <summary>
/// Every unlock source of every building, recipe, tier, tradition and festival. A source is the item's own
/// <c>start</c> flag, or an <c>unlockX</c> effect in a tech, civic or monument (ADR-0004: unlocks have one source of truth).
/// Each list is ordered the same way every run: start first, then techs, civics and monuments in ID order, and effects in
/// file order.
/// </summary>
internal sealed class UnlockIndex
{
    private readonly List<UnlockSource>[] _buildings;
    private readonly List<UnlockSource>[] _recipes;
    private readonly List<UnlockSource>[] _tiers;
    private readonly List<UnlockSource>[] _traditions;
    private readonly List<UnlockSource>[] _festivals;

    private UnlockIndex(ContentDb db)
    {
        _buildings = NewLists(db.Buildings.Count);
        _recipes = NewLists(db.Recipes.Count);
        _tiers = NewLists(db.Tiers.Count);
        _traditions = NewLists(db.Traditions.Count);
        _festivals = NewLists(db.Festivals.Count);
    }

    public IReadOnlyList<UnlockSource> Of(BuildingTypeId id) => _buildings[id.Value];

    public IReadOnlyList<UnlockSource> Of(RecipeId id) => _recipes[id.Value];

    public IReadOnlyList<UnlockSource> Of(TierId id) => _tiers[id.Value];

    public IReadOnlyList<UnlockSource> Of(TraditionId id) => _traditions[id.Value];

    public IReadOnlyList<UnlockSource> Of(FestivalId id) => _festivals[id.Value];

    /// <summary>
    /// Gets the earliest era in which an item is available: the lowest era among its sources, or
    /// <see cref="int.MaxValue"/> when it has none (EVL005 reports that).
    /// </summary>
    public static int EraOf(IReadOnlyList<UnlockSource> sources)
    {
        int era = int.MaxValue;
        foreach (UnlockSource source in sources)
        {
            era = Math.Min(era, source.Era);
        }

        return era;
    }

    public static UnlockIndex Build(LintContext context)
    {
        ContentDb db = context.Db;
        var index = new UnlockIndex(db);

        index.AddStarts(context);

        for (int i = 0; i < db.Techs.Count; i++)
        {
            TechDef tech = db.Techs.Items[i];
            string id = db.Techs.Names[i];
            index.AddEffects(UnlockSourceKind.Tech, id, tech.Era, context.FileOf(ContentType.Techs, id), tech.Effects);
        }

        for (int i = 0; i < db.Civics.Count; i++)
        {
            CivicDef civic = db.Civics.Items[i];
            string id = db.Civics.Names[i];
            index.AddEffects(UnlockSourceKind.Civic, id, civic.Era, context.FileOf(ContentType.Civics, id), civic.Effects);
        }

        for (int i = 0; i < db.Monuments.Count; i++)
        {
            MonumentDef monument = db.Monuments.Items[i];
            string id = db.Monuments.Names[i];

            // A monument has no era of its own; it is as early as the building it raises.
            int era = db.Get(monument.Building).Era;
            index.AddEffects(UnlockSourceKind.Monument, id, era, context.FileOf(ContentType.Monuments, id), monument.Effects);
        }

        return index;
    }

    private static List<UnlockSource>[] NewLists(int count)
    {
        var lists = new List<UnlockSource>[count];
        for (int i = 0; i < count; i++)
        {
            lists[i] = [];
        }

        return lists;
    }

    private void AddStarts(LintContext context)
    {
        ContentDb db = context.Db;
        for (int i = 0; i < db.Buildings.Count; i++)
        {
            if (db.Buildings.Items[i].Start)
            {
                _buildings[i].Add(StartSource(context, ContentType.Buildings, db.Buildings.Names[i]));
            }
        }

        for (int i = 0; i < db.Recipes.Count; i++)
        {
            if (db.Recipes.Items[i].Start)
            {
                _recipes[i].Add(StartSource(context, ContentType.Recipes, db.Recipes.Names[i]));
            }
        }

        for (int i = 0; i < db.Tiers.Count; i++)
        {
            if (db.Tiers.Items[i].Start)
            {
                _tiers[i].Add(StartSource(context, ContentType.Tiers, db.Tiers.Names[i]));
            }
        }

        for (int i = 0; i < db.Traditions.Count; i++)
        {
            if (db.Traditions.Items[i].Start)
            {
                _traditions[i].Add(StartSource(context, ContentType.Traditions, db.Traditions.Names[i]));
            }
        }

        for (int i = 0; i < db.Festivals.Count; i++)
        {
            if (db.Festivals.Items[i].Start)
            {
                _festivals[i].Add(StartSource(context, ContentType.Festivals, db.Festivals.Names[i]));
            }
        }
    }

    private static UnlockSource StartSource(LintContext context, ContentType type, string id) =>
        new(UnlockSourceKind.Start, id, 1, context.FileOf(type, id), "/start");

    private void AddEffects(UnlockSourceKind kind, string id, int era, string file, ImmutableArray<Effect> effects)
    {
        for (int i = 0; i < effects.Length; i++)
        {
            // The loader keeps effects in file order, so the array index is the index in the file.
            var source = new UnlockSource(kind, id, era, file, LintContext.ItemPointer("effects", i));
            switch (effects[i])
            {
                case UnlockBuildingEffect e:
                    _buildings[e.Building.Value].Add(source);
                    break;
                case UnlockRecipeEffect e:
                    _recipes[e.Recipe.Value].Add(source);
                    break;
                case UnlockTierEffect e:
                    _tiers[e.Tier.Value].Add(source);
                    break;
                case UnlockTraditionEffect e:
                    _traditions[e.Tradition.Value].Add(source);
                    break;
                case UnlockFestivalEffect e:
                    _festivals[e.Festival.Value].Add(source);
                    break;
                default:
                    break;
            }
        }
    }
}
