namespace Evenmere.Core.Ids;

/// <summary>
/// The identity of one entity (a building, a house, a transport job and so on). IDs are allocated by
/// <see cref="EntityIdAllocator"/>, are positive, increase monotonically and are never reused, so ordering by
/// ID is a stable iteration order (ADR-0003). Zero means "no entity".
/// </summary>
/// <param name="Value">The raw ID; zero means none.</param>
public readonly record struct EntityId(int Value) : IComparable<EntityId>
{
    /// <summary>The "no entity" value, which is also <see langword="default"/>.</summary>
    public static EntityId None => default;

    /// <summary>Gets a value indicating whether this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0;

    /// <summary>Tests whether one ID was allocated before another.</summary>
    /// <param name="left">The first ID.</param>
    /// <param name="right">The second ID.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is smaller.</returns>
    public static bool operator <(EntityId left, EntityId right) => left.Value < right.Value;

    /// <summary>Tests whether one ID was allocated after another.</summary>
    /// <param name="left">The first ID.</param>
    /// <param name="right">The second ID.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is larger.</returns>
    public static bool operator >(EntityId left, EntityId right) => left.Value > right.Value;

    /// <summary>Tests whether one ID was allocated no later than another.</summary>
    /// <param name="left">The first ID.</param>
    /// <param name="right">The second ID.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is not larger.</returns>
    public static bool operator <=(EntityId left, EntityId right) => left.Value <= right.Value;

    /// <summary>Tests whether one ID was allocated no earlier than another.</summary>
    /// <param name="left">The first ID.</param>
    /// <param name="right">The second ID.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is not smaller.</returns>
    public static bool operator >=(EntityId left, EntityId right) => left.Value >= right.Value;

    /// <inheritdoc />
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"EntityId({Value})";
}
