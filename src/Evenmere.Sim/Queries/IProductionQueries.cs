using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>Read-only queries over the producers, recipes and their buffers. Implemented by the production system (M1-08).</summary>
public interface IProductionQueries
{
    /// <summary>Describes one producer.</summary>
    /// <param name="building">The building.</param>
    /// <param name="info">A copy of the producer's state, when the building is a producer with a recipe.</param>
    /// <returns><see langword="true"/> if the building is a producer; <see langword="false"/> for any other building or an unknown ID.</returns>
    bool TryGet(EntityId building, out ProductionInfo info);

    /// <summary>Gets how much of a good the island produced during the previous whole day.</summary>
    /// <param name="good">The good.</param>
    /// <returns>The amount in milli-units; 0 during the first day.</returns>
    Amount ProducedYesterday(GoodId good);
}
