using System.Text.Json;
using Godot;

namespace Evenmere.Game.Host.Persistence;

/// <summary>
/// One save slot on disk: the display name, the game-time the save was written at and where that save file lives.
/// Slots come from the <c>.evmeta</c> sidecar written beside each save; a save without a sidecar still lists, with
/// the slot ID as its name and unknown details, so older or hand-placed saves stay loadable.
/// </summary>
/// <param name="SlotId">The file-safe slot ID (the save file name without its extension).</param>
/// <param name="Name">The display name shown in the slot list.</param>
/// <param name="Tick">The tick the save was written at, or -1 when unknown.</param>
/// <param name="Day">Whole days since the world began, or -1 when unknown.</param>
/// <param name="Season">The season index (0 spring to 3 winter), or -1 when unknown.</param>
/// <param name="Year">Whole years since the world began, or -1 when unknown.</param>
/// <param name="Era">The era's string ID, or empty when unknown.</param>
/// <param name="Seed">The world seed, or 0 when unknown.</param>
/// <param name="MapId">The island ID, or empty when unknown.</param>
/// <param name="StateHash">The state hash as 16 lowercase hex digits, or empty when unknown.</param>
/// <param name="LastWrittenUtc">When the save file was last written, for "most recent save" ordering.</param>
public sealed record SaveSlotInfo(
    string SlotId,
    string Name,
    long Tick,
    long Day,
    int Season,
    long Year,
    string Era,
    ulong Seed,
    string MapId,
    string StateHash,
    DateTime LastWrittenUtc);

/// <summary>
/// The JSON sidecar (<c>.evmeta</c>) written beside each save with the list metadata the slot list shows without
/// loading the save itself: the display name, the calendar, the era, the seed and island (for future use), and the
/// state hash for the debug label. Thumbnails come later (M2-15 owns save compat next).
/// </summary>
public sealed class SaveSlotMeta
{
    /// <summary>Gets or sets the display name shown in the slot list.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the tick the save was written at.</summary>
    public long Tick { get; set; }

    /// <summary>Gets or sets whole days since the world began.</summary>
    public long Day { get; set; }

    /// <summary>Gets or sets the season index (0 spring to 3 winter).</summary>
    public int Season { get; set; }

    /// <summary>Gets or sets whole years since the world began.</summary>
    public long Year { get; set; }

    /// <summary>Gets or sets the era's string ID.</summary>
    public string Era { get; set; } = string.Empty;

    /// <summary>Gets or sets the world seed.</summary>
    public ulong Seed { get; set; }

    /// <summary>Gets or sets the island ID.</summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>Gets or sets the state hash as 16 lowercase hex digits.</summary>
    public string StateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets when the save was written, in UTC.</summary>
    public DateTime SavedAtUtc { get; set; }
}

/// <summary>
/// The save slots in <c>user://saves/</c>: <c>&lt;slot&gt;.evsave</c> plus <c>&lt;slot&gt;.evlog</c>, with a small
/// JSON sidecar (<c>&lt;slot&gt;.evmeta</c>) carrying the list metadata. All writes go to a temporary file in the
/// same folder followed by a rename, so a crash never leaves a half-written save behind.
/// </summary>
public static class SaveSlots
{
    /// <summary>The save file extension.</summary>
    public const string SaveExtension = ".evsave";

    /// <summary>The command-log file extension.</summary>
    public const string LogExtension = ".evlog";

    /// <summary>The list-metadata sidecar extension.</summary>
    public const string MetaExtension = ".evmeta";

    /// <summary>The slot the autosave writes to.</summary>
    public const string AutosaveSlot = "autosave";

    private const int MaxSlotIdLength = 48;

