using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>An era began: both gateways of the next era completed, so the island moved into it. (M2-11.)</summary>
/// <param name="Tick">The tick during which the era began, the same tick the second gateway completed.</param>
/// <param name="Era">The era that began.</param>
public sealed record EraBegan(long Tick, EraId Era) : SimEvent(Tick);
