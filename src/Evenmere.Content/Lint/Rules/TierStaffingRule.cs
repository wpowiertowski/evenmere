using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL010 (ADR-0010, progression.md "Design rule" as revised by M2-30): for every tier's basic good and service, at
/// least one production chain or provider staffed entirely by lower tiers stays available, so a struggling town can
/// always fall back on it.
/// <list type="bullet">
/// <item><description>Upgrades may be staffed by any tier, including the tier whose basic need they serve: a
/// Villager-staffed rotation farm feeding Villagers' bread is allowed, as long as a lower-tier fallback stays.</description></item>
/// <item><description>For each tier with an <c>advanceFrom</c> tier (the first tier is exempt: nothing is lower than it),
/// every basic good needs a fallback chain: a producer, and transitively every recipe input, made by buildings that
/// need no workers or need a lower tier, and that are available (their own <c>start</c> or an unlock source).</description></item>
/// <item><description>Every basic service needs a fallback provider with the same staffing and availability.</description></item>
/// <item><description>Only basic goods and services are checked. Comfort goods, such as the villagers' cider, are exempt.</description></item>
/// <item><description>Crew reductions (ADR-0015) change the headcount, never the tier, so they cannot break the rule; the
/// reported worker count is the crew-reduced effective crew, computed with the same rounding the sim uses.</description></item>
/// <item><description>A building with no workforce needs nobody and passes; a dangling workforce tier is left to EVL002.
/// A good with no producer at all is left to EVL004.</description></item>
/// </list>
/// </summary>
public static class TierStaffingRule
{
    private const string CrewStatName = "crew";
    private const string ScopeNote = "EVL010 needs one fallback per basic good and service: a chain or provider staffed entirely by lower tiers that stays available (start or unlocked). Upgrades may be staffed by any tier, and comfort goods are exempt.";

