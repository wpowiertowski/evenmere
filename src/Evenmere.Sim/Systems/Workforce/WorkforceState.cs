using MessagePack;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// The workforce system's slice of the world state: the workforce supply per tier and each workplace's staffing. It is empty in M0; M1-07 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class WorkforceState
{
}
