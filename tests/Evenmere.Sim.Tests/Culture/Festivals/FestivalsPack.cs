using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Culture.Festivals;

/// <summary>
/// The content the festival tests run on: a base pack plus the <c>Pack/</c> overlay next to this file, which adds the
/// <c>test_festival_ground</c> building (tagged <c>festival_ground</c>), the any-season <c>test_harvest</c> festival
/// (5 days, 5 culture a day, autumn ×1.5, settlers arrive 50% faster) and the winter-only <c>test_winter</c> festival.
/// Minimal keeps the command and scheduler tests light; the slice base feeds the population integration test.
/// </summary>
internal static class FestivalsPack
{
    private static readonly Lazy<ContentDb> _minimal = new(static () =>
        ContentLoader.Load([WorldBuilder.MinimalPackPath, OverlayPath]).GetDbOrThrow());

    private static readonly Lazy<ContentDb> _slice = new(static () =>
        ContentLoader.Load([Path.Combine(WorldBuilder.RepoRoot, "content"), OverlayPath]).GetDbOrThrow());

    /// <summary>Gets the folder of the overlay pack.</summary>
    public static string OverlayPath => Path.Combine(WorldBuilder.RepoRoot, "tests", "Evenmere.Sim.Tests", "Culture", "Festivals", "Pack");

    /// <summary>Gets the minimal pack plus the overlay.</summary>
    public static ContentDb Minimal => _minimal.Value;

    /// <summary>Gets the real slice content plus the overlay.</summary>
    public static ContentDb Slice => _slice.Value;
}
