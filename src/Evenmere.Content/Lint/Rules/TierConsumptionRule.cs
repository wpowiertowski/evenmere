using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL011: higher tiers need more. For every tier with an <c>advanceFrom</c> tier, the sum of its
/// <c>basicNeeds.goods</c> amounts (units per 1,000 residents per day) must be strictly greater than that sum for its
/// <c>advanceFrom</c> tier (progression.md, How needs work).
/// </summary>
internal static class TierConsumptionRule
{
    /// <summary>Runs the higher-tiers-need-more check.</summary>
    /// <param name="context">The lint context.</param>
    public static void Run(LintContext context)
    {
        ContentDb db = context.Db;

        for (ushort i = 0; i < db.Tiers.Count; i++)
        {
            TierDef tier = db.Tiers.Items[i];
            if (tier.AdvanceFrom is not { } lowerId)
            {
                continue;
            }

            if (lowerId.Value >= db.Tiers.Count)
            {
                continue;
            }

            TierDef lower = db.Get(lowerId);
            string id = db.Tiers.Names[i];
            string lowerName = db.Tiers.Names[lowerId.Value];
            string file = context.FileOf(ContentType.Tiers, id);

            long total = Sum(tier);
            long lowerTotal = Sum(lower);
            if (total > lowerTotal)
            {
                continue;
            }

            context.Error(
                LintCodes.Evl011,
                file,
                LintContext.Pointer("basicNeeds", "goods"),
                $"tier '{id}' needs {Format(total)} basic goods per 1,000 residents a day, but its advanceFrom tier '{lowerName}' needs {Format(lowerTotal)}; a higher tier must need strictly more than the tier it advances from");
        }
    }

    private static long Sum(TierDef tier)
    {
        long total = 0;
        foreach (GoodAmount need in tier.BasicGoods)
        {
            total += need.Amount.Milli;
        }

        return total;
    }

    private static string Format(long milli) => global::Evenmere.Core.Numerics.Amount.FromMilli(milli).ToString();
}
