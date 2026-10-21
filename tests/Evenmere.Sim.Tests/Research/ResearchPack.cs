using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Research;

/// <summary>
/// The content the research and culture tests run on: the minimal pack plus the <c>Pack/</c> overlay next to this file.
/// The overlay adds a small tech chain (<c>test_theory</c> at 60 knowledge, <c>test_practice</c> at 40 behind it,
/// <c>test_mastery</c> at 30 behind that, and the era-2 <c>test_future</c>), the independent <c>test_lore</c>, a matching
/// civic chain plus the independent <c>test_tales</c>, and a <c>test_hall</c> with knowledge and culture outputs and a
/// workforce, so building credits can be fill-scaled.
/// </summary>
internal static class ResearchPack
{
    private static readonly Lazy<ContentDb> _overlay = new(static () =>
        ContentLoader.Load([WorldBuilder.MinimalPackPath, OverlayPath]).GetDbOrThrow());

    /// <summary>Gets the folder of the overlay pack.</summary>
    public static string OverlayPath => Path.Combine(WorldBuilder.RepoRoot, "tests", "Evenmere.Sim.Tests", "Research", "Pack");

    /// <summary>Gets the minimal pack plus the overlay, loaded once.</summary>
    public static ContentDb Content => _overlay.Value;
}
