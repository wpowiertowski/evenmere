namespace Evenmere.Sim.Persistence;

/// <summary>
/// Marks an array-typed state member that the content remap must leave alone. The architecture test requires every
/// array-typed member of every <c>[MessagePackObject]</c> type reachable from the world state to carry either a
/// content marker or this one, so adding a new per-good array without deciding is a test failure, not a silent
/// corruption. The reason says why the member is safe: entity IDs, tile-local enums, parallel arrays permuted with
/// their content-indexed sibling, and the like.
/// </summary>
/// <param name="Reason">Why the remap leaves the member alone.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NotContentIndexedAttribute(string Reason) : Attribute
{
    /// <summary>Gets why the remap leaves the member alone.</summary>
    public string Reason { get; } = Reason;
}
