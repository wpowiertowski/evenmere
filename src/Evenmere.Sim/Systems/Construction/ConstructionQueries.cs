using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>Implements <see cref="IConstructionQueries"/>. M1-00 declares the methods and M1-06 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class ConstructionQueries : IConstructionQueries
{
    /// <inheritdoc />
    public bool TryGetSite(EntityId building, out SiteInfo site) => throw new NotImplementedException("M1-06");
}
