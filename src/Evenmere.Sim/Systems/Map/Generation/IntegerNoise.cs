namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Integer-only value noise over fixed-point coordinates (Q16.16), the randomness source for island
/// generation (ADR-0003, ADR-0019). Every operation is 32- or 64-bit integer arithmetic with well-defined
/// wraparound, so a seed produces the same island on every CPU. The generator draws from its own integer
/// hash; it never touches a framework random-number generator, floating point, or a transcendental call.
/// </summary>
public static class IntegerNoise
{
    /// <summary>The number of fractional bits in the Q16.16 coordinates.</summary>
    public const int FractionBits = 16;

    /// <summary>One whole unit in Q16.16.</summary>
    public const int Unit = 1 << FractionBits;

    /// <summary>The fractional mask: the sub-tile part of a Q16.16 coordinate.</summary>
    public const int Mask = Unit - 1;

    /// <summary>The base lattice frequency of <see cref="Fbm"/> over the unit square: the island spans this many noise cells.</summary>
    public const int BaseFrequency = 4;

    /// <summary>
    /// Mixes integer lattice coordinates and a seed into 32 uniformly distributed bits. Unsigned arithmetic
    /// wraps modulo 2^32 by language definition, so the result is identical on every platform.
    /// </summary>
    /// <param name="x">The lattice X coordinate, which may be negative.</param>
    /// <param name="y">The lattice Y coordinate, which may be negative.</param>
    /// <param name="seed">The stream selector; different seeds give uncorrelated lattices.</param>
    /// <returns>32 hash bits.</returns>
    public static uint Hash(int x, int y, int seed)
    {
        uint h = unchecked((uint)seed * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u);
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return h;
    }

    /// <summary>
    /// Smooth value noise at a Q16.16 position: the four surrounding lattice hashes interpolated with a
    /// smootherstep fade, all in integers.
    /// </summary>
    /// <param name="xQ16">The X coordinate in Q16.16.</param>
    /// <param name="yQ16">The Y coordinate in Q16.16.</param>
    /// <param name="seed">The stream selector, passed to <see cref="Hash"/>.</param>
    /// <returns>A value in <c>[0, 65535]</c>.</returns>
    public static int Sample(int xQ16, int yQ16, int seed)
    {
        // The right shift of a negative int is arithmetic, so this floors; the fractions stay in [0, Unit).
        int xi = xQ16 >> FractionBits;
        int yi = yQ16 >> FractionBits;
        int xf = xQ16 - (xi << FractionBits);
        int yf = yQ16 - (yi << FractionBits);

        uint a = Hash(xi, yi, seed) >> 16;
        uint b = Hash(xi + 1, yi, seed) >> 16;
        uint c = Hash(xi, yi + 1, seed) >> 16;
        uint d = Hash(xi + 1, yi + 1, seed) >> 16;

        int u = Fade(xf);
        int v = Fade(yf);

        long ab = (long)a * (Unit - u) + (long)b * u;
        long cd = (long)c * (Unit - u) + (long)d * u;
        long result = (ab * (Unit - v) + cd * v) >> 32;
        return (int)result;
    }

    /// <summary>
    /// A fractal sum of <see cref="Sample"/> over doubling frequencies with halving weights, sampled at a
    /// map-fraction position. Normalising by the map size keeps the island's character similar at 256, 320
    /// and 384 tiles across.
    /// </summary>
    /// <param name="nxQ16">The X fraction of the map in Q16.16, in <c>[0, 65536]</c>.</param>
    /// <param name="nyQ16">The Y fraction of the map in Q16.16, in <c>[0, 65536]</c>.</param>
    /// <param name="seed">The stream selector; each octave derives its own stream from it.</param>
    /// <param name="octaves">The number of octaves, which must be positive.</param>
    /// <returns>A value in <c>[0, 65535]</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="octaves"/> is not positive.</exception>
    public static int Fbm(int nxQ16, int nyQ16, int seed, int octaves)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(octaves);

        long total = 0;
        long weightSum = 0;
        int frequency = BaseFrequency;
        int weight = Unit;
        for (int octave = 0; octave < octaves; octave++)
        {
            int stream = unchecked(seed + (octave * unchecked((int)0x85EBCA6B)));
            total += (long)Sample(nxQ16 * frequency, nyQ16 * frequency, stream) * weight;
            weightSum += weight;
            frequency *= 2;
            weight /= 2;
        }

        return (int)(total / weightSum);
    }

    // Smootherstep 6t^5 - 15t^4 + 10t^3 for t in [0, Unit); the result is in [0, Unit].
    private static int Fade(int t)
    {
        long t2 = ((long)t * t) >> FractionBits;
        long t3 = (t2 * t) >> FractionBits;
        long t4 = (t3 * t) >> FractionBits;
        long t5 = (t4 * t) >> FractionBits;
        return (int)((6 * t5) - (15 * t4) + (10 * t3));
    }
}
