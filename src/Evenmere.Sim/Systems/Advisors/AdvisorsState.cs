using MessagePack;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>
/// The advisors system's slice of the world state: active and dismissed advice. It is empty in M0; M1-15 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class AdvisorsState
{
}
