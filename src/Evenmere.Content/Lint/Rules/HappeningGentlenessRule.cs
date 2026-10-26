using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL012 (the gentleness check): no happening option may be net-negative. Grant amounts load as positive-only, so a
/// cost is a penalty modifier (a negative percent); an option that carries one must also carry a benefit — a grant, a
/// bonus modifier or a festival discount. Unlock and reveal effects count as benefits (they give something), and an
/// empty option (usually the polite decline) always passes.
/// </summary>
public static class HappeningGentlenessRule
{
    /// <summary>
    /// Runs the gentleness check.
    /// </summary>
    /// <param name="context">The lint context.</param>
    internal static void Run(LintContext context)
    {
        ContentDb db = context.Db;
        for (int h = 0; h < db.Happenings.Count; h++)
        {
            HappeningDef happening = db.Happenings.Items[h];
            string name = db.Happenings.Names[h];
            string file = context.FileOf(ContentType.Happenings, name);
            for (int i = 0; i < happening.Options.Length; i++)
            {
                HappeningOption option = happening.Options[i];
                if (HasCost(option) && !HasBenefit(option))
                {
                    context.Error(
                        LintCodes.Evl012,
                        file,
                        LintContext.Pointer("options", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "effects"),
                        $"option {i} of happening '{name}' is harsher than a setback with nothing in return: " +
                        $"a penalty must come with a grant, a bonus modifier or a festival discount");
                }
            }
        }
    }

    private static bool HasCost(HappeningOption option)
    {
        foreach (Effect effect in option.Effects)
        {
            switch (effect)
            {
                case ModifierEffect modifier when modifier.Percent < 0:
                    return true;
                case GrantGoodsEffect grant:
                    foreach (GoodAmount goods in grant.Goods)
                    {
                        if (goods.Amount < Amount.Zero)
                        {
                            return true;
                        }
                    }

                    break;
            }
        }

        return false;
    }

    private static bool HasBenefit(HappeningOption option)
    {
        foreach (Effect effect in option.Effects)
        {
            switch (effect)
            {
                case GrantGoodsEffect grant:
                    foreach (GoodAmount goods in grant.Goods)
                    {
                        if (goods.Amount > Amount.Zero)
                        {
                            return true;
                        }
                    }

                    break;
                case GrantPointsEffect points:
                    foreach (PointAmount point in points.Points)
                    {
                        if (point.Amount > Amount.Zero)
                        {
                            return true;
                        }
                    }

                    break;
                case ModifierEffect modifier when modifier.Percent > 0:
                    return true;
                case RequestFestivalEffect:
                    return true;
                case UnlockBuildingEffect:
                case UnlockRecipeEffect:
                case UnlockTierEffect:
                case UnlockTraditionEffect:
                case UnlockFestivalEffect:
                case UnlockTerrainAccessEffect:
                case UnlockFeatureEffect:
                case TraditionSlotEffect:
                case RevealCoastlineEffect:
                    return true;
            }
        }

        return false;
    }
}
