using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL010 (ADR-0010, progression.md "Design rule"): a tier's basic goods and services are made and staffed by lower
/// tiers, so a tier's workers are never required to make its own basic needs, which would deadlock a fresh tier.
/// <list type="bullet">
/// <item><description>For each tier with an <c>advanceFrom</c> tier (the first tier is exempt: nothing is lower than it),
/// every building that produces one of its basic goods, transitively through recipe inputs, and every building that
/// provides one of its basic services must declare a workforce tier lower than it.</description></item>
/// <item><description>Only basic goods and services are checked. Comfort goods, such as the villagers' cider, may be made
/// and staffed by their own tier.</description></item>
/// <item><description>Crew reductions (ADR-0015) change the headcount, never the tier, so they cannot break the rule; the
/// reported worker count is the crew-reduced effective crew, computed with the same rounding the sim uses.</description></item>
/// <item><description>A building with no workforce needs nobody and passes; a dangling workforce tier is left to EVL002.</description></item>
/// </list>
/// </summary>
public static class TierStaffingRule
{
    private const string CrewStatName = "crew";
    private const string ScopeNote = "EVL010 covers a tier's basic goods and services only; comfort goods are exempt.";

    /// <summary>
    /// Runs the lower-tier staffing check.
    /// </summary>
    /// <param name="context">The lint context.</param>
    internal static void Run(LintContext context)
    {
        ContentDb db = context.Db;
        int[] crews = EffectiveCrews(db);

        for (ushort t = 0; t < db.Tiers.Count; t++)
        {
            TierDef tier = db.Tiers.Items[t];
            if (tier.AdvanceFrom is null)
            {
                // The first tier has nothing lower than it, so it is exempt.
                continue;
            }

            string tierName = db.Tiers.Names[t];
            string tierFile = context.FileOf(ContentType.Tiers, tierName);
            bool[] checkedBuildings = new bool[db.Buildings.Count];
            bool[] visitedGoods = new bool[db.Goods.Count];

            foreach (GoodAmount need in tier.BasicGoods)
            {
                string needName = db.Goods.NameOf(need.Good);
                var hops = new List<string>();
                ExpandGood(db, context, tier, tierName, tierFile, needName, need.Good, hops, visitedGoods, checkedBuildings, crews);
            }

            foreach (ServiceKind service in tier.BasicServices)
            {
                CheckService(db, context, tier, tierName, tierFile, service, crews);
            }
        }
    }

    /// <summary>
    /// The workers a building with content crew <paramref name="count"/> needs after its summed <c>crew</c> modifier
    /// percent <paramref name="crewPercent"/>, in ADR-0015 integer maths: half up, floored at 1. A large penalty that
    /// would wipe the crew out still leaves one worker. Public so tests pin the rounding the sim (M2-30) must match.
    /// </summary>
    /// <param name="count">The content workforce count, at least 1.</param>
    /// <param name="crewPercent">The summed <c>crew</c> modifier percent; negative reduces.</param>
    /// <returns>The effective crew.</returns>
    public static int EffectiveCrew(int count, int crewPercent)
    {
        long multiplier = 1000L + 10L * crewPercent;
        if (multiplier < 0)
        {
            multiplier = 0;
        }

        long scaled = ((long)count * multiplier + 500L) / 1000L;
        if (scaled < 1)
        {
            scaled = 1;
        }

        return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
    }

    private static void ExpandGood(
        ContentDb db,
        LintContext context,
        TierDef tier,
        string tierName,
        string tierFile,
        string needName,
        GoodId good,
        List<string> hops,
        bool[] visited,
        bool[] checkedBuildings,
        int[] crews)
    {
        if (visited[good.Value])
        {
            return;
        }

        visited[good.Value] = true;
        for (ushort b = 0; b < db.Buildings.Count; b++)
        {
            BuildingDef building = db.Buildings.Items[b];
            if (building.Recipe is not { } recipeId || !Makes(db.Get(recipeId), good))
            {
                continue;
            }

            hops.Add($"{db.Goods.NameOf(good)} → {db.Recipes.NameOf(recipeId)} → {db.Buildings.Names[b]}");
            if (!checkedBuildings[b])
            {
                checkedBuildings[b] = true;
                CheckGoodsStaffing(db, context, tier, tierName, tierFile, b, crews[b], hops, needName);
            }

            foreach (GoodAmount input in db.Get(recipeId).Inputs)
            {
                ExpandGood(db, context, tier, tierName, tierFile, needName, input.Good, hops, visited, checkedBuildings, crews);
            }

            hops.RemoveAt(hops.Count - 1);
        }
    }

