using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Sim.Systems.Debug.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Production.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Systems.Workforce.Commands;
using MessagePack;
using MessagePack.Resolvers;

namespace Evenmere.Sim.Persistence;

/// <summary>
/// Appends (tick, command) pairs to a <c>.evlog</c> file next to a save, so any session is replayable. Each entry is a
/// length-prefixed MessagePack array of the tick, the command's simple type name and the command itself, serialised
/// with the contractless resolver (command records are plain records, not state, so their bytes are not hashed).
/// Content IDs in commands are the save's raw values; <see cref="CommandLogReader"/> maps them on read.
/// </summary>
public sealed class CommandLogWriter : IDisposable
{
    private readonly Stream _stream;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="CommandLogWriter"/> class over a stream.</summary>
    /// <param name="stream">The stream the entries are appended to; the writer never closes it.</param>
    public CommandLogWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    /// <summary>Appends one command with the tick it was enqueued for.</summary>
    /// <param name="tick">The tick the command is applied on (enqueue before ticking from <c>tick - 1</c> to <c>tick</c>).</param>
    /// <param name="command">The command.</param>
    public void Append(long tick, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] payload = MessagePackSerializer.Serialize<Command?>(command, CommandLogOptions.Payload);
        var entry = new LogEntry(tick, command.GetType().Name, payload);
        byte[] bytes = MessagePackSerializer.Serialize(entry, CommandLogOptions.Envelope);
        Span<byte> prefix = stackalloc byte[4];
        prefix[0] = (byte)bytes.Length;
        prefix[1] = (byte)(bytes.Length >> 8);
        prefix[2] = (byte)(bytes.Length >> 16);
        prefix[3] = (byte)(bytes.Length >> 24);
        _stream.Write(prefix);
        _stream.Write(bytes, 0, bytes.Length);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Reads a <see cref="CommandLogWriter"/> file back, mapping the commands' content IDs from the save's tables to the
/// current content's. A command that names an item the content no longer has cannot be applied faithfully (its raw ID
/// would point at a different item), so it is dropped.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="ForContent"/> when the log was recorded against the same content objects given here, such as the
/// smoke fixture replayed by the game: it builds identity tables internally, so the raw IDs are already current.
/// A log from different content (an older save, a remapped world) must go through the save's own tables instead,
/// as <see cref="Replay"/> does with <c>SaveGame.ReadInner</c>; reading it here would silently point at the wrong items.
/// </para>
/// </remarks>
public sealed class CommandLogReader
{
    private readonly Stream _stream;
    private readonly SaveTables _tables;

