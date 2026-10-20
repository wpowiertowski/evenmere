using MessagePack;

namespace Evenmere.Sim.Systems.Needs;

/// <summary>
/// The needs system's slice of the world state: per-house need fulfilment and the day's demand. It is empty in M0; M1-10 fills it.
/// Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class NeedsState
{
}
