using Evenmere.Core.Ids;

namespace Evenmere.Game.Rendering.Life;

/// <summary>
/// The view-side deterministic random source for walkers (carts replay the sim exactly and need none). It is a plain
/// xorshift32: view-only, never in sim state, so the banned-API analyser does not apply here, but determinism still
/// matters for steady screenshots, hence a seeded integer generator instead of <see cref="Random"/>.
/// </summary>
internal static class LifeRandom
{
    /// <summary>Mixes an entity ID with a salt into a nonzero 32-bit seed.</summary>
    /// <param name="id">The entity to seed from (a house for walkers).</param>
    /// <param name="salt">A per-use salt, so one entity gives independent streams.</param>
    /// <returns>A nonzero seed.</returns>
    public static uint Seed(EntityId id, uint salt)
    {
        uint hash = (uint)id.Value * 2654435761u ^ (salt * 40503u) ^ 0x9E3779B9u;
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        return hash == 0 ? 0x2545F491u : hash;
    }

    /// <summary>Advances the generator one step.</summary>
    /// <param name="state">The generator state; updated in place.</param>
    /// <returns>The next raw value.</returns>
    public static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>Draws a value in [0, 1) and advances the generator.</summary>
    /// <param name="state">The generator state; updated in place.</param>
    /// <returns>A fraction of <see cref="uint.MaxValue"/>.</returns>
    public static float Unit(ref uint state) => Next(ref state) / (float)uint.MaxValue;

    /// <summary>Draws a value in [0, exclusive) and advances the generator.</summary>
    /// <param name="state">The generator state; updated in place.</param>
    /// <param name="exclusive">The exclusive upper bound; at least 1.</param>
    /// <returns>A value from 0 to <paramref name="exclusive"/> - 1.</returns>
    public static int Range(ref uint state, int exclusive) => (int)(Unit(ref state) * exclusive);
}