    /// <summary>Initializes a new instance of the <see cref="CommandLogReader"/> class.</summary>
    /// <param name="stream">The stream the entries are read from; the reader never closes it.</param>
    /// <param name="tables">The save's old-to-new ID maps.</param>
    public CommandLogReader(Stream stream, SaveTables tables)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tables);
        _stream = stream;
        _tables = tables;
    }

    /// <summary>
    /// Reads a log recorded against the same content objects given here, building identity tables internally.
    /// A log from different content must go through the save's own tables instead, as <see cref="Replay"/> does.
    /// </summary>
    /// <param name="log">The log stream; the reader never closes it.</param>
    /// <param name="content">The content the log was recorded with.</param>
    /// <returns>The (tick, command) pairs in file order.</returns>
    /// <exception cref="InvalidDataException">An entry is malformed or names an unknown command type.</exception>
    public static IReadOnlyList<(long Tick, Command Command)> ForContent(Stream log, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(content);
        return new CommandLogReader(log, SaveTables.Identity(content)).ReadAll();
    }

    /// <summary>Reads every entry, remapping content IDs and dropping commands about gone items.</summary>
    /// <returns>The (tick, command) pairs in file order.</returns>
    /// <exception cref="InvalidDataException">An entry is malformed or names an unknown command type.</exception>
    public IReadOnlyList<(long Tick, Command Command)> ReadAll()
    {
        var entries = new List<(long Tick, Command Command)>();
        Span<byte> prefix = stackalloc byte[4];
        while (true)
        {
            int read = 0;
            while (read < prefix.Length)
            {
                int chunk = _stream.Read(prefix[read..]);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            if (read == 0)
            {
                return entries;
            }

            if (read < prefix.Length)
            {
                throw new InvalidDataException("The command log ends in the middle of an entry.");
            }

            int length = prefix[0] | (prefix[1] << 8) | (prefix[2] << 16) | (prefix[3] << 24);
            if (length < 0 || length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("The command log holds an entry length that is not a modest positive size.");
            }

            byte[] bytes = new byte[length];
            SaveFormat.ReadExactly(_stream, bytes);
            LogEntry entry;
            try
            {
                entry = MessagePackSerializer.Deserialize<LogEntry>(bytes, CommandLogOptions.Envelope);
            }
            catch (MessagePackSerializationException exception)
            {
                throw new InvalidDataException("A command log entry is not a valid entry.", exception);
            }

            if (!CommandLogOptions.TryGetCommandType(entry.Type, out _))
            {
                throw new InvalidDataException($"The command log names an unknown command type '{entry.Type}'.");
            }

            Command command;
            try
            {
                command = MessagePackSerializer.Deserialize<Command?>(entry.Payload, CommandLogOptions.Payload)
                    ?? throw new InvalidDataException($"The command log entry for '{entry.Type}' holds no command.");
            }
            catch (MessagePackSerializationException exception)
            {
                throw new InvalidDataException($"The command log entry for '{entry.Type}' is not a valid command.", exception);
            }

            if (!string.Equals(command.GetType().Name, entry.Type, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The command log entry for '{entry.Type}' holds a {command.GetType().Name}.");
            }

            if (RemapCommand(command) is { } remapped)
            {
                entries.Add((entry.Tick, remapped));
            }
        }
    }

    private Command? RemapCommand(Command command)
    {
        switch (command)
        {
            case PlaceBuilding place:
                {
                    int type = _tables.MapBuilding(place.Type.Value);
                    return type < 0 ? null : place with { Type = BuildingTypeId.From(checked((ushort)type)) };
                }

            case UpgradeBuilding upgrade:
                {
                    int type = _tables.MapBuilding(upgrade.ToType.Value);
                    return type < 0 ? null : upgrade with { ToType = BuildingTypeId.From(checked((ushort)type)) };
                }

            case Demolish:
            case SetBuildingPaused:
            case SetWorkforcePriority:
            case SetHouseUpgradeLock:
            case DismissAdvice:
                return command;

            case PlaceRoad road:
                {
                    int type = _tables.MapBuilding(road.RoadType.Value);
                    return type < 0 ? null : road with { RoadType = BuildingTypeId.From(checked((ushort)type)) };
                }

            case RemoveRoad:
                return command;

            case SetResearchTarget target:
                {
                    int tech = _tables.MapTech(target.Tech.Value);
                    return tech < 0 ? null : target with { Tech = TechId.From(checked((ushort)tech)) };
                }

            case SetResearchQueue queue:
                {
                    var kept = new List<TechId>(queue.Queue.Length);
                    foreach (TechId tech in queue.Queue)
                    {
                        int mapped = _tables.MapTech(tech.Value);
                        if (mapped >= 0)
                        {
                            kept.Add(TechId.From(checked((ushort)mapped)));
                        }
                    }

                    return queue with { Queue = [.. kept] };
                }

            case SetCivicTarget target:
                {
                    int civic = _tables.MapCivic(target.Civic.Value);
                    return civic < 0 ? null : target with { Civic = CivicId.From(checked((ushort)civic)) };
                }

            case SetCivicQueue queue:
                {
                    var kept = new List<CivicId>(queue.Queue.Length);
                    foreach (CivicId civic in queue.Queue)
                    {
                        int mapped = _tables.MapCivic(civic.Value);
                        if (mapped >= 0)
                        {
                            kept.Add(CivicId.From(checked((ushort)mapped)));
                        }
                    }

                    return queue with { Queue = [.. kept] };
                }

            case SetTradition tradition:
                {
                    if (tradition.Tradition is not { } card)
                    {
                        return tradition;
                    }

                    int mapped = _tables.MapTradition(card.Value);
                    return mapped < 0 ? null : tradition with { Tradition = TraditionId.From(checked((ushort)mapped)) };
                }

            case CheatGrantGoods grant:
                {
                    int good = _tables.MapGood(grant.Good.Value);
                    return good < 0 ? null : grant with { Good = GoodId.From(checked((ushort)good)) };
                }

            case CheatCompleteNode complete:
                {
                    // A completion names exactly one node (the handler enforces it); an empty command passes
                    // through and is rejected at apply time instead of silently dropped here.
                    TechId? tech = complete.Tech;
                    if (tech is not null)
                    {
                        int mapped = _tables.MapTech(tech.Value.Value);
                        if (mapped < 0)
                        {
                            return null;
                        }

                        tech = TechId.From(checked((ushort)mapped));
                    }

                    CivicId? civic = complete.Civic;
                    if (civic is not null)
                    {
                        int mapped = _tables.MapCivic(civic.Value.Value);
                        if (mapped < 0)
                        {
                            return null;
                        }

                        civic = CivicId.From(checked((ushort)mapped));
                    }

                    return complete with { Tech = tech, Civic = civic };
                }

            case CheatSetEra era:
                {
                    int mapped = _tables.MapEra(era.Era.Value);
                    return mapped < 0 ? null : era with { Era = EraId.From(checked((ushort)mapped)) };
                }

            case CheatRevealAll:
            case CheatAddResidents:
                // Region numbers are map data, not content IDs, and a headcount names nothing at all.
                return command;

            default:
                // TODO(M2-13b): add the HoldFestival remap case (map the festival, drop the command when it is
                // gone) and the ChooseHappeningOption remap case (map the happening, drop the command when it is
                // gone) once festival and happening content exist, and name real items in the coverage samples.
                // Until then the samples ride this passthrough, because a mapping case would drop them under
                // festival- and happening-free content.
                // A command from another assembly (a test harness command, for example) carries no known content
                // IDs; it passes through unchanged.
                return command;
        }
    }
}

/// <summary>Replays a session: loads a save, feeds it the logged commands and ticks to a tick.</summary>
public static class Replay
{
    /// <summary>Loads a save and replays a command log through it.</summary>
    /// <param name="save">The save, at or before <paramref name="untilTick"/>.</param>
    /// <param name="log">The command log recorded with the save's content.</param>
    /// <param name="content">The current content.</param>
    /// <param name="untilTick">The tick to stop at; at least the save's tick.</param>
    /// <returns>The state hash at <paramref name="untilTick"/>.</returns>
    public static ulong Run(Stream save, Stream log, ContentDb content, long untilTick)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(untilTick);

        (ISimulation simulation, SaveReport _, SaveTables tables) = SaveGame.ReadInner(save, content);
        IReadOnlyList<(long Tick, Command Command)> entries = new CommandLogReader(log, tables).ReadAll();

        long saveTick = simulation.CurrentTick;
        if (untilTick < saveTick)
        {
            throw new ArgumentOutOfRangeException(nameof(untilTick), untilTick, $"The save is already at tick {saveTick}.");
        }

        int next = 0;
        while (next < entries.Count && entries[next].Tick <= saveTick)
        {
            next++;
        }

        for (long tick = saveTick + 1; tick <= untilTick; tick++)
        {
            while (next < entries.Count && entries[next].Tick == tick)
            {
                simulation.Enqueue(entries[next].Command);
                next++;
            }

            simulation.Tick();
        }

        return simulation.ComputeStateHash();
    }
}

