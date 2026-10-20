using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only explain queries that trace a shortage or a stall back through the chain to its cause (ADR-0005), so that
/// every number can say why it is what it is. Each answer is a tree of locale keys and typed values; the view supplies
/// the words. Implemented by the explain service (M1-12).
/// </summary>
public interface IExplainQueries
{
    /// <summary>
    /// Explains a good: its supply and demand per day with the fulfilment, then each producer type with its count,
    /// productivity and dominant stall reasons, then the chained causes of any shortage down to the root.
    /// </summary>
    /// <param name="good">The good.</param>
    /// <returns>The explanation; never <see langword="null"/>.</returns>
    Explanation ExplainGood(GoodId good);

    /// <summary>
    /// Explains a building: for a producer its productivity breakdown (workforce fill, input availability, area yield,
    /// each modifier with its source), its stall reason with a fix hint, its buffers and its storehouse reach; for a site
    /// its progress and what it waits for.
    /// </summary>
    /// <param name="building">The building.</param>
    /// <returns>The explanation; a one-line "unknown building" explanation if the building does not exist; never <see langword="null"/>.</returns>
    Explanation ExplainBuilding(EntityId building);

    /// <summary>
    /// Explains a house: each need with its status and reason, the occupancy target, the advance progress and what blocks
    /// it, and the decline timer with what stops it.
    /// </summary>
    /// <param name="house">The house.</param>
    /// <returns>The explanation; a one-line "unknown house" explanation if the building is not a house; never <see langword="null"/>.</returns>
    Explanation ExplainHouse(EntityId house);
}
