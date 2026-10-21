namespace Evenmere.Sim.Persistence;

/// <summary>
/// Marks a state member that holds content IDs as raw numbers, so the load-time remap must rewrite them when the
/// content changed. Examples are the road layer (which stores <c>BuildingTypeId.Value + 1</c> per tile), packed counter
/// keys, encoded modifier conditions and the values inside effect sources. Typed ID members (<c>GoodId</c> and the like)
/// need no marker: the remap pass visits each of them explicitly. Like <see cref="ContentIndexedAttribute"/>, this marker
/// is read by the architecture test, not by reflection at load.
/// </summary>
/// <param name="Kinds">The content kinds whose IDs the member holds.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ContentValuedAttribute(params ContentKind[] Kinds) : Attribute
{
    /// <summary>Gets the content kinds whose IDs the member holds.</summary>
    public IReadOnlyList<ContentKind> Kinds { get; } = Kinds;
}
