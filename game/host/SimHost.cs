using System.Diagnostics;
using Evenmere.Content;
using Evenmere.Content.Locale;
using Evenmere.Content.Maps;
using Evenmere.Core.Time;
using Evenmere.Game.Host.Persistence;
using Evenmere.Sim;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Host;

/// <summary>
/// The bridge between Godot and the simulation (architecture.md, "Bridging sim and view"; ADR-0006). It owns the
/// <see cref="ISimulation"/> and nothing else in the game holds it. Each frame it turns real time into whole ticks
/// with a fixed-step accumulator, runs them on the main thread within a tick and time budget, drains the events and
/// publishes them. Views read state through <see cref="Query"/> between ticks and never hold simulation state.
/// </summary>
public sealed partial class SimHost : Node
{
    /// <summary>
    /// The most wall-clock time one frame may spend ticking (ADR-0006). It is checked after each tick, so a frame always
    /// makes progress. The tick length, the 8-tick cap and the backlog cap live in <see cref="FixedStepper"/>.
    /// </summary>
    public const double MaxFrameMilliseconds = 4.0;

    /// <summary>The language of <see cref="Locale"/> until the settings screen (M4-05) lets the player choose.</summary>
    public const string DefaultLanguage = "en";

    private const string NoSimulationMessage = "No simulation is running; call StartNewGame first.";

    private readonly FixedStepper _stepper = new();
    private readonly ChunkCoord[] _drainScratch = new ChunkCoord[64];
    private readonly List<ChunkCoord> _pendingDirty = new();
    private int _pendingStart;
    private ISimulation? _sim;
    private ContentDb? _content;
    private LocaleTable? _locale;
    private SimSpeed _speed = SimSpeed.X1;
    private ulong _seed = LaunchOptions.DefaultSeed;
    private string? _mapId;

    /// <summary>Raised once per frame in which ticks ran, after the events were drained, with those events in emission order (possibly none).</summary>
    public event Action<IReadOnlyList<SimEvent>>? EventsDrained;

    /// <summary>
    /// Raised after every enqueued command, with the tick the command is applied on (<see cref="CurrentTick"/> + 1 at
    /// enqueue time). The <see cref="CommandLogRecorder"/> listens to this to keep the save's <c>.evlog</c> complete.
    /// </summary>
    public event Action<long, Command>? CommandEnqueued;

    /// <summary>Gets a value indicating whether a simulation has been started.</summary>
    public bool IsRunning => _sim is not null;

    /// <summary>Gets the tick the simulation has completed.</summary>
    public long CurrentTick => RequireSim().CurrentTick;

    /// <summary>Gets the world seed the running simulation was started or loaded with (<see cref="LaunchOptions.DefaultSeed"/> when none is running).</summary>
    public ulong Seed => _seed;

    /// <summary>Gets the island ID the running simulation runs on (<c>starter</c> by default).</summary>
    public string MapId => _mapId ?? LaunchOptions.DefaultMap;

    /// <summary>
    /// Gets the running simulation itself, for the save code. Views and tools use <see cref="Query"/> and
    /// <see cref="Enqueue"/> instead; nothing outside the game assembly may touch this.
    /// </summary>
    internal ISimulation Simulation => RequireSim();

    /// <summary>Gets the calendar at the current tick.</summary>
    public GameClock Clock => new(RequireSim().CurrentTick);

    /// <summary>
    /// Gets the content the running simulation was created from. The host loads it once in <see cref="StartNewGame"/>, so views
    /// that need names or the order of tiers and goods read this and never load content themselves. It is read-only.
    /// </summary>
    /// <exception cref="InvalidOperationException">No simulation has been started.</exception>
    public ContentDb Content => _content ?? throw new InvalidOperationException(NoSimulationMessage);

    /// <summary>
    /// Gets the display text, in <see cref="DefaultLanguage"/>, loaded from the same content root as <see cref="Content"/>.
    /// A key with no text gives back the key itself, so a missing string is visible but never fatal.
    /// </summary>
    /// <exception cref="InvalidOperationException">No simulation has been started.</exception>
    public LocaleTable Locale => _locale ?? throw new InvalidOperationException(NoSimulationMessage);

    /// <summary>Gets the read-only queries. They are valid only between ticks, which is any time outside the host's own frame step.</summary>
    public ISimQueries Query => RequireSim().Query;

    /// <summary>
    /// Gets how many drained dirty chunks are waiting for the view. The terrain renderer takes them with
    /// <see cref="DequeueDirtyChunks"/> and rebuilds at most four per frame, so bursts spread over frames.
    /// </summary>
    public int PendingDirtyCount => _pendingDirty.Count - _pendingStart;

