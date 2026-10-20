using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>A cart set out on a transport job. The view starts replaying the job (its phases are in <c>ILogisticsQueries.ActiveJobs</c>).</summary>
/// <param name="Tick">The tick during which the job started.</param>
/// <param name="Job">The job's entity ID.</param>
/// <param name="Storehouse">The storehouse whose cart serves the job.</param>
/// <param name="Building">The producer (pickup) or consumer (delivery) at the other end.</param>
/// <param name="Good">The good carried.</param>
/// <param name="Amount">The amount carried, in milli-units.</param>
/// <param name="Direction">Whether the job picks goods up or delivers them.</param>
public sealed record TransportJobStarted(
    long Tick, EntityId Job, EntityId Storehouse, EntityId Building, GoodId Good, Amount Amount, TransportDirection Direction)
    : SimEvent(Tick);

/// <summary>A transport job finished: the cart is back at its storehouse and the goods have reached their destination.</summary>
/// <param name="Tick">The tick during which the job finished; the cart's arrival on screen matches it.</param>
/// <param name="Job">The job's entity ID.</param>
/// <param name="Storehouse">The storehouse whose cart served the job.</param>
/// <param name="Building">The producer (pickup) or consumer (delivery) at the other end.</param>
/// <param name="Good">The good carried.</param>
/// <param name="Amount">The amount carried, in milli-units.</param>
/// <param name="Direction">Whether the job picked goods up or delivered them.</param>
public sealed record TransportJobCompleted(
    long Tick, EntityId Job, EntityId Storehouse, EntityId Building, GoodId Good, Amount Amount, TransportDirection Direction)
    : SimEvent(Tick);
