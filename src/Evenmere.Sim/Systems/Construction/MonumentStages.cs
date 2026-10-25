using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>
/// The integer maths of multi-stage monuments (M2-10): how many stages a monument has, how the build time splits
/// across them, and how the <c>materialCost</c> modifier scales each stage's bill. Everything is in whole numbers
/// (ADR-0003) and shared by the system, the demolish handler and the queries, so a refund always matches what the
/// site drew and the inspector always shows what the site draws.
/// </summary>
internal static class MonumentStages
{
    /// <summary>
    /// Gets how many stages a new building is built in: the monument's stage count for a <c>Build</c> site whose
    /// type a <see cref="MonumentDef"/> names, or 0 for an ordinary building. In-place upgrades always report 0:
    /// an upgrade draws its own bill, never stages.
    /// </summary>
    /// <param name="content">The immutable content.</param>
    /// <param name="kind">Whether the site is a new building or an upgrade.</param>
    /// <param name="target">The type being built.</param>
    /// <returns>The stage count, or 0 when the site is not a monument under construction.</returns>
    public static int StageCount(ContentDb content, SiteKind kind, BuildingTypeId target)
    {
        ArgumentNullException.ThrowIfNull(content);
        return kind == SiteKind.Build && content.TryGetMonumentOf(target, out MonumentId monument)
            ? content.Get(monument).Stages.Length
            : 0;
    }

    /// <summary>
    /// Gets the work one monument stage needs: the building's total work split across the stages, with the rounding
    /// remainder on the last stage, so the stages add up to the total exactly (the sum over stages 0 to
    /// <c>stageIndex - 1</c> is <c>totalWork × stageIndex / stageCount</c>).
    /// </summary>
    /// <param name="totalWork">The building's total work, in milli-ticks.</param>
    /// <param name="stageIndex">The 0-based stage.</param>
    /// <param name="stageCount">How many stages the monument has.</param>
    /// <returns>The stage's work, in milli-ticks.</returns>
    public static long StageWorkTotal(long totalWork, int stageIndex, int stageCount) =>
        (totalWork * (stageIndex + 1) / stageCount) - (totalWork * stageIndex / stageCount);

    /// <summary>
    /// Scales one stage total by the <c>materialCost</c> bonus (for example <c>benefactors</c>): the same
    /// <see cref="Ratio.FromBonusPercent"/> multiplier the rest of the simulation reads, never below zero.
    /// </summary>
    /// <param name="stageTotal">The good's amount in the stage's bill.</param>
    /// <param name="materialCostBonusPercent">The summed <c>materialCost</c> modifiers, in percent.</param>
    /// <returns>The scaled amount.</returns>
    public static Amount Scale(Amount stageTotal, int materialCostBonusPercent) =>
        stageTotal.MultiplyBy(Ratio.FromBonusPercent(materialCostBonusPercent));

    /// <summary>
    /// Gets the amount of one good that a monument stage step draws: the scaled stage total split the way
    /// <see cref="SiteMath.StepAmount"/> splits a bill, so the steps add up to the scaled stage exactly.
    /// </summary>
    /// <param name="stageTotal">The good's amount in the stage's bill, before scaling.</param>
    /// <param name="step">The step, 1 to <see cref="SiteMath.Steps"/>.</param>
    /// <param name="materialCostBonusPercent">The summed <c>materialCost</c> modifiers, in percent.</param>
    /// <returns>The step's share of the scaled stage.</returns>
    public static Amount StepAmount(Amount stageTotal, int step, int materialCostBonusPercent) =>
        SiteMath.StepAmount(Scale(stageTotal, materialCostBonusPercent), step);

    /// <summary>
    /// Gets how much of one good a monument stage has drawn after a number of steps: the sum of
    /// <see cref="StepAmount(Amount, int, int)"/> over those steps.
    /// </summary>
    /// <param name="stageTotal">The good's amount in the stage's bill, before scaling.</param>
    /// <param name="stepsDrawn">The steps taken, 0 to <see cref="SiteMath.Steps"/>.</param>
    /// <param name="materialCostBonusPercent">The summed <c>materialCost</c> modifiers, in percent.</param>
    /// <returns>The drawn amount of the scaled stage.</returns>
    public static Amount DrawnAfter(Amount stageTotal, int stepsDrawn, int materialCostBonusPercent) =>
        SiteMath.DrawnAfter(Scale(stageTotal, materialCostBonusPercent), stepsDrawn);

    /// <summary>
    /// Gets the refund for demolishing a finished monument: half of the scaled final stage, rounded down to the
    /// milli-unit, consistent with <see cref="SiteMath.DemolishRefund"/> for an ordinary finished building.
    /// </summary>
    /// <param name="finalStageTotal">The good's amount in the final stage's bill, before scaling.</param>
    /// <param name="materialCostBonusPercent">The summed <c>materialCost</c> modifiers, in percent.</param>
    /// <returns>Half of the scaled stage.</returns>
    public static Amount FinalStageRefund(Amount finalStageTotal, int materialCostBonusPercent) =>
        SiteMath.DemolishRefund(Scale(finalStageTotal, materialCostBonusPercent));
}