/// <summary>The MessagePack envelope of one log entry: the tick, the command's simple type name and its payload.</summary>
/// <param name="Tick">The tick the command is applied on.</param>
/// <param name="Type">The command's simple type name.</param>
/// <param name="Payload">The command, serialised with the contractless resolver.</param>
[MessagePackObject(AllowPrivate = true)]
internal sealed record LogEntry([property: Key(0)] long Tick, [property: Key(1)] string Type, [property: Key(2)] byte[] Payload);

/// <summary>The serialisation options of the command log: a MessagePack envelope and contractless command payloads.</summary>
internal static class CommandLogOptions
{
    /// <summary>Gets the options of the (tick, type, payload) envelope.</summary>
    public static MessagePackSerializerOptions Envelope { get; } =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>Gets the options of the command payloads: <see cref="CommandFormatter"/> over the primitives.</summary>
    public static MessagePackSerializerOptions Payload { get; } =
        MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create([new CommandFormatter()], [StandardResolver.Instance]))
            .WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>Finds a command type by its simple name.</summary>
    /// <param name="name">The simple type name.</param>
    /// <param name="type">The command type, when found.</param>
    /// <returns><see langword="true"/> if a concrete <see cref="Command"/> has that name.</returns>
    internal static bool TryGetCommandType(string name, out Type? type)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _registry.Value.TryGetValue(name, out type);
    }

    private static readonly Lazy<SortedDictionary<string, Type>> _registry = new(BuildRegistry);

    private static SortedDictionary<string, Type> BuildRegistry()
    {
        var registry = new SortedDictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in typeof(Command).Assembly.GetTypes())
        {
            if (type is { IsAbstract: false, ContainsGenericParameters: false } && type.IsSubclassOf(typeof(Command)))
            {
                if (!registry.TryAdd(type.Name, type))
                {
                    throw new InvalidOperationException(
                        $"The command name \"{type.Name}\" is ambiguous: it names both {registry[type.Name].FullName} and {type.FullName}.");
                }
            }
        }

        return registry;
    }
}