    /// <summary>
    /// Hands the view up to <c>destination.Length</c> dirty chunks drained from the simulation after the last ticks,
    /// in ascending (Y, X) order. It never allocates: chunks stay in a reused buffer until taken here.
    /// </summary>
    /// <param name="destination">Receives the chunks.</param>
    /// <returns>The number of chunks written; 0 when none are waiting.</returns>
    public int DequeueDirtyChunks(Span<ChunkCoord> destination)
    {
        int waiting = _pendingDirty.Count - _pendingStart;
        int count = Math.Min(waiting, destination.Length);
        for (int i = 0; i < count; i++)
        {
            destination[i] = _pendingDirty[_pendingStart + i];
        }

        _pendingStart += count;
        if (_pendingStart >= _pendingDirty.Count)
        {
            _pendingDirty.Clear();
            _pendingStart = 0;
        }

        return count;
    }

    /// <summary>Gets how far the accumulator has run toward the next tick, from 0 to 1, so the view can interpolate between ticks.</summary>
    public double InterpolationAlpha => _stepper.InterpolationAlpha;

    /// <summary>Gets a value indicating whether <see cref="SimSpeed.X16"/> may be chosen, which is true only in debug builds.</summary>
    public static bool DebugSpeedAllowed => OS.IsDebugBuild();

    /// <summary>Gets or sets the speed. Asking for <see cref="SimSpeed.X16"/> in a release build gives <see cref="SimSpeed.X4"/>.</summary>
    public SimSpeed Speed
    {
        get => _speed;
        set
        {
            _speed = value == SimSpeed.X16 && !DebugSpeedAllowed ? SimSpeed.X4 : value;
            if (_speed == SimSpeed.Paused)
            {
                // Resuming should not start with a burst, so whole ticks of backlog go. The fraction stays, so the view holds its pose.
                _stepper.DropBacklog();
            }
        }
    }

    /// <summary>
    /// Loads the content and its display text when nothing runs yet and returns the content, so the new-game screen
    /// can generate a preview island before any simulation starts (when <see cref="Content"/> would still throw).
    /// The content loads once and is reused by <see cref="StartNewGame"/> and loaded saves.
    /// </summary>
    /// <returns>The loaded content.</returns>
    /// <exception cref="ContentLoadException">The content has errors.</exception>
    public ContentDb LoadContent()
    {
        EnsureContent();
        return Content;
    }

    /// <summary>
    /// Loads the content and its display text, loads the island and starts a new world on it, replacing any simulation
    /// already running. Problems in the locale files are logged as warnings and never stop the game.
    /// </summary>
    /// <param name="seed">The world seed.</param>
    /// <param name="mapId">The island to start on (a folder under <c>maps/</c>); the starter island by default.</param>
    /// <param name="cheatsUsed">Whether cheat commands are processed for the new game (ADR-0012).</param>
    /// <exception cref="ContentLoadException">The content has errors.</exception>
    /// <exception cref="MapLoadException">The map has errors.</exception>
    public void StartNewGame(ulong seed, string mapId = LaunchOptions.DefaultMap, bool cheatsUsed = false)
    {
        EnsureContent();
        MapDef map = MapLoader.Load([ContentPaths.Resolve()], mapId, Content!).GetMapOrThrow();
        StartNewGameCore(seed, map, mapId, cheatsUsed);
    }

    /// <summary>
    /// Starts a new world on a ready-made island (M2-25), replacing any simulation already running. A generated
    /// island needs no map folder: the new-game screen draws the <see cref="MapDef"/> off-thread and hands it
    /// over finished. The content still loads first, so the island always matches the running content.
    /// </summary>
    /// <param name="seed">The world seed.</param>
    /// <param name="map">The finished island, generated or loaded.</param>
    /// <param name="cheatsUsed">Whether cheat commands are processed for the new game (ADR-0012).</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContentLoadException">The content has errors.</exception>
    public void StartNewGame(ulong seed, MapDef map, bool cheatsUsed = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        EnsureContent();
        StartNewGameCore(seed, map, map.Id, cheatsUsed);
    }

    private void StartNewGameCore(ulong seed, MapDef map, string mapId, bool cheatsUsed)
    {
        // Held as the interface on purpose: the host exposes queries, never the concrete simulation or its state.
        _sim = Evenmere.Sim.Simulation.CreateNew(Content!, new NewGameOptions { Seed = seed, Map = map });

        // Cheat commands are processed only for cheats-enabled saves (ADR-0012): every debug new game, or a
        // release new game with the launch flag. Loaded saves carry the flag in their header instead.
        _sim.CheatsUsed = cheatsUsed || OS.IsDebugBuild();
        _seed = seed;
        _mapId = mapId;
        _stepper.Reset();

        // A fresh map marks every chunk dirty; the terrain renderer builds them all on load, so the marks wait in the
        // view's buffer until it takes them (and drops them as already drawn) instead of arriving mid-frame later.
        DrainDirtyChunks(_sim);
    }

