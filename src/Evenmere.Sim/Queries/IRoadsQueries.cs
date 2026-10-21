using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the road layer, connectivity and the storehouse reach and service coverage fields
/// (ADR-0005). Implemented by the roads service (M1-05). Costs are in road cost units, where 1000 is one tile of dirt
/// road and cheaper road types cost less per tile (cobbled 800, paved 650).
/// </summary>
public interface IRoadsQueries
{
    /// <summary>Finds the road on a tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The road type (a building type with a road cost), or <see langword="null"/> when the tile has no road or is off the map.</returns>
    BuildingTypeId? RoadAt(int x, int y);

    /// <summary>Tests whether at least one road tile touches the building's footprint along an edge.</summary>
    /// <param name="building">The building or site.</param>
    /// <returns><see langword="true"/> if the building has road access; <see langword="false"/> if it has none or does not exist.</returns>
    bool HasRoadAccess(EntityId building);

    /// <summary>Tests whether some storehouse's reach covers the building, so it takes part in the island's goods pool. A storehouse is in its own reach.</summary>
    /// <param name="building">The building or site.</param>
    /// <returns><see langword="true"/> if a storehouse's road distance to the building is within that storehouse's reach.</returns>
    bool IsInStorehouseReach(EntityId building);

    /// <summary>
    /// Tests whether a footprint that is not built yet would lie in storehouse reach, for the construction ghost preview. It
    /// is <see langword="true"/> when some road tile orthogonally adjacent to the footprint lies inside a finished
    /// storehouse's reach field, using the same fields and costs as <see cref="IsInStorehouseReach"/>. A building later
    /// placed on the footprint therefore gets the same answer there, except that a storehouse is always in its own reach
    /// and there is no storehouse here yet.
    /// </summary>
    /// <param name="x">The column of the footprint's top-left tile.</param>
    /// <param name="y">The row of the footprint's top-left tile.</param>
    /// <param name="width">The footprint width in tiles after rotation.</param>
    /// <param name="height">The footprint height in tiles after rotation.</param>
    /// <returns><see langword="true"/> if a road tile touching the footprint is within some storehouse's reach; <see langword="false"/> if none is, or the footprint is empty.</returns>
    bool IsFootprintInStorehouseReach(int x, int y, int width, int height);

    /// <summary>Finds the storehouse nearest to a building by road distance.</summary>
    /// <param name="building">The building or site.</param>
    /// <param name="storehouse">The nearest storehouse whose reach covers the building; the lowest ID wins a tie.</param>
    /// <param name="cost">The road cost from the storehouse to the building's nearest access tile, in cost units (1000 = one dirt tile).</param>
    /// <returns><see langword="true"/> if a storehouse reaches the building.</returns>
    bool TryNearestStorehouse(EntityId building, out EntityId storehouse, out int cost);

    /// <summary>Finds the road cost from one particular storehouse to a building, for logistics to choose among storehouses by distance.</summary>
    /// <param name="storehouse">The storehouse.</param>
    /// <param name="building">The building or site.</param>
    /// <param name="cost">The road cost from the storehouse to the building's nearest access tile, in cost units (1000 = one dirt tile).</param>
    /// <returns><see langword="true"/> if the building lies within that storehouse's reach.</returns>
    bool TryGetStorehouseCost(EntityId storehouse, EntityId building, out int cost);

    /// <summary>
    /// Tests whether a house lies within the coverage of a building that provides a service. For
    /// <see cref="ServiceKind.Market"/> the market must also be in storehouse reach (ADR-0005), so a market cut off from
    /// every storehouse covers nobody.
    /// </summary>
    /// <param name="house">The house.</param>
    /// <param name="service">The service.</param>
    /// <returns><see langword="true"/> if the house is covered.</returns>
    bool IsCovered(EntityId house, ServiceKind service);

    /// <summary>
    /// Finds a route over road tiles between two buildings, for the view to animate a cart. It is for presentation only:
    /// systems never call it, and nothing in the simulation depends on its result.
    /// </summary>
    /// <param name="source">The building the route starts at.</param>
    /// <param name="destination">The building the route ends at.</param>
    /// <param name="path">Cleared, then filled with the road tiles from <paramref name="source"/>'s nearest access tile to <paramref name="destination"/>'s, inclusive.</param>
    /// <returns><see langword="true"/> if a route exists.</returns>
    bool TryFindViewPath(EntityId source, EntityId destination, List<TileCoord> path);

    /// <summary>Finds a route over road tiles between two road tiles, for presentation only.</summary>
    /// <param name="source">The road tile the route starts at.</param>
    /// <param name="destination">The road tile the route ends at.</param>
    /// <param name="path">Cleared, then filled with the road tiles from <paramref name="source"/> to <paramref name="destination"/>, inclusive.</param>
    /// <returns><see langword="true"/> if both tiles are roads and a route exists.</returns>
    bool TryFindViewPath(TileCoord source, TileCoord destination, List<TileCoord> path);
}
