using Evenmere.Content;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Persistence;

// The version numbers in the name are the ADR-0018 convention (one class per format step); CA1707's
// no-underscores rule does not apply to a name whose underscores carry the version.
#pragma warning disable CA1707
/// <summary>
/// The version-1 to version-2 format step (ADR-0018): the first M2 state additions ride version 2, so this step grows
/// every content-sized array to the loaded content with the same rules as each state's own sizing, and leaves every
/// scalar at its load-safe default. The content-ID remap in <see cref="RemapPass"/> runs after this step at load, so
/// new content-indexed members are remapped by the same pass as the old ones; later M2 state additions that only
/// append keys ride version 2 with no new class.
/// </summary>
public sealed class Migration_0001_to_0002 : IMigration
{
    /// <summary>Gets the format version this migration reads: 1.</summary>
    public int FromVersion => 1;

    /// <summary>Gets the format version this migration writes: 2.</summary>
    public int ToVersion => 2;

    /// <summary>
    /// Rewrites a version-1 state to version 2: grows the content-sized arrays to the loaded content, keeping every
    /// value at its save ID. The remap pass permutes them to the current content afterwards.
    /// </summary>
    /// <param name="state">The state at version 1.</param>
    /// <param name="content">The current content, for sizing the grown arrays.</param>
    /// <returns>The state at version 2.</returns>
    /// <exception cref="InvalidDataException">A required member is missing from the version-1 state.</exception>
    public WorldState Apply(WorldState state, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);

        // A damaged file can leave members nil; the load path reports those as data errors, not null references.
        if (state.Research is null || state.Culture is null || state.Advisors is null)
        {
            throw new InvalidDataException("The version-1 state is missing a required member.");
        }

        state.Research.EnsureSlots(content.Techs.Count);
        state.Culture.EnsureSlots(content.Civics.Count, content.Festivals.Count);
        state.Advisors.EnsureSlots(content.Goods.Count, content.Tiers.Count);
        return state;
    }
}
