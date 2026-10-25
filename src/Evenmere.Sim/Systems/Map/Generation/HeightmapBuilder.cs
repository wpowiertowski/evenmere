namespace Evenmere.Sim.Systems.Map.Generation;

/// <summary>
/// Builds the island elevation grid from integer noise over a warped, anisotropic envelope (all integers,
/// per ADR-0003). Each seed gets an elliptical falloff bowl with its own orientation, aspect ratio and
/// centre offset, plus a low-frequency warp of the radius that carves bays, headlands and peninsulas. The
/// bowl still ends at the sea-margin line: every tile closer than the margin to an edge has zero relief,
/// so the margin is sea for any noise value and the island never touches the map edge. The relief is a
/// weak envelope under noise-led massifs, so high ground forms patches and ridges wherever the noise
/// peaks, usually away from the map centre. Elevations are quantised to 0–15, where 0 is the sea.
/// </summary>
internal static class HeightmapBuilder
{
    /// <summary>The highest elevation step, matching <c>MapState.MaxElevation</c>.</summary>
    public const int MaxElevation = 15;

    /// <summary>The number of noise octaves in the elevation field.</summary>
    public const int Octaves = 5;

    /// <summary>The number of noise octaves in the coastline warp field; fewer octaves stay smooth.</summary>
    public const int WarpOctaves = 3;

    /// <summary>
    /// The radial relief at the envelope centre, in per-mille. It is deliberately weak: the noise builds
    /// the massifs, and the radial term only guarantees one connected island.
    /// </summary>
    public const int RadialPerMille = 150;

    /// <summary>The noise amplitude at the envelope centre, in per-mille; it tapers to zero with the relief.</summary>
    public const int NoiseAmplitudePerMille = 300;

    /// <summary>The closest any land may come to the map edge, in tiles: room for open sea around the island.</summary>
    public const int MinSeaMargin = 12;

    /// <summary>The sea margin grows with the island: this many tiles per hundred tiles of size.</summary>
    public const int SeaMarginPerHundred = 6;

    /// <summary>The per-mille height below which a tile is sea (elevation 0).</summary>
    public const int SeaLevelPerMille = 4;

    /// <summary>The envelope centre wanders this far from the map centre: tiles per hundred tiles of size.</summary>
    public const int CentreWanderPerHundred = 1;

    /// <summary>The widest envelope aspect ratio, in per-mille (1600 is 1.6 : 1).</summary>
    public const int MaxAspectPerMille = 1600;

    /// <summary>The number of envelope orientations; the bowl turns in multiples of 360 / this.</summary>
    public const int Orientations = 8;

    /// <summary>The coastline warp amplitude, in per-mille of the envelope radius.</summary>
    public const int WarpPerMille = 70;

    /// <summary>The coastline warp never exceeds this, in per-mille of the envelope radius.</summary>
    public const int MaxWarpPerMille = 140;

    /// <summary>The massif bump height at its peak, in per-mille: it plants the summit off-centre.</summary>
    /// <summary>The massif bump height at its peak, in per-mille: it plants the summit off-centre.</summary>
    public const int MassifPerMille = 1300;

    /// <summary>The massif radius as a fraction of the bowl radius, in per-mille.</summary>
    public const int MassifRadiusPerMille = 550;

    /// <summary>The massif centre lies within this fraction of the bowl radius, in per-mille.</summary>
    public const int MassifReachPerMille = 600;

    /// <summary>The shoreline roughness amplitude, in per-mille: short-wavelength wiggle that never rests.</summary>
    public const int RoughnessPerMille = 600;

    /// <summary>The shoreline roughness never exceeds this, in per-mille.</summary>
    public const int MaxRoughnessPerMille = 800;

    /// <summary>The shoreline roughness never goes below this, in per-mille: shallow dips detach fewer islets.</summary>
    public const int MinRoughnessPerMille = -400;

    /// <summary>Skerries sprout only this far out in the shallows, in per-mille of relief.</summary>
    public const int SkerryBandPerMille = 220;

    /// <summary>The share of shallow tiles that become skerries, in per-mille.</summary>
    public const int SkerryDensityPerMille = 70;

    /// <summary>The height a skerry rises above the shallows, in per-mille.</summary>
    public const int SkerryBumpPerMille = 150;

    /// <summary>Stream separation for the skerry dice.</summary>
    public const int SkerryStreamXor = unchecked((int)0x6A09E667);

    /// <summary>Stream separation between the envelope dice, the warp field and the relief noise.</summary>
    public const int WarpStreamXor = unchecked((int)0x4E3A9C7B);

