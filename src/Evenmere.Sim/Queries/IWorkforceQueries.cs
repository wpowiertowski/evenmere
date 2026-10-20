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

    /// <summary>Tests whether the player has marked a workplace as high priority with <c>SetWorkforcePriority</c>, so the inspector can show the toggle's state.</summary>
    /// <param name="building">The workplace.</param>
    /// <returns><see langword="true"/> if the workplace is filled first; <see langword="false"/> for normal priority or an unknown building.</returns>
    bool IsHighPriority(EntityId building);
}
