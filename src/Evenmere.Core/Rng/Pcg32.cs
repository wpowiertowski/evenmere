using System.Numerics;
using Evenmere.Core.Hashing;
using Evenmere.Core.Numerics;

namespace Evenmere.Core.Rng;

/// <summary>
/// The standard PCG32 generator (XSH-RR, 64-bit state, 32-bit output) with the pcg-c reference seeding. It is
/// the only source of randomness in the simulation (ADR-0003): each system owns one, stored in the world state.
/// </summary>
/// <remarks>
/// This is a mutable value type. Keep it in a field and call methods on that field, so draws advance the stored
/// state; calling them on a copy advances only the copy. A <see langword="default"/> instance is not a valid
/// generator (its increment is even and it returns zero forever), so always create one with the constructor,
/// <see cref="ForSystem"/> or <see cref="FromState"/>.
/// </remarks>
public struct Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private ulong _inc;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pcg32"/> struct with the reference
    /// <c>pcg32_srandom_r(initstate, initseq)</c> seeding: the state starts at zero, the increment becomes
    /// <c>(stream &lt;&lt; 1) | 1</c>, then the generator steps, adds the seed and steps again.
    /// </summary>
    /// <param name="seed">The initial state (<c>initstate</c> in pcg-c).</param>
    /// <param name="stream">The sequence selector (<c>initseq</c> in pcg-c); the top bit is discarded.</param>
    public Pcg32(ulong seed, ulong stream)
    {
        _state = 0;
        _inc = unchecked((stream << 1) | 1UL);
        _ = NextUInt();
        _state = unchecked(_state + seed);
        _ = NextUInt();
    }

    /// <summary>Gets the raw 64-bit state. Together with <see cref="Inc"/> this is everything a save needs to store.</summary>
    public readonly ulong State => _state;

    /// <summary>Gets the raw 64-bit increment, which is always odd for a valid generator.</summary>
    public readonly ulong Inc => _inc;

    /// <summary>Restores a generator from state previously read from <see cref="State"/> and <see cref="Inc"/>.</summary>
    /// <param name="state">The saved state.</param>
    /// <param name="inc">The saved increment, which must be odd.</param>
    /// <returns>A generator that continues exactly where the saved one stopped.</returns>
    /// <exception cref="ArgumentException"><paramref name="inc"/> is even, so the state cannot come from a valid generator.</exception>
    public static Pcg32 FromState(ulong state, ulong inc)
    {
        if ((inc & 1UL) == 0)
        {
            throw new ArgumentException("A PCG32 increment is always odd; the saved state is corrupt.", nameof(inc));
        }

        Pcg32 rng = default;
        rng._state = state;
        rng._inc = inc;
        return rng;
    }

    /// <summary>
    /// Creates the generator a system uses, so each system draws from its own independent stream. The stream
    /// is the XxHash64 of the system name's UTF-8 bytes and the seed is the world seed. This derivation is
    /// part of the save format: changing it changes every existing world's randomness.
    /// </summary>
    /// <param name="worldSeed">The world seed.</param>
    /// <param name="systemName">The system's stable name, such as <c>"production"</c>.</param>
    /// <returns>The generator for that system.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="systemName"/> is <see langword="null"/>.</exception>
    public static Pcg32 ForSystem(ulong worldSeed, string systemName)
    {
        ArgumentNullException.ThrowIfNull(systemName);
        StableHasher hasher = new();
        hasher.AddUtf8(systemName);
        return new Pcg32(worldSeed, hasher.ToUInt64());
    }

    /// <summary>Returns the next 32 uniformly distributed bits.</summary>
    /// <returns>A value in <c>[0, 2^32)</c>.</returns>
    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked((old * Multiplier) + _inc);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    /// <summary>Returns a uniformly distributed integer in <c>[0, maxExclusive)</c> with no modulo bias.</summary>
    /// <param name="maxExclusive">The exclusive upper bound, which must be positive.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is not positive.</exception>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)NextBounded((uint)maxExclusive);
    }

    /// <summary>Returns a uniformly distributed integer in <c>[min, maxExclusive)</c> with no modulo bias.</summary>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="maxExclusive">The exclusive upper bound, which must be greater than <paramref name="min"/>.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is not greater than <paramref name="min"/>.</exception>
    public int NextInt(int min, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxExclusive, min);

        // The span can exceed int.MaxValue, so compute it, and the offset, in unsigned arithmetic.
        uint range = unchecked((uint)maxExclusive - (uint)min);
        return unchecked((int)((uint)min + NextBounded(range)));
    }

    /// <summary>
    /// Returns <see langword="true"/> with the given probability. It always consumes exactly one draw (apart
    /// from the rare rejection in <see cref="NextInt(int)"/>), whatever the ratio, so changing a balance value
    /// never shifts the rest of the random sequence. A ratio of 1000 or more always succeeds; zero or less never does.
    /// </summary>
    /// <param name="probability">The chance of success, where 1000 per-mille is certain.</param>
    /// <returns>Whether the event happens.</returns>
    public bool Chance(Ratio probability) => NextInt(1000) < probability.PerMille;

    // Lemire's nearly-divisionless method: the high half of x * range is uniform once draws whose low half
    // falls below 2^32 mod range are rejected.
    private uint NextBounded(uint range)
    {
        ulong product = (ulong)NextUInt() * range;
        uint low = (uint)product;
        if (low < range)
        {
            uint threshold = unchecked(0u - range) % range;
            while (low < threshold)
            {
                product = (ulong)NextUInt() * range;
                low = (uint)product;
            }
        }

        return (uint)(product >> 32);
    }
}
