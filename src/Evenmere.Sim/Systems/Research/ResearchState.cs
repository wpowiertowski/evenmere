using MessagePack;

namespace Evenmere.Sim.Systems.Research;

/// <summary>
/// The research system's slice of the world state: the research target and queue, progress and finished techs. It is empty in M0; M1-11 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ResearchState
{
}
