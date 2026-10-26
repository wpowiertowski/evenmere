using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Exploration;

namespace Evenmere.Sim.Queries;

/// <summary>Read-only queries over the fog, expeditions and discoveries (M2-06, per ADR-0017).</summary>
public interface IExplorationQueries
{
    /// <summary>Tests whether a region's fog has lifted.</summary>
    /// <param name="region">The region number.</param>
    /// <returns><see langword="true"/> when the region is revealed; <see langword="false"/> for an unknown region on a map that has none of that number.</returns>
    bool IsRevealed(ushort region);

    /// <summary>Lists every revealed region number, in ascending order.</summary>
    /// <returns>The revealed regions.</returns>
    ushort[] RevealedRegions();

    /// <summary>Gets the expedition a lodge is running, if one is under way.</summary>
    /// <param name="lodge">The Scout's Lodge entity.</param>
    /// <returns>The expedition with its target and days left, or <see langword="null"/> when the lodge is idle or unknown.</returns>
    ExpeditionView? ExpeditionForLodge(EntityId lodge);

    /// <summary>Lists every expedition under way, in ascending lodge-ID order.</summary>
    /// <returns>The active expeditions.</returns>
    ExpeditionView[] Expeditions();

    /// <summary>Lists every discovery placed on the island, in map order, with whether its fog has lifted.</summary>
    /// <returns>The discoveries.</returns>
    DiscoveryView[] Discoveries();
}
