using Evenmere.Content;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// One step of the save format's migration chain: rewrites a world state from <see cref="FromVersion"/> to
/// <see cref="ToVersion"/>. Migrations run in order at load, before the ID remap, so each one sees the shape its
/// version wrote. The first step is <see cref="Migration_0001_to_0002"/>; later M2 additions that only append keys
/// ride version 2 with no new class (ADR-0018).
/// </summary>
public interface IMigration
{
    /// <summary>Gets the format version the migration reads.</summary>
    int FromVersion { get; }

    /// <summary>Gets the format version the migration writes.</summary>
    int ToVersion { get; }

    /// <summary>Rewrites a state from <see cref="FromVersion"/> to <see cref="ToVersion"/>.</summary>
    /// <param name="state">The state at <see cref="FromVersion"/>.</param>
    /// <param name="content">The current content, for sizing resized arrays.</param>
    /// <returns>The state at <see cref="ToVersion"/>.</returns>
    WorldState Apply(WorldState state, ContentDb content);
}
