using System.Text.Json;
using Evenmere.Content;
using Evenmere.Core.Hashing;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using MessagePack;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// The save files (ADR-0007): <c>Write</c> stores the magic <c>EVMR</c>, the format version, a MessagePack header with
/// the meta and one string-ID table per content kind, then the state body, which is the same canonical bytes the state
/// hash uses, LZ4-compressed. <c>Read</c> runs the migration chain, remaps the IDs from the header's tables to the
/// current content and restores the simulation, reporting every remap, ruin and drop.
/// </summary>
public static class SaveGame
{
    private static readonly MessagePackSerializerOptions _bodyOptions =
        SimResolver.Options.WithCompression(MessagePackCompression.Lz4BlockArray);

    /// <summary>Writes a save.</summary>
    /// <param name="stream">The stream, left open.</param>
    /// <param name="simulation">The simulation to save; commands queued and not yet applied are not part of the save, so save between ticks.</param>
    /// <param name="meta">The content, game version and island ID the header stores.</param>
    /// <exception cref="ArgumentException"><paramref name="simulation"/> is not a simulation this code created.</exception>
    public static void Write(Stream stream, ISimulation simulation, SaveMeta meta)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(meta);

        if (simulation is not Simulation sim)
        {
            throw new ArgumentException("Only simulations created by this assembly can be saved.", nameof(simulation));
        }

        WorldState world = sim.World;
        byte[] canonical = StateSerializer.Write(world);

        var header = new SaveHeader
        {
            FormatVersion = SaveFormat.CurrentVersion,
            GameVersion = meta.GameVersion,
            ContentHash = meta.Content.ContentHash,
            Seed = world.Seed,
            MapId = meta.MapId,
            Tick = sim.CurrentTick,
            Era = sim.Query.Progression.CurrentEra.Value,
            Population = sim.Query.Population.TotalResidents,
            StateHash = Hash(canonical),
            Goods = [.. meta.Content.Goods.Names],
            Buildings = [.. meta.Content.Buildings.Names],
            Recipes = [.. meta.Content.Recipes.Names],
            Tiers = [.. meta.Content.Tiers.Names],
            Techs = [.. meta.Content.Techs.Names],
            Civics = [.. meta.Content.Civics.Names],
            Traditions = [.. meta.Content.Traditions.Names],
            Festivals = [.. meta.Content.Festivals.Names],
            GreatWorks = [.. meta.Content.GreatWorks.Names],
            Monuments = [.. meta.Content.Monuments.Names],
            Happenings = [.. meta.Content.Happenings.Names],
            Eras = [.. meta.Content.Eras.Names],
            Terrains = [.. meta.Content.Terrains.Names],
            CheatsUsed = sim.CheatsUsed,
        };

        byte[] headerBytes = MessagePackSerializer.Serialize(header, SimResolver.Options);
        byte[] body = MessagePackSerializer.Serialize(canonical, _bodyOptions);

