namespace Evenmere.Sim.Persistence;

/// <summary>
/// Marks a state member whose array elements are indexed by a content ID, so the load-time remap must permute them when
/// the content changed. A <c>GoodId.Value</c>-indexed stock array moves each entry to its new ID; a bitset over techs
/// moves each bit; a statistics history rebuilds its series in the new order. Members with several kinds (the statistics
/// histories, which hold goods blocks and then tier blocks) list every kind they hold. The remap itself lives in the
/// persistence pass, which reads these markers through the architecture test, not through reflection at load.
/// </summary>
/// <param name="Kinds">The content kinds the member is indexed by, in the order they appear in the member.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ContentIndexedAttribute(params ContentKind[] Kinds) : Attribute
{
    /// <summary>Gets the content kinds the member is indexed by.</summary>
    public IReadOnlyList<ContentKind> Kinds { get; } = Kinds;
}
