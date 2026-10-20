using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL005: every building, tier, tradition and festival has exactly one unlock source, which is an <c>unlockX</c> effect of
/// a tech, civic or monument or its own <c>"start": true</c>. A recipe that a building names is available with that building
/// and needs no source of its own; a recipe that no building names needs exactly one.
/// </summary>
internal static class UnlockRule
{
    public static void Run(LintContext context, UnlockIndex index)
    {
        ContentDb db = context.Db;

        for (ushort i = 0; i < db.Buildings.Count; i++)
        {
            Check(context, ContentType.Buildings, db.Buildings.Names[i], "unlockBuilding", index.Of(BuildingTypeId.From(i)));
        }

        for (ushort i = 0; i < db.Tiers.Count; i++)
        {
            Check(context, ContentType.Tiers, db.Tiers.Names[i], "unlockTier", index.Of(TierId.From(i)));
        }

        for (ushort i = 0; i < db.Traditions.Count; i++)
        {
            Check(context, ContentType.Traditions, db.Traditions.Names[i], "unlockTradition", index.Of(TraditionId.From(i)));
        }

        for (ushort i = 0; i < db.Festivals.Count; i++)
        {
            Check(context, ContentType.Festivals, db.Festivals.Names[i], "unlockFestival", index.Of(FestivalId.From(i)));
        }

        bool[] namedByBuilding = new bool[db.Recipes.Count];
        foreach (BuildingDef building in db.Buildings.Items)
        {
            if (building.Recipe is { } recipe)
            {
                namedByBuilding[recipe.Value] = true;
            }
        }

        for (ushort i = 0; i < db.Recipes.Count; i++)
        {
            if (!namedByBuilding[i])
            {
                Check(context, ContentType.Recipes, db.Recipes.Names[i], "unlockRecipe", index.Of(RecipeId.From(i)));
            }
        }
    }

    private static void Check(LintContext context, ContentType type, string id, string effect, IReadOnlyList<UnlockSource> sources)
    {
        string noun = type.SingularName();
        string file = context.FileOf(type, id);

        if (sources.Count == 0)
        {
            string hint = type == ContentType.Recipes
                ? "no building names this recipe, so set \"start\": true or list it in an unlockRecipe effect"
                : $"set \"start\": true, or list it in the effects of one tech, civic or monument as {{ \"{effect}\": \"{id}\" }}";
            context.Error(LintCodes.Evl005, file, string.Empty, $"{noun} '{id}' has no unlock source; {hint}");
            return;
        }

        if (sources.Count > 1)
        {
            // When "start" is one of the sources, that is the line to delete or the effect to remove.
            bool hasStart = sources.Any(static s => s.Kind == UnlockSourceKind.Start);
            context.Error(
                LintCodes.Evl005,
                file,
                hasStart ? "/start" : string.Empty,
                $"{noun} '{id}' has {sources.Count} unlock sources but needs exactly one: {string.Join("; ", sources.Select(static s => s.Describe()))}");
        }
    }
}