    // Cosine and sine of k * 45 degrees in Q16.16, for the envelope orientation without any floating point.
    private static readonly int[] _cosQ16 = [65536, 46341, 0, -46341, -65536, -46341, 0, 46341];
    private static readonly int[] _sinQ16 = [0, 46341, 65536, 46341, 0, -46341, -65536, -46341];

    /// <summary>
    /// The guaranteed sea margin for an island size: every tile closer than this to any edge is sea.
    /// </summary>
    /// <param name="size">The width and height in tiles.</param>
    /// <returns>The margin in tiles.</returns>
    public static int SeaMarginFor(int size) => Math.Max(MinSeaMargin, (size * SeaMarginPerHundred) / 100);

    /// <summary>
    /// Builds the elevation grid for a square island of the given size.
    /// </summary>
    /// <param name="size">The width and height in tiles.</param>
    /// <param name="seed">The generator seed.</param>
    /// <returns><c>size × size</c> elevations in <c>[0, 15]</c>, rows from the top edge.</returns>
    public static byte[] BuildElevation(int size, int seed)
    {
        byte[] elevation = new byte[size * size];
        int[] height = new int[size * size];

        // The per-seed envelope: an ellipse (orientation, aspect, centre) that the falloff bowl follows.
        // Every value derives from integer hashes of the seed, so it is identical on every CPU.
        int half = size / 2;
        int margin = SeaMarginFor(size);
        int wander = (size * CentreWanderPerHundred) / 100;
        int envelopeX = (int)(IntegerNoise.Hash(seed, 11, 0x51F3A9) % (2 * wander + 1)) - wander;
        int envelopeY = (int)(IntegerNoise.Hash(seed, 29, 0x51F3A9) % (2 * wander + 1)) - wander;
        int orientation = (int)(IntegerNoise.Hash(seed, 47, 0x51F3A9) % Orientations);
        int aspectPerMille = 1000 + (int)(IntegerNoise.Hash(seed, 67, 0x51F3A9) % (MaxAspectPerMille - 1000 + 1));
        int cos = _cosQ16[orientation];
        int sin = _sinQ16[orientation];

        // The bowl radius leaves room for the centre wander: the closest margin tile to the envelope
        // centre is still outside the bowl, and the warp below is gated to zero out there.
        long leastMargin = (half - margin) - wander;
        long bowlSquared = leastMargin * leastMargin;

        // A massif bump planted off-centre: the summit region sits away from the map centre on most
        // seeds, and high ground forms patches around it instead of rings around the middle.
        long massifRadius = (leastMargin * MassifRadiusPerMille) / 1000;
        long massifSigmaSquared = massifRadius * massifRadius;
        long massifReach = (leastMargin * MassifReachPerMille) / 1000;
        int massifAngle = (int)(IntegerNoise.Hash(seed, 83, 0x51F3A9) % Orientations);
        long massifDist = (massifReach * (500 + ((long)(IntegerNoise.Hash(seed, 101, 0x51F3A9) % 1000) * 500) / 1000)) / 1000;
        int massifX = envelopeX + (int)((massifDist * _cosQ16[massifAngle]) >> 16);
        int massifY = envelopeY + (int)((massifDist * _sinQ16[massifAngle]) >> 16);
        int roughSeed = seed ^ unchecked((int)0x2B7E4A63);
        int warpSeed = seed ^ WarpStreamXor;
        int span = size - 1;
        // The sea level compensates the envelope aspect, so a skinny island covers about the same
        // land as a round one: without this the 1.6 : 1 envelope would always make the smallest islands.
        // It stays positive, so the margin (capped at zero height) is sea whatever its value.
        int seaLevel = (int)(((long)SeaLevelPerMille * (1000 + aspectPerMille)) / (2 * aspectPerMille));
        int summit = seaLevel;
        for (int y = 0; y < size; y++)
        {
            int nyQ16 = (int)(((long)y * IntegerNoise.Unit) / size);
            int yCentred = y - (span / 2) - envelopeY;
            for (int x = 0; x < size; x++)
            {
                int nxQ16 = (int)(((long)x * IntegerNoise.Unit) / size);
                // Sampled at twice the density, so the largest texture wavelength is half the island:
                // island-scale swells would move whole shorelines and scatter island sizes.
                int noise = IntegerNoise.Fbm((nxQ16 * 2) + 3, (nyQ16 * 2) + 5, seed, Octaves);

                // Elliptical squared distance from the envelope centre, in square tiles.
                int xCentred = x - (span / 2) - envelopeX;
                int rotatedX = (int)(((long)xCentred * cos - (long)yCentred * sin) >> 16);
                int rotatedY = (int)(((long)xCentred * sin + (long)yCentred * cos) >> 16);
                long stretchedY = ((long)rotatedY * aspectPerMille) / 1000;
                long ellipseSquared = ((long)rotatedX * rotatedX) + (stretchedY * stretchedY);
                int bowlPerMille = 1000 - (int)((ellipseSquared * 1000) / bowlSquared);

                // Low-frequency warp of the radius, cut hard at the margin line: full bays and
                // headlands everywhere inland, while margin tiles keep the unwarped (sea) value by
                // construction (their bowl is never positive, so the fade below is always zero there).
                int fade = bowlPerMille > 0 ? 150 : 0;
                int warpSample = IntegerNoise.Fbm((nxQ16 * 2) + 17, (nyQ16 * 2) + 101, warpSeed, WarpOctaves);
                int warpPerMille = Math.Max(-MaxWarpPerMille, Math.Min((int)(((long)(warpSample - 32768) * WarpPerMille) / 9000), MaxWarpPerMille));
                int warpedPerMille = bowlPerMille + ((warpPerMille * fade) / 150);

                // Short-wavelength shoreline roughness, on the same fade: outward-only teeth that keep
                // every coast ragged even where the warp field is locally flat. Outward-only, so the
                // teeth stay attached to the mainland instead of pinching off into islets.
                int roughSample = IntegerNoise.Sample((nxQ16 * 20) + 501, (nyQ16 * 20) + 701, roughSeed);
                int roughPerMille = Math.Max(MinRoughnessPerMille, Math.Min((int)(((long)(roughSample - 32768) * RoughnessPerMille) / 16384), MaxRoughnessPerMille));
                int relief = Math.Max(0, warpedPerMille + ((roughPerMille * fade) / 150));

                // The off-centre massif with a flat top, gated twice by the relief so it vanishes at
                // the margin. The plateau spreads the top elevation bands over the whole cap, so hills
                // and highlands form patches instead of rings and the summit sits off-centre.
                int massifDx = xCentred - (massifX - envelopeX);
                int massifDy = yCentred - (massifY - envelopeY);
                long massifSquared = ((long)massifDx * massifDx) + ((long)massifDy * massifDy);
                int massif = 0;
                if (massifSquared < massifSigmaSquared)
                {
                    int plateau = (int)(((massifSigmaSquared - massifSquared) * 2000) / massifSigmaSquared);
                    massif = (int)((MassifPerMille * (long)Math.Min(plateau, 1000)) / 1000);
                }

                // The height is relief × (weak radial + tapered noise + massif). Zero relief caps the
                // height at zero and the clamp swallows negative relief, whatever the noise says.
                int h = (int)((relief * (RadialPerMille + (((long)NoiseAmplitudePerMille * noise) / 65535) + (((long)massif * relief) / 1000))) / 1000);

                // Rocky skerries in the shallows: a fixed share of shallow tiles rises above the sea.
                // Gated by positive relief, so the margin (zero relief) never sprouts any by construction.
                if (relief > 0 && relief <= SkerryBandPerMille
                    && (IntegerNoise.Hash(x, y, seed ^ SkerryStreamXor) % 1000) < SkerryDensityPerMille)
                {
                    h += SkerryBumpPerMille;
                }
                height[(y * size) + x] = h;
                if (h > summit)
                {
                    summit = h;
                }
            }
        }

        // The island's summit always maps to the top step, so every island has peaks and highlands
        // no matter how calm its noise is. The envelope centre always clears the sea level (its relief
        // is 1000 and the radial term alone exceeds it), so the divisor below stays positive.
        int headroom = summit - seaLevel;
        for (int i = 0; i < height.Length; i++)
        {
            elevation[i] = Quantize(height[i], headroom, seaLevel);
        }

        return elevation;
    }

    // Below the sea level is the sea (0); above it the island's own range maps onto 1-15 through a
    // square root, so the beach (elevation 1) is a narrow fringe while the middle bands that farms and
    // forests need stay broad. The summit still maps to exactly 15.
    private static byte Quantize(int heightPerMille, int headroom, int seaLevel)
    {
        if (heightPerMille < seaLevel)
        {
            return 0;
        }

        // f = (h - S) / headroom in Q16.16, then elevation = 1 + 14 * sqrt(f): all integers.
        long fraction = (((long)heightPerMille - seaLevel) << 32) / headroom;
        int rootQ16 = ISqrt(fraction);
        int elevation = 1 + ((MaxElevation - 1) * rootQ16 >> 16);
        return (byte)Math.Min(elevation, MaxElevation);
    }

    // The integer square root, rounded down: the greatest r with r * r <= n. A binary search over
    // [0, 65536], so the result is identical on every CPU; n never exceeds 2^32 here.
    private static int ISqrt(long n)
    {
        int low = 0;
        int high = 65536;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if ((long)mid * mid <= n)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }
}
