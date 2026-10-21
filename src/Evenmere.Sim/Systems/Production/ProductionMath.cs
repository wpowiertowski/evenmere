using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Systems.Production;

/// <summary>The integer arithmetic of a production cycle, shared by the tick, the buffers and the queries so they cannot disagree.</summary>
internal static class ProductionMath
{
    /// <summary>The number of simulation ticks in one second of recipe time (a tick is 0.25 s, ADR-0003).</summary>
    public const int TicksPerSecond = 4;

    /// <summary>The per-mille value of a factor of exactly one.</summary>
    public const int One = 1000;

    /// <summary>
    /// The season's factor on productivity, in per-mille. It is 1000 until seasons arrive.
    /// TODO(M2-05): seasonal yield, read from <c>RulesDef.Seasons</c> for buildings that carry a season curve's tag.
    /// </summary>
    public const int SeasonFactor = 1000;

    /// <summary>Gets the progress at which a recipe's cycle completes: <c>cycleSeconds × 4 ticks × 1000 per-mille</c>.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>The progress target.</returns>
    public static long CycleTarget(RecipeDef recipe) => (long)recipe.CycleSeconds * TicksPerSecond * One;

    /// <summary>
    /// Computes one tick's productivity in per-mille: the four per-mille factors fill, area yield, <c>1000 + 10 × bonus%</c>
    /// and season multiplied together and divided by 1000³, rounded down once at the end. 1000 is a full cycle speed;
    /// bonuses can take it above that. The input factor (present or not) is applied by the caller, because a building
    /// without its inputs is stalled rather than slow.
    /// </summary>
    /// <param name="fill">The workforce fill.</param>
    /// <param name="areaYield">The area yield.</param>
    /// <param name="bonusPercent">The summed productivity bonuses in percent; a penalty of 100 or more gives zero.</param>
    /// <returns>The productivity, zero or more.</returns>
    public static long Productivity(Ratio fill, Ratio areaYield, int bonusPercent)
    {
        long bonus = Ratio.FromBonusPercent(bonusPercent).PerMille;
        long product = (long)Math.Max(0, fill.PerMille) * Math.Max(0, areaYield.PerMille) * bonus * SeasonFactor;
        return product / ((long)One * One * One);
    }

    /// <summary>Tests whether the input buffer holds the goods the next cycle consumes.</summary>
    /// <param name="record">The producer.</param>
    /// <param name="recipe">Its recipe.</param>
    /// <returns><see langword="true"/> if every input is there (always true for a gatherer).</returns>
    public static bool HasInputs(ProducerRecord record, RecipeDef recipe)
    {
        ImmutableArray<GoodAmount> needs = recipe.Inputs;
        for (int i = 0; i < needs.Length; i++)
        {
            if (record.Inputs[i] < needs[i].Amount)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests whether the output buffer has room for one more cycle's outputs. The buffer holds at most
    /// <c>max(outputBuffer, one cycle's output)</c> of each good, so a recipe that yields more than the buffer size in one
    /// cycle can still run.
    /// </summary>
    /// <param name="record">The producer.</param>
    /// <param name="recipe">Its recipe.</param>
    /// <param name="outputBuffer">The most a buffer holds per good (<c>RulesDef.OutputBuffer</c>).</param>
    /// <returns><see langword="true"/> if a cycle can complete without losing goods.</returns>
    public static bool HasOutputRoom(ProducerRecord record, RecipeDef recipe, Amount outputBuffer)
    {
        ImmutableArray<GoodAmount> yields = recipe.Outputs;
        for (int i = 0; i < yields.Length; i++)
        {
            if (record.Outputs[i] + yields[i].Amount > Amount.Max(outputBuffer, yields[i].Amount))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the level below which an input is requested: one cart load, or the amount one cycle consumes if that is larger,
    /// so a recipe that needs more than a cart carries is not left waiting for a request that never opens.
    /// </summary>
    /// <param name="perCycle">What one cycle consumes of the good.</param>
    /// <param name="cartLoad">The amount one cart carries (<c>RulesDef.CartLoad</c>).</param>
    /// <returns>The request threshold.</returns>
    public static Amount DeliveryThreshold(Amount perCycle, Amount cartLoad) => Amount.Max(perCycle, cartLoad);
}
