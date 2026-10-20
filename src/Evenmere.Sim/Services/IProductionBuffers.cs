using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

/// <summary>A producer's request for a cart: to collect output (pickup) or to bring input (delivery).</summary>
/// <param name="Building">The producer or consumer that asks.</param>
/// <param name="Direction">Whether the cart picks goods up or delivers them.</param>
/// <param name="Good">The good to move.</param>
/// <param name="Amount">The amount one trip should move, in milli-units: at most one cart load.</param>
/// <param name="RequestTick">The tick the need first arose, which is the primary sort key of the list.</param>
internal readonly record struct TransportRequest(
    EntityId Building,
    TransportDirection Direction,
    GoodId Good,
    Amount Amount,
    long RequestTick);

/// <summary>
/// What Logistics needs from Production's buffers (ADR-0005): the requests for carts, and the moves of goods between a
/// building's buffers and a cart. Implemented by the production system (M1-08).
/// </summary>
internal interface IProductionBuffers
{
    /// <summary>
    /// Appends the requests that are open now, in ascending (request tick, building ID) order. A pickup request is open
    /// while an output buffer holds at least one cart load, and a delivery request while an input buffer is below one cart
    /// load. A request stays listed until the goods have moved (<see cref="TryTakeOutput"/> or <see cref="AddInput"/>) or
    /// the buffer no longer qualifies, so Logistics must not start a second job for a request it already serves.
    /// </summary>
    /// <param name="requests">The list to append to; it is not cleared first.</param>
    void ListRequests(List<TransportRequest> requests);

    /// <summary>Takes goods from a building's output buffer into a cart, all or nothing.</summary>
    /// <param name="building">The producer.</param>
    /// <param name="good">The good.</param>
    /// <param name="amount">How much to take, in milli-units.</param>
    /// <returns><see langword="true"/> if the buffer held the whole amount and it was taken.</returns>
    bool TryTakeOutput(EntityId building, GoodId good, Amount amount);

    /// <summary>Puts goods from a cart into a building's input buffer.</summary>
    /// <param name="building">The consumer.</param>
    /// <param name="good">The good.</param>
    /// <param name="amount">How much to add, in milli-units.</param>
    void AddInput(EntityId building, GoodId good, Amount amount);
}
