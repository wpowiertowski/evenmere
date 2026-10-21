using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Production;

/// <summary>Implements <see cref="IProductionQueries"/> over the production state. Every answer is a copy.</summary>
internal sealed class ProductionQueries(ProductionState state, ContentDb content) : IProductionQueries
{
    /// <inheritdoc />
    /// <remarks>
    /// The buffers list every good of the recipe in good-ID order, including those at zero, so a view can show "0 of 1 grain"
    /// for a producer that is missing an input.
    /// </remarks>
    public bool TryGet(EntityId building, out ProductionInfo info)
    {
        if (!state.Producers.TryGet(building, out ProducerRecord? record))
        {
            // The caller must not read it when this returns false; the interface has no nullability attribute to say so.
            info = null!;
            return false;
        }

        RecipeDef recipe = content.Get(record.Recipe);
        long target = ProductionMath.CycleTarget(recipe);
        int progress = (int)Math.Min(ProductionMath.One, record.Progress * ProductionMath.One / target);

        info = new ProductionInfo(
            building,
            record.Recipe,
            record.Stall,
            record.Productivity,
            record.WorkforceFill,
            record.InputsReady,
            record.AreaYield,
            record.BonusPercent,
            Ratio.FromPerMille(progress),
            Copy(recipe.Inputs, record.Inputs),
            Copy(recipe.Outputs, record.Outputs));
        return true;
    }

    /// <inheritdoc />
    public Amount ProducedYesterday(GoodId good)
    {
        Amount[] totals = state.Totals.Yesterday;
        return good.Value < totals.Length ? totals[good.Value] : Amount.Zero;
    }

    private static ImmutableArray<GoodAmount> Copy(ImmutableArray<GoodAmount> goods, Amount[] amounts)
    {
        ImmutableArray<GoodAmount>.Builder builder = ImmutableArray.CreateBuilder<GoodAmount>(goods.Length);
        for (int i = 0; i < goods.Length; i++)
        {
            builder.Add(new GoodAmount(goods[i].Good, amounts[i]));
        }

        return builder.MoveToImmutable();
    }
}
