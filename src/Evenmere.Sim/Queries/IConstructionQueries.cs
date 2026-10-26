using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the construction sites. Implemented by the construction system (M1-06). Placement validation
/// is in <see cref="IPlacementQueries"/>.
/// </summary>
public interface IConstructionQueries
{
    /// <summary>Describes the progress of one construction site, for the inspector, the explain queries and the scaffolding visuals.</summary>
    /// <param name="building">The building.</param>
    /// <param name="site">A copy of the site's progress, when the building is a site.</param>
    /// <returns><see langword="true"/> if the building is a construction site; <see langword="false"/> for a finished building or an unknown ID.</returns>
    bool TryGetSite(EntityId building, out SiteInfo site);

    /// <summary>Gets the type an in-place upgrade site is building toward, for the workforce planned demand (M2-30).</summary>
    /// <param name="building">The building being upgraded.</param>
    /// <param name="target">The type the building becomes when the upgrade completes, when it is being upgraded.</param>
    /// <returns><see langword="true"/> if the building has an upgrade site; <see langword="false"/> for a new-building site, a finished building or an unknown ID.</returns>
    bool TryGetUpgradeTarget(EntityId building, out BuildingTypeId target);
}