        stream.Write(SaveFormat.Magic);
        Span<byte> version = stackalloc byte[2];
        version[0] = (byte)SaveFormat.CurrentVersion;
        version[1] = (byte)(SaveFormat.CurrentVersion >> 8);
        stream.Write(version);
        SaveFormat.WriteLengthPrefix(stream, headerBytes.Length);
        stream.Write(headerBytes, 0, headerBytes.Length);
        SaveFormat.WriteLengthPrefix(stream, body.Length);
        stream.Write(body, 0, body.Length);
    }

    /// <summary>Writes the debug variant: the same header and body as JSON text, for people, not for loading.</summary>
    /// <param name="stream">The stream, left open.</param>
    /// <param name="simulation">The simulation to save.</param>
    /// <param name="meta">The content, game version and island ID the header stores.</param>
    public static void WriteJson(Stream stream, ISimulation simulation, SaveMeta meta)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(meta);

        if (simulation is not Simulation sim)
        {
            throw new ArgumentException("Only simulations created by this assembly can be saved.", nameof(simulation));
        }

        WorldState world = sim.World;
        byte[] canonical = StateSerializer.Write(world);
        string bodyJson = MessagePackSerializer.ConvertToJson(canonical, SimResolver.Options);

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", "evmr-json-1");
        writer.WriteNumber("version", SaveFormat.CurrentVersion);
        writer.WriteString("gameVersion", meta.GameVersion);
        writer.WriteNumber("contentHash", meta.Content.ContentHash);
        writer.WriteNumber("seed", world.Seed);
        writer.WriteNumber("tick", sim.CurrentTick);
        writer.WriteNumber("era", sim.Query.Progression.CurrentEra.Value);
        writer.WriteNumber("population", sim.Query.Population.TotalResidents);
        writer.WriteNumber("stateHash", Hash(canonical));
        writer.WriteString("mapId", meta.MapId);
        WriteTable(writer, "goods", meta.Content.Goods.Names);
        WriteTable(writer, "buildings", meta.Content.Buildings.Names);
        WriteTable(writer, "recipes", meta.Content.Recipes.Names);
        WriteTable(writer, "tiers", meta.Content.Tiers.Names);
        WriteTable(writer, "techs", meta.Content.Techs.Names);
        WriteTable(writer, "civics", meta.Content.Civics.Names);
        WriteTable(writer, "traditions", meta.Content.Traditions.Names);
        WriteTable(writer, "festivals", meta.Content.Festivals.Names);
        WriteTable(writer, "greatWorks", meta.Content.GreatWorks.Names);
        WriteTable(writer, "monuments", meta.Content.Monuments.Names);
        WriteTable(writer, "happenings", meta.Content.Happenings.Names);
        WriteTable(writer, "eras", meta.Content.Eras.Names);
        WriteTable(writer, "terrains", meta.Content.Terrains.Names);
        writer.WriteBoolean("cheatsUsed", sim.CheatsUsed);
        writer.WritePropertyName("body");
        writer.WriteRawValue(bodyJson);
        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>Reads a save.</summary>
    /// <param name="stream">The save.</param>
    /// <param name="content">The current content; the save's IDs are remapped to it.</param>
    /// <returns>The restored simulation and what loading changed or dropped.</returns>
    /// <exception cref="InvalidDataException">The stream is not a save this version can load.</exception>
    public static (ISimulation Simulation, SaveReport Report) Read(Stream stream, ContentDb content)
    {
        (ISimulation simulation, SaveReport report, SaveTables _) = ReadInner(stream, content);
        return (simulation, report);
    }

    /// <summary>Reads a save and keeps its ID maps, for command-log replay.</summary>
    /// <param name="stream">The save.</param>
    /// <param name="content">The current content.</param>
    /// <returns>The restored simulation, the load report and the save's old-to-new ID maps.</returns>
    internal static (ISimulation Simulation, SaveReport Report, SaveTables Tables) ReadInner(Stream stream, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(content);

        (SaveHeader header, byte[] canonical, ushort version) = ReadSections(stream);
        if (Hash(canonical) != header.StateHash)
        {
            throw new InvalidDataException("The save's body does not match its stored state hash; the file is corrupt.");
        }

        var report = new SaveReport();
        if (header.ContentHash != content.ContentHash)
        {
            report.Notes.Add($"the save was written with content hash {header.ContentHash:x16}, but the content has {content.ContentHash:x16}");
        }

        var tables = SaveTables.Build(header, content);
        WorldState world;
        try
        {
            world = MessagePackSerializer.Deserialize<WorldState>(canonical, SimResolver.Options)
                ?? throw new InvalidDataException("The save holds no world state.");
        }
        catch (MessagePackSerializationException exception)
        {
            throw new InvalidDataException("The save's body is not a valid world state.", exception);
        }

        world = MigrationChain.Run(version, world, content, MigrationChain.All);
        if (!tables.IsIdentity)
        {
            RemapPass.Apply(world, tables, report);
            canonical = StateSerializer.Write(world);
        }

        Simulation simulation;
        try
        {
            simulation = Simulation.Restore(content, canonical);
        }
        catch (MessagePackSerializationException exception)
        {
            throw new InvalidDataException("The remapped state is not a valid world state.", exception);
        }

        // The flag is permission, not state: it travels in the header and the runtime carries it from here on.
        simulation.CheatsUsed = header.CheatsUsed;

        return (simulation, report, tables);
    }

    /// <summary>
    /// Reads a save's cheats flag: parses the framing and the header only, with no content needed and without
    /// touching the body, so the load list can show it. Missing files and corrupt saves read as disabled.
    /// </summary>
    /// <param name="stream">The save.</param>
    /// <returns><see langword="true"/> when the save was written with cheats enabled.</returns>
    public static bool ReadCheatsUsed(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return ReadHeader(stream).Header.CheatsUsed;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Reads a save's state hash: parses the framing, decompresses the body and hashes it, with no content needed.</summary>
    /// <param name="stream">The save.</param>
    /// <returns>The state hash.</returns>
    /// <exception cref="InvalidDataException">The stream is not a save this version can read.</exception>
    public static ulong ReadStateHash(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        (_, byte[] canonical, _) = ReadSections(stream);
        return Hash(canonical);
    }

    private static (SaveHeader Header, byte[] Canonical, ushort Version) ReadSections(Stream stream)
    {
        (SaveHeader header, ushort version) = ReadHeader(stream);

        Span<byte> prefix = stackalloc byte[4];
        SaveFormat.ReadExactly(stream, prefix);
        int bodyLength = SaveFormat.ReadLengthPrefix(prefix);
        if (bodyLength < 0)
        {
            throw new InvalidDataException("The save holds a negative body length.");
        }

        byte[] body = new byte[bodyLength];
        SaveFormat.ReadExactly(stream, body);

        byte[] canonical;
        try
        {
            canonical = MessagePackSerializer.Deserialize<byte[]>(body, _bodyOptions)
                ?? throw new InvalidDataException("The save holds no body.");
        }
        catch (MessagePackSerializationException exception)
        {
            throw new InvalidDataException("The save's body is not a valid compressed state.", exception);
        }

        return (header, canonical, version);
    }

    private static (SaveHeader Header, ushort Version) ReadHeader(Stream stream)
    {
        Span<byte> magic = stackalloc byte[4];
        SaveFormat.ReadExactly(stream, magic);
        for (int i = 0; i < SaveFormat.Magic.Length; i++)
        {
            if (magic[i] != SaveFormat.Magic[i])
            {
                throw new InvalidDataException("The stream is not an Evenmere save: the magic EVMR is missing.");
            }
        }

        Span<byte> versionBytes = stackalloc byte[2];
        SaveFormat.ReadExactly(stream, versionBytes);
        ushort version = (ushort)(versionBytes[0] | (versionBytes[1] << 8));
        if (version > SaveFormat.CurrentVersion)
        {
            throw new InvalidDataException($"The save is version {version}, but this game reads up to version {SaveFormat.CurrentVersion}.");
        }

        // An older version with no migration fails in MigrationChain.Run; the framing is still readable.

        Span<byte> prefix = stackalloc byte[4];
        SaveFormat.ReadExactly(stream, prefix);
        int headerLength = SaveFormat.ReadLengthPrefix(prefix);
        if (headerLength < 0)
        {
            throw new InvalidDataException("The save holds a negative header length.");
        }

        byte[] headerBytes = new byte[headerLength];
        SaveFormat.ReadExactly(stream, headerBytes);
        SaveHeader header;
        try
        {
            header = MessagePackSerializer.Deserialize<SaveHeader>(headerBytes, SimResolver.Options)
                ?? throw new InvalidDataException("The save holds no header.");
        }
        catch (MessagePackSerializationException exception)
        {
            throw new InvalidDataException("The save's header is not a valid header.", exception);
        }

        if (header.FormatVersion != version)
        {
            throw new InvalidDataException($"The save's outer version is {version} but its header says {header.FormatVersion}.");
        }

        return (header, version);
    }

    private static void WriteTable(Utf8JsonWriter writer, string name, System.Collections.Immutable.ImmutableArray<string> names)
    {
        writer.WriteStartArray(name);
        foreach (string item in names)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }

    private static ulong Hash(byte[] canonical)
    {
        var hasher = new StableHasher();
        hasher.Add(canonical);
        return hasher.ToUInt64();
    }
}