    /// <summary>
    /// Runs the lower-tier fallback check.
    /// </summary>
    /// <param name="context">The lint context.</param>
    internal static void Run(LintContext context)
    {
        ContentDb db = context.Db;
        var unlocks = UnlockIndex.Build(context);
        bool[] available = new bool[db.Buildings.Count];
        for (ushort b = 0; b < db.Buildings.Count; b++)
        {
            available[b] = unlocks.Of(BuildingTypeId.From(b)).Count > 0;
        }

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

            foreach (GoodAmount need in tier.BasicGoods)
            {
                CheckGoodFallback(db, context, available, crews, tier, tierName, tierFile, need.Good);
            }

            foreach (ServiceKind service in tier.BasicServices)
            {
                CheckServiceFallback(db, context, available, crews, tier, tierName, tierFile, service);
            }
        }
    }

    /// <summary>
    /// The workers a building with content <paramref name="count"/> needs after its summed <c>crew</c> modifier
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

    private static void CheckGoodFallback(
        ContentDb db,
        LintContext context,
        bool[] available,
        int[] crews,
        TierDef tier,
        string tierName,
        string tierFile,
        GoodId need)
    {
        string needName = db.Goods.NameOf(need);
        if (!HasProducer(db, need))
        {
            // No producer at all: EVL004 reports that, and there is no staffing to judge.
            return;
        }

        int goodCount = db.Goods.Count;
        bool[] done = new bool[goodCount];
        bool[] ok = new bool[goodCount];
        bool[] busy = new bool[goodCount];
        if (HasFallbackGood(db, available, tier, need, done, ok, busy))
        {
            return;
        }

        var hops = new List<string>();
        bool[] visited = new bool[goodCount];
        CollectHops(db, available, crews, tier, need, hops, visited);
        context.Error(
            LintCodes.Evl010,
            tierFile,
            LintContext.Pointer("basicNeeds", "goods", needName),
            $"tier '{tierName}' has no lower-tier fallback for basic good '{needName}': every chain making it is staffed at or above '{tierName}', or is not available. Production chain:\n{string.Join("\n", hops)}\n{ScopeNote}");
    }

    private static bool HasProducer(ContentDb db, GoodId good)
    {
        for (ushort b = 0; b < db.Buildings.Count; b++)
        {
            BuildingDef building = db.Buildings.Items[b];
            if (building.Recipe is { } recipeId && Makes(db.Get(recipeId), good))
            {
                return true;
            }
        }

        return false;
    }

    // Whether some chain makes the good with lower-tier (or workerless) staffing all the way down, through available
    // buildings only. Memoised per good; a recipe cycle answers false for the goods inside it.
    private static bool HasFallbackGood(
        ContentDb db,
        bool[] available,
        TierDef tier,
        GoodId good,
        bool[] done,
        bool[] ok,
        bool[] busy)
    {
        if (done[good.Value])
        {
            return ok[good.Value];
        }

        if (busy[good.Value])
        {
            return false;
        }

        busy[good.Value] = true;
        bool found = false;
        for (ushort b = 0; b < db.Buildings.Count; b++)
        {
            BuildingDef building = db.Buildings.Items[b];
            if (building.Recipe is not { } recipeId || !Makes(db.Get(recipeId), good))
            {
                continue;
            }

            if (!available[b] || !IsLowerOrNone(db, tier, building))
            {
                continue;
            }

            bool inputsOk = true;
            foreach (GoodAmount input in db.Get(recipeId).Inputs)
            {
                if (!HasFallbackGood(db, available, tier, input.Good, done, ok, busy))
                {
                    inputsOk = false;
                    break;
                }
            }

            if (inputsOk)
            {
                found = true;
                break;
            }
        }

        busy[good.Value] = false;
        done[good.Value] = true;
        ok[good.Value] = found;
        return found;
    }

    // Every reachable hop for the diagnostic, in building ID order: "good → recipe → building", annotated with the
    // staffing and availability that disqualify it. Mirrors the old chain naming, one line per hop.
    private static void CollectHops(
        ContentDb db,
        bool[] available,
        int[] crews,
        TierDef tier,
        GoodId good,
        List<string> hops,
        bool[] visited)
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

            hops.Add($"{db.Goods.NameOf(good)} → {db.Recipes.NameOf(recipeId)} → {db.Buildings.Names[b]} ({StaffText(db, b, crews[b])}, {(available[b] ? AvailabilityText(db, b) : "unavailable")})");
            foreach (GoodAmount input in db.Get(recipeId).Inputs)
            {
                CollectHops(db, available, crews, tier, input.Good, hops, visited);
            }
        }
    }

    private static void CheckServiceFallback(
        ContentDb db,
        LintContext context,
        bool[] available,
        int[] crews,
        TierDef tier,
        string tierName,
        string tierFile,
        ServiceKind service)
    {
        var providers = new List<string>();
        bool fallback = false;
        for (ushort b = 0; b < db.Buildings.Count; b++)
        {
            BuildingDef building = db.Buildings.Items[b];
            if (building.Service is not { } provided || provided.Kind != service)
            {
                continue;
            }

            providers.Add($"'{db.Buildings.Names[b]}' providing it is {ProviderStaffText(db, b, crews[b])} ({(available[b] ? AvailabilityText(db, b) : "unavailable")})");
            if (available[b] && IsLowerOrNone(db, tier, building))
            {
                fallback = true;
            }
        }

        if (fallback)
        {
            return;
        }

        context.Error(
            LintCodes.Evl010,
            tierFile,
            ServicePointer(context, tierName, service),
            $"tier '{tierName}' has no lower-tier fallback for the '{ContentNames.NameOf(service)}' service as a basic need: every provider is staffed at or above '{tierName}', or is not available. Providers:\n{string.Join("\n", providers)}\n{ScopeNote}");
    }

    // A building keeps a fallback chain alive when it needs nobody, names a dangling tier (EVL002's problem), or needs
    // a lower tier. Crew reductions change the headcount, never the tier, so they cannot break this.
    private static bool IsLowerOrNone(ContentDb db, TierDef tier, BuildingDef building)
    {
        return building.Workforce is not { } workforce
            || workforce.Tier.Value >= db.Tiers.Count
            || db.Get(workforce.Tier).Order < tier.Order;
    }

    private static string AvailabilityText(ContentDb db, ushort building)
    {
        return db.Buildings.Items[building].Start ? "start" : "unlocked";
    }

    private static string StaffText(ContentDb db, ushort building, int crew)
    {
        BuildingDef def = db.Buildings.Items[building];
        if (def.Workforce is not { } workforce)
        {
            return "no workforce";
        }

        string worker = workforce.Tier.Value < db.Tiers.Count ? db.Tiers.NameOf(workforce.Tier) : "unknown tier";
        return $"staffed by '{worker}' ({CrewText(crew)})";
    }

    private static string ProviderStaffText(ContentDb db, ushort building, int crew)
    {
        BuildingDef def = db.Buildings.Items[building];
        if (def.Workforce is not { } workforce)
        {
            return "needs no workers";
        }

        string worker = workforce.Tier.Value < db.Tiers.Count ? db.Tiers.NameOf(workforce.Tier) : "unknown tier";
        return $"staffed by '{worker}' ({CrewText(crew)})";
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

    private static string CrewText(int crew) => crew == 1 ? "1 worker" : $"{crew} workers";

    /// <summary>
    /// Points at a service inside the tier's <c>basicNeeds.services</c> array, the same way EVL006 does: the definition
    /// sorts the services, so the position is looked up in the file as written.
    /// </summary>
    private static string ServicePointer(LintContext context, string tierId, ServiceKind service)
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
                if (item.ValueKind == JsonValueKind.String && item.GetString() == ContentNames.NameOf(service))
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
    /// ordinary day. The stat is matched by its content name, so M2-30's <c>Stat.Crew</c> flows in with no change here.
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
