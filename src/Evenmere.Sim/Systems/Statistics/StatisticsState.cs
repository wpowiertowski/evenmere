using MessagePack;

namespace Evenmere.Sim.Systems.Statistics;

/// <summary>
/// The statistics system's slice of the world state: the rolling one-year windows of daily samples. It is empty in M0; M1-12 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class StatisticsState
{
}