    /// <summary>
    /// Loads a save file, replacing the simulation already running (or starting one when the game is at the menu).
    /// The content is loaded first when nothing runs yet; otherwise the running content remaps the save's IDs.
    /// </summary>
    /// <param name="savePath">The <c>.evsave</c> path.</param>
    /// <returns>What loading changed or dropped to fit the current content.</returns>
    /// <exception cref="ContentLoadException">The content has errors.</exception>
    /// <exception cref="InvalidDataException">The stream is not a save this version can load.</exception>
    public SaveReport LoadGame(string savePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        EnsureContent();

        using FileStream stream = File.OpenRead(savePath);
        (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, Content!);
        _sim = simulation;
        _stepper.Reset();

        // The seed and island travel in the save's sidecar; without one (a hand-placed save) keep the previous values.
        SaveSlotMeta? meta = SaveSlots.ReadMeta(
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? string.Empty, SaveSlots.SlotIdOf(savePath) + SaveSlots.MetaExtension));
        if (meta is not null)
        {
            _seed = meta.Seed;
            if (!string.IsNullOrEmpty(meta.MapId))
            {
                _mapId = meta.MapId;
            }
        }

        DrainDirtyChunks(simulation);
        return report;
    }

    /// <summary>Stops the running simulation and forgets it, for quitting back to the main menu.</summary>
    public void UnloadGame()
    {
        _sim = null;
        _stepper.Reset();
        _pendingDirty.Clear();
        _pendingStart = 0;
    }

    // Loads the content and its display text once, shared by new games and loaded saves. Problems in the locale
    // files are logged as warnings and never stop the game.
    private void EnsureContent()
    {
        if (_content is not null && _locale is not null)
        {
            return;
        }

        string root = ContentPaths.Resolve();
        ContentDb content = ContentLoader.Load(root).GetDbOrThrow();
        LocaleLoadResult locale = LocaleTable.Load(root, DefaultLanguage);
        foreach (ContentDiagnostic diagnostic in locale.Diagnostics)
        {
            GD.PushWarning($"[Evenmere] locale: {diagnostic}");
        }

        _content = content;
        _locale = locale.Table;
    }

    /// <summary>Queues a command for the next tick, then tells the command-log recorder which tick it lands on.</summary>
    /// <param name="command">The command.</param>
    public void Enqueue(Command command)
    {
        ISimulation sim = RequireSim();
        long appliedTick = sim.CurrentTick + 1;
        sim.Enqueue(command);
        CommandEnqueued?.Invoke(appliedTick, command);
    }

    /// <summary>Computes the state hash of the world as it is now.</summary>
    /// <returns>The XxHash64 state hash (ADR-0003).</returns>
    public ulong ComputeStateHash() => RequireSim().ComputeStateHash();

    /// <summary>
    /// Runs ticks back to back, ignoring the accumulator, the speed and the budgets. For the smoke test and for catching up;
    /// the events of all the ticks are published together at the end.
    /// </summary>
    /// <param name="count">The number of ticks.</param>
    public void RunTicksNow(int count)
    {
        ISimulation sim = RequireSim();
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        for (int i = 0; i < count; i++)
        {
            sim.Tick();
        }

        Publish(sim);
        DrainDirtyChunks(sim);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_sim is not null)
        {
            Advance(_sim, delta);
        }
    }

    private void Advance(ISimulation sim, double deltaSeconds)
    {
        if (_speed == SimSpeed.Paused)
        {
            return;
        }

        _stepper.Accumulate(deltaSeconds, _speed.Multiplier());

        // Ticks run one at a time so the time budget can stop the frame early; the stepper keeps the ticks not run.
        int due = _stepper.TicksDue();
        int ticks = 0;
        long start = Stopwatch.GetTimestamp();
        while (ticks < due)
        {
            sim.Tick();
            ticks++;

            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= MaxFrameMilliseconds)
            {
                break;
            }
        }

        _stepper.Consume(ticks);

        if (ticks > 0)
        {
            Publish(sim);
            DrainDirtyChunks(sim);
        }
    }

    private void Publish(ISimulation sim) => EventsDrained?.Invoke(sim.DrainEvents());

    // Moves the map's dirty marks into the view's reused buffer. The marks are bookkeeping outside world state,
    // so draining them never changes the state hash.
    private void DrainDirtyChunks(ISimulation sim)
    {
        while (true)
        {
            int drained = sim.Query.Map.DrainDirtyChunks(_drainScratch.AsSpan());
            if (drained == 0)
            {
                break;
            }

            for (int i = 0; i < drained; i++)
            {
                _pendingDirty.Add(_drainScratch[i]);
            }
        }
    }

    private ISimulation RequireSim() =>
        _sim ?? throw new InvalidOperationException(NoSimulationMessage);
}
