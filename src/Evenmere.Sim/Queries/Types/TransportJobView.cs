using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Queries.Types;

/// <summary>Which way a transport job moves goods.</summary>
public enum TransportDirection : byte
{
    /// <summary>Goods go from a producer's output buffer to the island pool.</summary>
    Pickup = 0,

    /// <summary>Goods go from the island pool to a consumer's input buffer.</summary>
    Delivery = 1,
}

/// <summary>
/// The phase of a transport job, in the order a cart goes through them. A pickup job runs
/// <see cref="TravelOut"/> (storehouse to building), <see cref="Load"/> (at the building), <see cref="TravelBack"/>
/// (building to storehouse) and <see cref="Unload"/> (at the storehouse). A delivery job has the goods reserved
/// when it starts, so it runs <see cref="TravelOut"/> (loaded), <see cref="Unload"/> (at the building) and
/// <see cref="TravelBack"/> (empty). The view places a cart from the phase, so these meanings are part of the contract.
/// </summary>
public enum TransportPhase : byte
{
    /// <summary>The cart drives from its storehouse to the building.</summary>
    TravelOut = 0,

    /// <summary>The cart loads the goods; it stands at the building (pickup jobs only).</summary>
    Load = 1,

    /// <summary>The cart drives from the building back to its storehouse.</summary>
    TravelBack = 2,

    /// <summary>The cart unloads: at the storehouse for a pickup job, at the building for a delivery job.</summary>
    Unload = 3,
}

/// <summary>
/// A copy of one transport job for the view, which replays it as a cart (ADR-0005). The view positions the cart by the
/// progress <c>(now − PhaseStartTick) / PhaseTicks</c> through the current phase and never decides anything.
/// </summary>
/// <param name="Id">The job's entity ID.</param>
/// <param name="Storehouse">The storehouse whose cart serves the job.</param>
/// <param name="Building">The producer (pickup) or consumer (delivery) at the other end.</param>
/// <param name="Good">The good carried.</param>
/// <param name="Amount">The amount carried, in milli-units.</param>
/// <param name="Direction">Whether the job picks goods up or delivers them.</param>
/// <param name="Phase">The phase the cart is in.</param>
/// <param name="PhaseStartTick">The tick the phase began.</param>
/// <param name="PhaseTicks">How many ticks the phase lasts; a phase that ends at tick T started at <c>T − PhaseTicks</c>.</param>
public readonly record struct TransportJobView(
    EntityId Id,
    EntityId Storehouse,
    EntityId Building,
    GoodId Good,
    Amount Amount,
    TransportDirection Direction,
    TransportPhase Phase,
    long PhaseStartTick,
    int PhaseTicks)
{
    /// <summary>Gets the tick the phase ends, which is <see cref="PhaseStartTick"/> plus <see cref="PhaseTicks"/>.</summary>
    public long PhaseEndTick => PhaseStartTick + PhaseTicks;
}
