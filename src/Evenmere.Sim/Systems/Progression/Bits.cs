namespace Evenmere.Sim.Systems.Progression;

/// <summary>Bitsets over content IDs, stored as <see cref="ulong"/> words: bit <c>n</c> is word <c>n / 64</c>, bit <c>n % 64</c>.</summary>
internal static class Bits
{
    /// <summary>Gets the number of words that hold a number of bits.</summary>
    /// <param name="bitCount">The number of bits, zero or more.</param>
    /// <returns>The word count.</returns>
    public static int WordCount(int bitCount) => (bitCount + 63) >> 6;

    /// <summary>Tests a bit. A bit past the end of the set reads as clear, so a query about an ID the content does not have is simply "no".</summary>
    /// <param name="words">The set.</param>
    /// <param name="index">The bit, which is a content ID's raw value.</param>
    /// <returns><see langword="true"/> if the bit is set.</returns>
    public static bool Has(ulong[] words, int index)
    {
        int word = index >> 6;
        return (uint)word < (uint)words.Length && (words[word] & (1UL << (index & 63))) != 0;
    }

    /// <summary>Sets a bit.</summary>
    /// <param name="words">The set.</param>
    /// <param name="index">The bit, which is a content ID's raw value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The bit is past the end of the set, which means the ID is not in the content.</exception>
    public static void Set(ulong[] words, int index)
    {
        int word = index >> 6;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(word, words.Length, nameof(index));
        words[word] |= 1UL << (index & 63);
    }

    /// <summary>Counts the set bits.</summary>
    /// <param name="words">The set.</param>
    /// <returns>The number of bits that are set.</returns>
    public static int Count(ulong[] words)
    {
        int count = 0;
        foreach (ulong word in words)
        {
            count += System.Numerics.BitOperations.PopCount(word);
        }

        return count;
    }
}
