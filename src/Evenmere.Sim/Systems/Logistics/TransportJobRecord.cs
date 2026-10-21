using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using MessagePack;

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>
/// Everything the logistics system remembers about one cart job: its endpoints, its goods, which phase it is in and
/// when that phase began. A pickup job runs <see cref="TransportPhase.TravelOut"/>, <see cref="TransportPhase.Load"/>,
/// <see cref="TransportPhase.TravelBack"/> and <see cref="TransportPhase.Unload"/>; a delivery job runs
/// <see cref="TransportPhase.TravelOut"/>, <see cref="TransportPhase.Unload"/> and <see cref="TransportPhase.TravelBack"/>,
/// because its goods are reserved from the pool at assignment and ride out already loaded.
/// </summary>
[MessagePackObject]
public sealed class TransportJobRecord
{
    /// <summary>Gets or sets the storehouse whose cart serves the job.</summary>
    [Key(0)]
    public EntityId Storehouse { get; set; }

    /// <summary>Gets or sets the producer (pickup) or consumer (delivery) at the other end.</summary>
    [Key(1)]
    public EntityId Building { get; set; }

    /// <summary>Gets or sets the good the cart moves.</summary>
    [Key(2)]
    public GoodId Good { get; set; }

    /// <summary>Gets or sets how much one trip moves, in milli-units: at most one cart load.</summary>
    [Key(3)]
    public Amount Amount { get; set; }

    /// <summary>Gets or sets whether the cart picks goods up or delivers them.</summary>
    [Key(4)]
    public TransportDirection Direction { get; set; }

    /// <summary>Gets or sets the phase the cart is in.</summary>
    [Key(5)]
    public TransportPhase Phase { get; set; }

    /// <summary>Gets or sets the tick the current phase began.</summary>
    [Key(6)]
    public long PhaseStartTick { get; set; }

    /// <summary>Gets or sets how many ticks the current phase lasts: <see cref="LegTicks"/> for a travel phase, <see cref="LogisticsSystem.HandlingTicks"/> for a handling phase.</summary>
    [Key(7)]
    public int PhaseTicks { get; set; }

    /// <summary>Gets or sets how many ticks one leg (storehouse to building, or back) takes, from the road cost, the cart speed and the island-wide cart-speed bonus.</summary>
    [Key(8)]
    public int LegTicks { get; set; }

    /// <summary>Gets or sets the goods on the cart now, in milli-units: the reservation for a delivery, the picked-up goods for a pickup past its load, otherwise zero.</summary>
    [Key(9)]
    public Amount Carried { get; set; }

    /// <summary>Gets or sets whether an endpoint vanished mid-job: the job then finishes its current phase and returns what it carries to the pool.</summary>
    [Key(10)]
    public bool Interrupted { get; set; }
}
