using Evenmere.Core.Time;
using Evenmere.Sim.Persistence;

namespace Evenmere.Game.Host.Persistence;

/// <summary>
/// Writes and loads the game's save slots: the <c>.evsave</c> beside its <c>.evlog</c>, plus the <c>.evmeta</c>
/// sidecar the slot list shows. Every write is atomic (temporary file plus rename), and writing or loading a slot
/// hands its fresh, empty log to the <see cref="CommandLogRecorder"/>, so the log always holds exactly the commands
/// made after that save. Saves happen between ticks (the menus call this from <c>_Process</c>), never mid-tick.
/// </summary>
public static class SaveService
{
    private static readonly string _gameVersion =
        typeof(SaveService).Assembly.GetName().Version?.ToString(3) ?? "0.0";

    /// <summary>How many buildings a single confirmed demolish must remove before the game takes a safety autosave.</summary>
    public const int BulkDemolishAutosaveThreshold = 10;

    /// <summary>Gets the game version text stored in each save header.</summary>
    public static string GameVersion => _gameVersion;

    /// <summary>
    /// Saves the running simulation into a slot: writes the <c>.evsave</c> atomically, refreshes the sidecar, and
    /// starts a fresh, empty command log for the slot.
    /// </summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="recorder">The command log recorder, rebound to the slot's fresh log.</param>
    /// <param name="slotId">The file-safe slot ID.</param>
    /// <param name="displayName">The display name stored in the sidecar.</param>
    /// <returns>The state hash of the saved world, as 16 lowercase hex digits.</returns>
    /// <exception cref="InvalidOperationException">No simulation is running.</exception>
    public static string Save(SimHost host, CommandLogRecorder recorder, string slotId, string displayName)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentException.ThrowIfNullOrEmpty(slotId);
        ArgumentNullException.ThrowIfNull(displayName);

        if (!host.IsRunning)
        {
            throw new InvalidOperationException("No simulation is running; there is nothing to save.");
        }

        if (slotId == SaveSlots.AutosaveSlot)
        {
            // The periodic autosave rotates the slots first, so the fresh save always lands in the autosave slot and
            // the two previous generations survive beside it. Manual saves into other slots never rotate.
            SaveSlots.RotateAutosaves();
        }

        string savePath = SaveSlots.SavePath(slotId);
        SaveSlots.WriteFileAtomic(savePath, stream =>
            SaveGame.Write(stream, host.Simulation, new SaveMeta(host.Content, GameVersion, host.MapId)));

        ulong hash = host.ComputeStateHash();
        var clock = new GameClock(host.CurrentTick);
        SaveSlots.WriteMeta(SaveSlots.MetaPath(slotId), new SaveSlotMeta
        {
            Name = displayName,
            Tick = host.CurrentTick,
            Day = clock.Day,
            Season = (int)clock.Season,
            Year = clock.Year,
            Era = host.Content.Eras.NameOf(host.Query.Progression.CurrentEra),
            Seed = host.Seed,
            MapId = host.MapId,
            StateHash = $"{hash:x16}",
            SavedAtUtc = DateTime.UtcNow,
        });

        recorder.BeginSlot(SaveSlots.LogPath(slotId));
        return $"{hash:x16}";
    }

    /// <summary>
    /// Tests whether a confirmed demolish should take a safety autosave first: more than
    /// <see cref="BulkDemolishAutosaveThreshold"/> buildings at once (road tiles do not count).
    /// </summary>
    /// <param name="buildingCount">The confirmed buildings.</param>
    /// <returns><see langword="true"/> when the demolish deserves a safety autosave.</returns>
    public static bool NeedsPreDemolishAutosave(int buildingCount) => buildingCount > BulkDemolishAutosaveThreshold;

    /// <summary>
    /// Takes a safety autosave before a bulk demolish when <paramref name="buildingCount"/> is over the threshold;
    /// otherwise does nothing and returns <see langword="null"/>. The save joins the normal autosave rotation.
    /// </summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="recorder">The command log recorder, rebound to the fresh log.</param>
    /// <param name="buildingCount">The confirmed buildings.</param>
    /// <param name="displayName">The display name stored in the sidecar.</param>
    /// <returns>The state hash of the saved world, or <see langword="null"/> when no save was taken.</returns>
    public static string? AutosaveBeforeBulkDemolish(SimHost host, CommandLogRecorder recorder, int buildingCount, string displayName)
    {
        if (!NeedsPreDemolishAutosave(buildingCount))
        {
            return null;
        }

        return Save(host, recorder, SaveSlots.AutosaveSlot, displayName);
    }

    /// <summary>
    /// Loads a slot's save, replacing the running simulation, and starts a fresh, empty command log for the slot.
    /// </summary>
    /// <param name="host">The simulation host.</param>
    /// <param name="recorder">The command log recorder, rebound to the slot's fresh log.</param>
    /// <param name="slotId">The file-safe slot ID.</param>
    /// <returns>The state hash of the loaded world and what loading changed or dropped to fit the current content.</returns>
    public static (string Hash, SaveReport Report) Load(SimHost host, CommandLogRecorder recorder, string slotId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentException.ThrowIfNullOrEmpty(slotId);

        SaveReport report = host.LoadGame(SaveSlots.SavePath(slotId));
        recorder.BeginSlot(SaveSlots.LogPath(slotId));
        return ($"{host.ComputeStateHash():x16}", report);
    }

    /// <summary>
    /// Reads a save's cheats flag straight from the file, for the load list: only the header is parsed, never the body.
    /// Saves written before the flag existed read as disabled.
    /// </summary>
    /// <param name="slotId">The file-safe slot ID.</param>
    /// <returns><see langword="true"/> when the save was written with cheats enabled.</returns>
    public static bool ReadCheatsUsed(string slotId)
    {
        ArgumentException.ThrowIfNullOrEmpty(slotId);
        using FileStream stream = File.OpenRead(SaveSlots.SavePath(slotId));
        return SaveGame.ReadCheatsUsed(stream);
    }

    /// <summary>
    /// Reads a save's state hash straight from the file, with no content needed, for the debug label's identity check.
    /// </summary>
    /// <param name="slotId">The file-safe slot ID.</param>
    /// <returns>The state hash, as 16 lowercase hex digits.</returns>
    public static string ReadStateHash(string slotId)
    {
        ArgumentException.ThrowIfNullOrEmpty(slotId);
        using FileStream stream = File.OpenRead(SaveSlots.SavePath(slotId));
        return $"{SaveGame.ReadStateHash(stream):x16}";
    }
}
