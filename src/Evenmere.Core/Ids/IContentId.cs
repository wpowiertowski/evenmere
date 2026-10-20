namespace Evenmere.Core.Ids;

/// <summary>
/// Implemented by every content ID type (<c>GoodId</c>, <c>BuildingTypeId</c> and so on), so generic code such
/// as dense per-ID tables and serialisers can be written once over all of them.
/// </summary>
/// <typeparam name="TSelf">The implementing ID type.</typeparam>
public interface IContentId<TSelf> : IEquatable<TSelf>
    where TSelf : struct, IContentId<TSelf>
{
    /// <summary>Gets the raw index of the content item, assigned densely by the content loader.</summary>
    ushort Value { get; }

    /// <summary>Creates an ID from its raw index.</summary>
    /// <param name="value">The raw index.</param>
    /// <returns>The ID.</returns>
    static abstract TSelf From(ushort value);
}
