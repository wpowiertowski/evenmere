using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the tier workforce and its allocation to workplaces. Implemented by the workforce system
/// (M1-07), which allocates once a day. Workers are whole persons, not Amounts.
/// </summary>
public interface IWorkforceQueries
{
    /// <summary>Gets how many workers a tier supplies: the supply Population set, scaled by the <c>workforce</c> modifiers.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The number of workers; 0 if the tier has no residents.</returns>
    int Supply(TierId tier);

    /// <summary>Gets how many workers the active, unpaused buildings of a tier ask for in all.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The number of workers wanted.</returns>
    int Demand(TierId tier);

    /// <summary>Gets how much of a workplace's workforce is staffed.</summary>
    /// <param name="building">The workplace.</param>
    /// <returns>The fill in per-mille, where 1000 is fully staffed. A building that needs no workers reports 1000; a building that does not exist reports 0.</returns>
    Ratio FillOf(EntityId building);

    /// <summary>Gets how many workers the construction sites of a tier will ask for once they are finished (ADR-0016).</summary>
    /// <remarks>Only new buildings in the <see cref="Evenmere.Sim.Queries.Types.BuildingState.Site"/> state count, by the tier their type names. An in-place upgrade keeps working as its old type until it completes, so its old tier's <see cref="Demand"/> still covers it.</remarks>
    /// <param name="tier">The tier.</param>
    /// <returns>The planned workers wanted.</returns>
    int PlannedDemand(TierId tier);

    /// <summary>Gets how many workers of a tier the last daily allocation placed in lower-tier jobs (ADR-0016).</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The workers of the tier working below it; 0 when nobody steps down.</returns>
    int WorkingBelowTier(TierId tier);

    /// <summary>Gets how many jobs of a tier the last daily allocation filled with higher-tier workers (ADR-0016).</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The jobs of the tier filled from above; 0 when the tier staffs itself.</returns>
    int FilledFromAbove(TierId tier);

    /// <summary>Gets how many workers the last daily allocation assigned to a workplace, including substitutes from higher tiers (ADR-0016).</summary>
    /// <param name="building">The workplace.</param>
    /// <returns>The assigned workers, or 0 when the building has no record.</returns>
    int AllocatedWorkers(EntityId building);

    /// <summary>Gets how many of a workplace's assigned workers came from higher tiers (ADR-0016).</summary>
    /// <param name="building">The workplace.</param>
    /// <returns>The substitutes working here, or 0 when the tier staffs itself or the building has no record.</returns>
    int SubstitutedWorkers(EntityId building);

    /// <summary>Gets the nearest higher tier that placed workers at a workplace in the last daily allocation (ADR-0016).</summary>
    /// <remarks>Meaningful only when <see cref="SubstitutedWorkers"/> is positive; otherwise the default tier ID.</remarks>
    /// <param name="building">The workplace.</param>
    /// <returns>The source tier of this building's substitutes.</returns>
    TierId SubstituteSourceTier(EntityId building);

    /// <summary>Tests whether the player has marked a workplace as high priority with <c>SetWorkforcePriority</c>, so the inspector can show the toggle's state.</summary>
    /// <param name="building">The workplace.</param>
    /// <returns><see langword="true"/> if the workplace is filled first; <see langword="false"/> for normal priority or an unknown building.</returns>
    bool IsHighPriority(EntityId building);
}
