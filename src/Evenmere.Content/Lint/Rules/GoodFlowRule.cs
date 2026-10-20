using System.Collections.Immutable;
using Evenmere.Content.Definitions;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL004: every good is made by some recipe and used by something. A producer is a recipe output. A consumer is a recipe
/// input, a tier's basic or comfort need, a building's cost or fuel, a tier's upgrade cost, a monument stage cost or a
/// festival cost. "No producer" and "no consumer" are reported separately, both at the good's file.
/// </summary>
internal static class GoodFlowRule
{
    public static void Run(LintContext context)
    {
        ContentDb db = context.Db;
        bool[] produced = new bool[db.Goods.Count];
        bool[] consumed = new bool[db.Goods.Count];

        foreach (RecipeDef recipe in db.Recipes.Items)
        {
            Mark(produced, recipe.Outputs);
            Mark(consumed, recipe.Inputs);
        }

        foreach (TierDef tier in db.Tiers.Items)
        {
            Mark(consumed, tier.BasicGoods);
            Mark(consumed, tier.ComfortGoods);
            Mark(consumed, tier.UpgradeCost.Goods);
        }

        foreach (BuildingDef building in db.Buildings.Items)
        {
            Mark(consumed, building.Cost.Goods);
            if (building.Fuel is { } fuel)
            {
                consumed[fuel.Good.Value] = true;
            }
        }

        foreach (MonumentDef monument in db.Monuments.Items)
        {
            foreach (CostSpec stage in monument.Stages)
            {
                Mark(consumed, stage.Goods);
            }
        }

        foreach (FestivalDef festival in db.Festivals.Items)
        {
            Mark(consumed, festival.Cost.Goods);
        }

        for (int i = 0; i < db.Goods.Count; i++)
        {
            string id = db.Goods.Names[i];
            string file = context.FileOf(ContentType.Goods, id);
            if (!produced[i])
            {
                context.Error(
                    LintCodes.Evl004,
                    file,
                    string.Empty,
                    $"good '{id}' has no producer: no recipe lists it in its outputs");
            }

            if (!consumed[i])
            {
                context.Error(
                    LintCodes.Evl004,
                    file,
                    string.Empty,
                    $"good '{id}' has no consumer: no recipe input, tier need, building cost or fuel, upgrade cost, monument stage or festival cost uses it");
            }
        }
    }

    private static void Mark(bool[] flags, ImmutableArray<GoodAmount> amounts)
    {
        foreach (GoodAmount amount in amounts)
        {
            flags[amount.Good.Value] = true;
        }
    }
}
