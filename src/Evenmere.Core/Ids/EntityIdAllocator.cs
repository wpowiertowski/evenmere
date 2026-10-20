namespace Evenmere.Core.Ids;

/// <summary>
/// Hands out <see cref="EntityId"/> values in strictly increasing order, starting at 1, and never reuses one.
/// The only state is <see cref="NextValue"/>, which a save stores and restores through the constructor.
/// </summary>
public sealed class EntityIdAllocator
{
    private int _nextValue;

    /// <summary>Initializes a new instance of the <see cref="EntityIdAllocator"/> class whose first ID is 1.</summary>
    public EntityIdAllocator()
        : this(1)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EntityIdAllocator"/> class from saved state.</summary>
    /// <param name="nextValue">The value <see cref="NextValue"/> had when the state was saved; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nextValue"/> is less than 1.</exception>
    public EntityIdAllocator(int nextValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextValue, 1);
        _nextValue = nextValue;
    }

    /// <summary>Gets the raw value of the ID the next call to <see cref="Allocate"/> returns. This is the serialisable state.</summary>
    public int NextValue => _nextValue;

    /// <summary>Allocates the next ID.</summary>
    /// <returns>An ID larger than every ID returned before.</returns>
    /// <exception cref="OverflowException">The ID space is exhausted; the allocator state is left unchanged.</exception>
    public EntityId Allocate()
    {
        int value = _nextValue;
        _nextValue = checked(value + 1);
        return new EntityId(value);
    }
}
