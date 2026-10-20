using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Progression;

/// <summary>
/// The content the progression and modifier tests run on. <see cref="Content"/> is the minimal pack plus a small overlay
/// (<c>Pack/</c> next to this file) that adds locked buildings, a locked tier, tradition and festival, great-work types and a
/// tech that has every effect kind. Its era <c>awakening</c> sorts before <c>founding</c> by name but comes after it by order,
/// so a test that wants "the first era" cannot pass by picking era ID 0. <see cref="Real"/> is the slice content under
/// <c>content/</c>.
/// </summary>
internal static class ProgressionPack
{
    private static readonly Lazy<ContentDb> _overlay = new(static () =>
        ContentLoader.Load([WorldBuilder.MinimalPackPath, OverlayPath]).GetDbOrThrow());

    private static readonly Lazy<ContentDb> _real = new(static () =>
        ContentLoader.Load(Path.Combine(WorldBuilder.RepoRoot, "content")).GetDbOrThrow());

    /// <summary>Gets the folder of the overlay pack.</summary>
    public static string OverlayPath => Path.Combine(WorldBuilder.RepoRoot, "tests", "Evenmere.Sim.Tests", "Progression", "Pack");

    /// <summary>Gets the minimal pack plus the overlay, loaded once.</summary>
    public static ContentDb Content => _overlay.Value;

    /// <summary>Gets the real slice content, loaded once.</summary>
    public static ContentDb Real => _real.Value;
}
