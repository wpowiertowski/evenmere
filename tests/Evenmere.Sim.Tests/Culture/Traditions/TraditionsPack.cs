using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Culture.Traditions;

/// <summary>
/// The content the tradition tests run on: the minimal pack plus the <c>Pack/</c> overlay next to this file. The
/// overlay adds a Wild card (<c>test_wild_song</c>, island-wide +10% productivity) and two style cards
/// (<c>test_style_a</c>, <c>test_style_b</c>). The minimal pack already carries a Hearth card (<c>common_hearth</c>,
/// Settlers capacity +15%) and the <c>village_moot</c> civic, whose effects the harness applies to arrange unlocks and
/// slots without adopting anything for real.
/// </summary>
internal static class TraditionsPack
{
    private static readonly Lazy<ContentDb> _overlay = new(static () =>
        ContentLoader.Load([WorldBuilder.MinimalPackPath, OverlayPath]).GetDbOrThrow());

    /// <summary>Gets the folder of the overlay pack.</summary>
    public static string OverlayPath => Path.Combine(WorldBuilder.RepoRoot, "tests", "Evenmere.Sim.Tests", "Culture", "Traditions", "Pack");

    /// <summary>Gets the minimal pack plus the overlay, loaded once.</summary>
    public static ContentDb Content => _overlay.Value;
}
