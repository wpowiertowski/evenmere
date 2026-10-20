using System.Globalization;
using System.Text.Json;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL006: a tier's basic needs can be met with what its era has unlocked.
/// <list type="bullet">
/// <item><description>A tier's era is its declared <c>era</c>. Every basic good must be producible by buildings unlocked in
/// that era or earlier, and so must the inputs of their recipes, down to gatherers; every basic service must be provided
/// by such a building. The set of producible goods is the fixed point of "a building's outputs are producible once its
/// inputs are".</description></item>
/// <item><description>The declared era may not be earlier than the era of the tier's unlock source (<c>start</c> is era 1;
/// with several sources, the earliest counts). It may be later: the gateway tech Town Charter is an era I node that unlocks
/// Townsfolk, an era II tier. There is no upper bound.</description></item>
/// </list>
/// A tier that nothing unlocks is left to EVL005.
/// </summary>
internal static class TierNeedsRule
{
    public static void Run(LintContext context, UnlockIndex index)
    {
        ContentDb db = context.Db;
        int[] buildingEras = new int[db.Buildings.Count];
        for (ushort i = 0; i < buildingEras.Length; i++)
        {
            buildingEras[i] = UnlockIndex.EraOf(index.Of(BuildingTypeId.From(i)));
        }

        // Era numbers that exist, ascending, for "first producible in era N" messages.
        int[] eras = [.. db.Eras.Items.Select(static e => e.Order).Order()];

        // Looked up by era only; never iterated.
        var producibleByEra = new Dictionary<int, bool[]>();
        bool[] ProducibleIn(int era)
        {
            if (!producibleByEra.TryGetValue(era, out bool[]? producible))
            {
                producible = ProducibleGoods(db, buildingEras, era);
                producibleByEra.Add(era, producible);
            }

            return producible;
        }

        for (ushort i = 0; i < db.Tiers.Count; i++)
        {
            IReadOnlyList<UnlockSource> sources = index.Of(TierId.From(i));
            if (sources.Count == 0)
            {
                // EVL005 reports a tier nothing unlocks; its needs are moot.
                continue;
            }

            TierDef tier = db.Tiers.Items[i];
            int era = tier.Era;
            string id = db.Tiers.Names[i];
            string file = context.FileOf(ContentType.Tiers, id);

            int sourceEra = UnlockIndex.EraOf(sources);
            if (era < sourceEra)
            {
                UnlockSource earliest = sources.First(s => s.Era == sourceEra);
                context.Error(
                    LintCodes.Evl006,
                    file,
                    LintContext.Pointer("era"),
                    $"tier '{id}' declares era {era}, but it is unlocked in era {sourceEra} by {earliest.Describe()}; a tier cannot belong to an era before the one that unlocks it");
            }

            foreach (GoodAmount need in tier.BasicGoods)
            {
                if (ProducibleIn(era)[need.Good.Value])
                {
                    continue;
                }

                string good = db.Goods.NameOf(need.Good);
                context.Error(
                    LintCodes.Evl006,
                    file,
                    LintContext.Pointer("basicNeeds", "goods", good),
                    $"tier '{id}' (era {era}) needs '{good}', but {WhenProducible(need.Good.Value, era, eras, ProducibleIn)}");
            }

            foreach (ServiceKind service in tier.BasicServices)
            {
                if (ServiceProvidedBy(db, buildingEras, service, era))
                {
                    continue;
                }

                string name = ContentNames.NameOf(service);
                context.Error(
                    LintCodes.Evl006,
                    file,
                    ServicePointer(context, id, name),
                    $"tier '{id}' (era {era}) needs the '{name}' service, but {WhenProvided(db, buildingEras, service, era, eras)}");
            }
        }
    }

    private static bool[] ProducibleGoods(ContentDb db, int[] buildingEras, int era)
    {
        bool[] producible = new bool[db.Goods.Count];
        bool changed;
        do
        {
            changed = false;
            for (int b = 0; b < db.Buildings.Count; b++)
            {
                if (buildingEras[b] > era || db.Buildings.Items[b].Recipe is not { } recipeId)
                {
                    continue;
                }

                RecipeDef recipe = db.Get(recipeId);
                if (!recipe.Inputs.All(input => producible[input.Good.Value]))
                {
                    continue;
                }

                foreach (GoodAmount output in recipe.Outputs)
                {
                    if (!producible[output.Good.Value])
                    {
                        producible[output.Good.Value] = true;
                        changed = true;
                    }
                }
            }
        }
        while (changed);

        return producible;
    }

    private static bool ServiceProvidedBy(ContentDb db, int[] buildingEras, ServiceKind service, int era)
    {
        for (int b = 0; b < db.Buildings.Count; b++)
        {
            if (buildingEras[b] <= era && db.Buildings.Items[b].Service is { } provided && provided.Kind == service)
            {
                return true;
            }
        }

        return false;
    }

    private static string WhenProducible(int good, int tierEra, int[] eras, Func<int, bool[]> producibleIn)
    {
        foreach (int era in eras)
        {
            if (era > tierEra && producibleIn(era)[good])
            {
                return $"nothing unlocked by era {tierEra} produces it (a chain that makes it is complete only in era {era})";
            }
        }

        return $"nothing unlocked by era {tierEra} produces it, and no chain of buildings makes it at all";
    }

    private static string WhenProvided(ContentDb db, int[] buildingEras, ServiceKind service, int tierEra, int[] eras)
    {
        foreach (int era in eras)
        {
            if (era > tierEra && ServiceProvidedBy(db, buildingEras, service, era))
            {
                return $"no building unlocked by era {tierEra} provides it (one is unlocked in era {era})";
            }
        }

        return $"no building unlocked by era {tierEra} provides it, and no building provides it at all";
    }

    /// <summary>
    /// Points at a service inside the tier's <c>basicNeeds.services</c> array. The definition sorts the services, so the
    /// position is looked up in the file as written; if it cannot be found the pointer names the whole array.
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
}
