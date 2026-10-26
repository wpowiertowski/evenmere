using Evenmere.Content;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Happenings;

/// <summary>
/// The content the happening tests run on: the minimal pack plus the <c>Pack/</c> overlay next to this file, which
/// adds four test happenings: <c>test_feast</c> (spring, always rolls: grant fish or a 10-day productivity bonus),
/// <c>test_request</c> (spring: a festival discount), <c>test_storm</c> (spring: a penalty paired with wood, or an
/// empty decline) and <c>test_winter_tale</c> (winter only, so it never rolls in spring tests).
/// </summary>
internal static class HappeningsPack
{
    private static readonly Lazy<ContentDb> _overlay = new(static () =>
        ContentLoader.Load([WorldBuilder.MinimalPackPath, OverlayPath]).GetDbOrThrow());

    /// <summary>Gets the folder of the overlay pack.</summary>
    public static string OverlayPath => Path.Combine(WorldBuilder.RepoRoot, "tests", "Evenmere.Sim.Tests", "Happenings", "Pack");

    /// <summary>Gets the minimal pack plus the overlay, loaded once.</summary>
    public static ContentDb Content => _overlay.Value;
}