    private static bool Makes(RecipeDef recipe, GoodId good)
    {
        foreach (GoodAmount output in recipe.Outputs)
        {
            if (output.Good == good)
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckService(
        ContentDb db,
        LintContext context,
        TierDef tier,
        string tierName,
        string tierFile,
        ServiceKind service,
        int[] crews)
    {
        for (ushort b = 0; b < db.Buildings.Count; b++)
        {
            if (db.Buildings.Items[b].Service is not { } provided || provided.Kind != service)
            {
                continue;
            }

            // A service provider is checked even when its goods chain already cleared it: it answers another need.
            CheckServiceStaffing(db, context, tier, tierName, tierFile, b, crews[b]);
        }
    }

    private static void CheckGoodsStaffing(
        ContentDb db,
        LintContext context,
        TierDef tier,
        string tierName,
        string tierFile,
        ushort building,
        int crew,
        List<string> hops,
        string needName)
    {
        if (db.Buildings.Items[building].Workforce is not { } workforce
            || workforce.Tier.Value >= db.Tiers.Count
            || db.Get(workforce.Tier).Order < tier.Order)
        {
            // No workforce needs nobody; a dangling tier is EVL002's; a lower tier is fine.
            return;
        }

        string buildingName = db.Buildings.Names[building];
        string workerName = db.Tiers.NameOf(workforce.Tier);
        context.Error(
            LintCodes.Evl010,
            tierFile,
            LintContext.Pointer("basicNeeds", "goods", needName),
            $"tier '{tierName}' needs '{needName}' as a basic good, but '{buildingName}' is staffed by '{workerName}' ({CrewText(crew)}), which is not lower than '{tierName}'. Production chain:\n{string.Join("\n", hops)}\n{ScopeNote}");
    }

    private static void CheckServiceStaffing(
        ContentDb db,
        LintContext context,
        TierDef tier,
        string tierName,
        string tierFile,
        ushort building,
        int crew)
    {
        BuildingDef def = db.Buildings.Items[building];
        if (def.Workforce is not { } workforce
            || def.Service is not { } provided
            || workforce.Tier.Value >= db.Tiers.Count
            || db.Get(workforce.Tier).Order < tier.Order)
        {
            // As above: no workforce, dangling tier or lower tier needs no report. The service is
            // always present here since the caller only passes providers, but the guard keeps it total.
            return;
        }

        string buildingName = db.Buildings.Names[building];
        string workerName = db.Tiers.NameOf(workforce.Tier);
        string service = ContentNames.NameOf(provided.Kind);
        context.Error(
            LintCodes.Evl010,
            tierFile,
            ServicePointer(context, tierName, service),
            $"tier '{tierName}' needs the '{service}' service as a basic need, but '{buildingName}' providing it is staffed by '{workerName}' ({CrewText(crew)}), which is not lower than '{tierName}'. {ScopeNote}");
    }

    private static string CrewText(int crew) => crew == 1 ? "1 worker" : $"{crew} workers";

    /// <summary>
    /// Points at a service inside the tier's <c>basicNeeds.services</c> array, the same way EVL006 does: the definition
    /// sorts the services, so the position is looked up in the file as written.
    /// </summary>
    private static string ServicePointer(LintContext context, string tierId, string service)
    {
        if (context.JsonOf(ContentType.Tiers, tierId) is { } json
            && json.TryGetProperty("basicNeeds", out JsonElement basic)
            && basic.ValueKind == JsonValueKind.Object
            && basic.TryGetProperty("services", out JsonElement array)
            && array.ValueKind == JsonValueKind.Array)
        {
            int position = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() == service)
                {
                    return LintContext.Pointer("basicNeeds", "services", position.ToString(CultureInfo.InvariantCulture));
                }

                position++;
            }
        }

        return LintContext.Pointer("basicNeeds", "services");
    }

    /// <summary>
    /// The effective crew of every building: its content count reduced by the summed percent of every unconditional
    /// <c>crew</c> modifier whose target selects it. Conditional modifiers are left out: the staffing must hold on an
    /// ordinary day. The stat is matched by its content name, so M2-30's <c>Stat.Crew</c> flows in with no change here;
    /// until then nothing matches and the count is the content count.
    /// </summary>
    private static int[] EffectiveCrews(ContentDb db)
    {
        int[] sums = new int[db.Buildings.Count];
        foreach (TechDef tech in db.Techs.Items)
        {
            AddCrewModifiers(db, sums, tech.Effects);
        }

        foreach (CivicDef civic in db.Civics.Items)
        {
            AddCrewModifiers(db, sums, civic.Effects);
        }

        foreach (TraditionDef tradition in db.Traditions.Items)
        {
            AddCrewModifiers(db, sums, tradition.Effects);
        }

        foreach (FestivalDef festival in db.Festivals.Items)
        {
            AddCrewModifiers(db, sums, festival.Effects);
        }

        foreach (MonumentDef monument in db.Monuments.Items)
        {
            AddCrewModifiers(db, sums, monument.Effects);
        }

        foreach (HappeningDef happening in db.Happenings.Items)
        {
            foreach (HappeningOption option in happening.Options)
            {
                AddCrewModifiers(db, sums, option.Effects);
            }
        }

        int[] crews = new int[db.Buildings.Count];
        for (int b = 0; b < crews.Length; b++)
        {
            crews[b] = db.Buildings.Items[b].Workforce is { } workforce ? EffectiveCrew(workforce.Count, sums[b]) : 0;
        }

        return crews;
    }

    private static void AddCrewModifiers(ContentDb db, int[] sums, ImmutableArray<Effect> effects)
    {
        foreach (Effect effect in effects)
        {
            if (effect is not ModifierEffect modifier
                || modifier.When is not null
                || ContentNames.NameOf(modifier.Stat) != CrewStatName)
            {
                continue;
            }

            switch (modifier.Target)
            {
                case IslandTarget:
                    for (int b = 0; b < sums.Length; b++)
                    {
                        sums[b] += modifier.Percent;
                    }

                    break;
                case BuildingsTarget picked:
                    for (int b = 0; b < sums.Length; b++)
                    {
                        if (Selects(picked.Selector, db.Buildings.Items[b], b))
                        {
                            sums[b] += modifier.Percent;
                        }
                    }

                    break;
                default:
                    // Tier, good and discovery targets never select buildings.
                    break;
            }
        }
    }

    private static bool Selects(BuildingSelector selector, BuildingDef def, int building)
    {
        switch (selector)
        {
            case BuildingTypeSelector byId:
                return byId.Building.Value == building;
            case BuildingTagSelector byTag:
                return def.Tags.Contains(byTag.Tag);
            case BuildingCategorySelector byCategory:
                return def.Category == byCategory.Category;
            default:
                return false;
        }
    }
}
