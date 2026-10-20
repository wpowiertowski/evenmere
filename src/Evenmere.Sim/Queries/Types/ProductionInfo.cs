using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// A copy of one producer's state: the factors of its current productivity, its progress through the cycle and its
/// buffers. Productivity is <c>WorkforceFill × (InputsReady ? 1 : 0) × AreaYield × (1000 + BonusPercent × 10) / 1000</c>
/// (architecture.md, Production chains), so the explain queries can show each factor.
/// </summary>
/// <param name="Building">The producer's entity ID.</param>
/// <param name="Recipe">The recipe it runs.</param>
/// <param name="Stall">Why it is not producing; <see cref="StallReason.None"/> when it is.</param>
/// <param name="Productivity">The productivity applied on the last tick, in per-mille; 1000 is a full cycle speed.</param>
/// <param name="WorkforceFill">The share of the building's workforce that is staffed, in per-mille.</param>
/// <param name="InputsReady">Whether the input buffer holds the goods the next cycle needs.</param>
/// <param name="AreaYield">The yield the surroundings allow (forest, water, fertility, deposit), in per-mille, capped at 1000; 1000 for buildings without an area yield.</param>
/// <param name="BonusPercent">The summed <c>productivity</c> modifiers that applied, in percent (+25 means ×1.25).</param>
/// <param name="CycleProgress">The progress through the current cycle, in per-mille.</param>
/// <param name="InputBuffer">The goods waiting in the input buffer, sorted by good ID; empty for gatherers.</param>
/// <param name="OutputBuffer">The goods waiting for a cart in the output buffer, sorted by good ID.</param>
public sealed record ProductionInfo(
    EntityId Building,
    RecipeId Recipe,
    StallReason Stall,
    Ratio Productivity,
    Ratio WorkforceFill,
    bool InputsReady,
    Ratio AreaYield,
    int BonusPercent,
    Ratio CycleProgress,
    ImmutableArray<GoodAmount> InputBuffer,
    ImmutableArray<GoodAmount> OutputBuffer)
{
    /// <inheritdoc />
    public bool Equals(ProductionInfo? other) =>
        other is not null
        && Building == other.Building
        && Recipe == other.Recipe
        && Stall == other.Stall
        && Productivity == other.Productivity
        && WorkforceFill == other.WorkforceFill
        && InputsReady == other.InputsReady
        && AreaYield == other.AreaYield
        && BonusPercent == other.BonusPercent
        && CycleProgress == other.CycleProgress
        && ValueArrays.Equal(InputBuffer, other.InputBuffer)
        && ValueArrays.Equal(OutputBuffer, other.OutputBuffer);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Building, Recipe, Stall, Productivity, WorkforceFill, ValueArrays.Hash(InputBuffer), ValueArrays.Hash(OutputBuffer));
}

/// <summary>A copy of one construction site's progress, for the inspector, the explain queries and the site visuals.</summary>
/// <param name="Building">The site's entity ID.</param>
/// <param name="Stage">How many build steps are complete.</param>
/// <param name="Steps">How many build steps the site has in all (10 in M1).</param>
/// <param name="Progress">The share of the build time that has elapsed, in per-mille.</param>
/// <param name="Stall">Why the site is waiting: <see cref="StallReason.NoStorehouseReach"/> or <see cref="StallReason.MissingMaterial"/>; <see cref="StallReason.None"/> when it is advancing.</param>
/// <param name="WaitingFor">For <see cref="StallReason.MissingMaterial"/>, the first good the next step lacks; otherwise <see langword="null"/>.</param>
public readonly record struct SiteInfo(
    EntityId Building,
    int Stage,
    int Steps,
    Ratio Progress,
    StallReason Stall,
    GoodId? WaitingFor);
