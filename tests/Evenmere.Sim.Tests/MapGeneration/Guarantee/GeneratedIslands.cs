using System.Collections.Concurrent;
using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;

namespace Evenmere.Sim.Tests.MapGeneration.Guarantee;

/// <summary>Process-wide memo of finished islands: generates each (seed, size) pair once per test run and returns
/// the same immutable <c>MapDef</c> to every test. Generation takes 0.5–2.3 s, and many tests regenerate the same
/// pairs (goldens, regions, discoveries, the guarantee subset), so sharing cuts minutes. Thread-safe for xUnit's
/// parallel classes: one <c>Lazy</c> per key with publication safety, so a pair generates at most once even raced.
/// Do not use for determinism tests (TwoRuns), which must generate twice for real.</summary>
internal static class GeneratedIslands
{
    private static readonly ConcurrentDictionary<(int Seed, int Size), Lazy<MapDef>> _cache = new();

    /// <summary>Gets the finished island for the seed and size, generating it once per test run.</summary>
    /// <param name="seed">The generator seed.</param>
    /// <param name="size">The width and height in tiles.</param>
    /// <returns>The same immutable island on every call with the same arguments.</returns>
    internal static MapDef Get(int seed, int size) =>
        _cache.GetOrAdd((seed, size), key =>
            new Lazy<MapDef>(
                () => IslandGenerator.Generate(key.Seed, key.Size, GeneratedIslandFixture.Content),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}
