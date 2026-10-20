using System.Collections.Immutable;

namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// Element-wise equality and hashing for the arrays that records carry. A record compares its array members by
/// reference, so a record that holds an array calls these from its own <c>Equals</c> and <c>GetHashCode</c> to get value
/// semantics: two commands or query results with the same contents are equal. A default (uninitialised)
/// <see cref="ImmutableArray{T}"/> counts as empty, and so does a <see langword="null"/> array.
/// </summary>
internal static class ValueArrays
{
    /// <summary>Compares two arrays element by element.</summary>
    /// <typeparam name="T">The element type, compared with its default equality.</typeparam>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> if they have the same length and equal elements in the same order.</returns>
    public static bool Equal<T>(T[]? left, T[]? right) => left.AsSpan().SequenceEqual(right.AsSpan());

    /// <summary>Compares two immutable arrays element by element.</summary>
    /// <typeparam name="T">The element type, compared with its default equality.</typeparam>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> if they have the same length and equal elements in the same order.</returns>
    public static bool Equal<T>(ImmutableArray<T> left, ImmutableArray<T> right) =>
        left.AsSpan().SequenceEqual(right.AsSpan());

    /// <summary>Computes a hash code from the elements, in order. It is for equality only, never for ordering or for state.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The array.</param>
    /// <returns>The hash code.</returns>
    public static int Hash<T>(T[]? array) => Hash(array.AsSpan());

    /// <summary>Computes a hash code from the elements, in order. It is for equality only, never for ordering or for state.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The array.</param>
    /// <returns>The hash code.</returns>
    public static int Hash<T>(ImmutableArray<T> array) => Hash(array.AsSpan());

    private static int Hash<T>(ReadOnlySpan<T> items)
    {
        unchecked
        {
            int hash = items.Length;
            foreach (T item in items)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }
}