    /// <summary>Gets the absolute saves folder, creating it on first use.</summary>
    /// <returns>The folder path.</returns>
    public static string SavesDirectory
    {
        get
        {
            string directory = ProjectSettings.GlobalizePath("user://saves");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <summary>Gets the save path of a slot.</summary>
    /// <param name="slotId">The file-safe slot ID.</param>
    /// <returns>The absolute <c>.evsave</c> path.</returns>
    public static string SavePath(string slotId) => Path.Combine(SavesDirectory, slotId + SaveExtension);

    /// <summary>Gets the command-log path of a slot.</summary>
    /// <param name="slotId">The file-safe slot ID.</param>
    /// <returns>The absolute <c>.evlog</c> path.</returns>
    public static string LogPath(string slotId) => Path.Combine(SavesDirectory, slotId + LogExtension);

    /// <summary>Gets the sidecar path of a slot.</summary>
    /// <param name="slotId">The file-safe slot ID.</param>
    /// <returns>The absolute <c>.evmeta</c> path.</returns>
    public static string MetaPath(string slotId) => Path.Combine(SavesDirectory, slotId + MetaExtension);

    /// <summary>Gets the slot ID of a save path (the file name without its extension).</summary>
    /// <param name="savePath">A <c>.evsave</c> path.</param>
    /// <returns>The slot ID.</returns>
    public static string SlotIdOf(string savePath) => Path.GetFileNameWithoutExtension(savePath);

    /// <summary>
    /// Lists every slot with a save file, most recently written first. The metadata comes from the sidecar; a save
    /// without one still lists, with the slot ID as its name and unknown details.
    /// </summary>
    /// <returns>The slots, in file-time order (ties broken by slot ID).</returns>
    public static IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        string directory = SavesDirectory;
        string[] saves;
        try
        {
            saves = Directory.GetFiles(directory, "*" + SaveExtension);
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        var slots = new List<SaveSlotInfo>(saves.Length);
        foreach (string save in saves)
        {
            string slotId = SlotIdOf(save);
            DateTime written = File.GetLastWriteTimeUtc(save);
            SaveSlotMeta? meta = ReadMeta(MetaPath(slotId));
            slots.Add(meta is null
                ? new SaveSlotInfo(slotId, slotId, -1, -1, -1, -1, string.Empty, 0, string.Empty, string.Empty, written)
                : new SaveSlotInfo(slotId, meta.Name, meta.Tick, meta.Day, meta.Season, meta.Year, meta.Era, meta.Seed, meta.MapId, meta.StateHash, written));
        }

        slots.Sort(static (a, b) =>
        {
            int order = b.LastWrittenUtc.CompareTo(a.LastWrittenUtc);
            return order != 0 ? order : string.Compare(a.SlotId, b.SlotId, StringComparison.Ordinal);
        });
        return slots;
    }

    /// <summary>Reads a sidecar file.</summary>
    /// <param name="metaPath">The <c>.evmeta</c> path.</param>
    /// <returns>The metadata, or <see langword="null"/> when the file is missing or unreadable.</returns>
    public static SaveSlotMeta? ReadMeta(string metaPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(metaPath);
            return JsonSerializer.Deserialize<SaveSlotMeta>(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes a sidecar file, atomically.</summary>
    /// <param name="metaPath">The <c>.evmeta</c> path.</param>
    /// <param name="meta">The metadata.</param>
    public static void WriteMeta(string metaPath, SaveSlotMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        WriteFileAtomic(metaPath, stream => JsonSerializer.Serialize(stream, meta));
    }

    /// <summary>
    /// Writes a file atomically: to a temporary file in the same folder, then renamed over the target, so a crash
    /// never leaves a half-written file where a whole one is expected.
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <param name="write">Fills the temporary stream; the stream is closed before the rename.</param>
    public static void WriteFileAtomic(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? SavesDirectory;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None))
            {
                write(stream);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // The rename already took the file, or nothing was written: either way there is nothing to clean.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above: best effort cleanup only.
            }
        }
    }

    /// <summary>Makes a file-safe slot ID from a display name: lowercase letters, digits, dashes and underscores.</summary>
    /// <param name="name">The display name.</param>
    /// <returns>The sanitized ID, or <c>island</c> when nothing usable remains.</returns>
    public static string SanitizeSlotId(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<char> buffer = stackalloc char[Math.Min(name.Length, MaxSlotIdLength)];
        int used = 0;
        foreach (char rune in name.ToLowerInvariant())
        {
            if (used >= buffer.Length)
            {
                break;
            }

            buffer[used] = rune is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' ? rune : '-';
            used++;
        }

        string sanitized = new string(buffer[..used]).Trim('-', '_');
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized.Length == 0 ? "island" : sanitized;
    }

    /// <summary>Makes a slot ID unique among existing slots by appending a number.</summary>
    /// <param name="baseId">The preferred ID.</param>
    /// <param name="exists">Tests whether an ID is taken.</param>
    /// <returns>An ID no existing slot uses.</returns>
    public static string UniqueSlotId(string baseId, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseId);
        ArgumentNullException.ThrowIfNull(exists);

        if (!exists(baseId))
        {
            return baseId;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{baseId}-{suffix}";
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }
}
