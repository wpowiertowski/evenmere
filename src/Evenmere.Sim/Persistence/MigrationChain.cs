using Evenmere.Content;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// Runs the migration chain from a save's version up to the current format. Each step hands its output to the next,
/// so a save from any older version lands in the current shape before the ID remap runs.
/// </summary>
public static class MigrationChain
{
    /// <summary>Gets the migrations the game ships, in no particular order; empty until the first format change.</summary>
    public static IReadOnlyList<IMigration> All { get; } = [];

    /// <summary>Runs the chain from a version up to the current format.</summary>
    /// <param name="fromVersion">The save's format version.</param>
    /// <param name="state">The deserialised state.</param>
    /// <param name="content">The current content.</param>
    /// <param name="migrations">The steps to run; <see cref="All"/> at load, fakes in tests.</param>
    /// <returns>The state at <see cref="SaveFormat.CurrentVersion"/>.</returns>
    /// <exception cref="InvalidDataException">No step leaves <paramref name="fromVersion"/>, or two steps do.</exception>
    public static WorldState Run(int fromVersion, WorldState state, ContentDb content, IEnumerable<IMigration> migrations) =>
        Run(fromVersion, SaveFormat.CurrentVersion, state, content, migrations);

    /// <summary>Runs the chain from a version up to a target version. Used by tests; loads always target the current format.</summary>
    /// <param name="fromVersion">The save's format version.</param>
    /// <param name="toVersion">The version to stop at.</param>
    /// <param name="state">The deserialised state.</param>
    /// <param name="content">The current content.</param>
    /// <param name="migrations">The steps to run.</param>
    /// <returns>The state at <paramref name="toVersion"/>.</returns>
    internal static WorldState Run(int fromVersion, int toVersion, WorldState state, ContentDb content, IEnumerable<IMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(migrations);

        var byFrom = new SortedDictionary<int, IMigration>();
        foreach (IMigration migration in migrations)
        {
            if (!byFrom.TryAdd(migration.FromVersion, migration))
            {
                throw new InvalidDataException($"Two migrations leave version {migration.FromVersion}.");
            }
        }

        int version = fromVersion;
        while (version < toVersion)
        {
            if (!byFrom.TryGetValue(version, out IMigration? migration))
            {
                throw new InvalidDataException($"No migration leaves version {version}; the save cannot be loaded.");
            }

            state = migration.Apply(state, content);
            ArgumentNullException.ThrowIfNull(state, nameof(state));
            version = migration.ToVersion;
            if (version <= migration.FromVersion)
            {
                throw new InvalidDataException($"The migration from version {migration.FromVersion} does not move forward.");
            }
        }

        return state;
    }
}
