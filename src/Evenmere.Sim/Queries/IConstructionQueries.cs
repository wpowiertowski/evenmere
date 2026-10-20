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
}
