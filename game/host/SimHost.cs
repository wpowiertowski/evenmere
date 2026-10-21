using System.Diagnostics;
using Evenmere.Content;
using Evenmere.Content.Locale;
using Evenmere.Content.Maps;
using Evenmere.Core.Time;
using Evenmere.Sim;
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

    /// <summary>Raised once per frame in which ticks ran, after the events were drained, with those events in emission order (possibly none).</summary>
    public event Action<IReadOnlyList<SimEvent>>? EventsDrained;

    /// <summary>Gets a value indicating whether a simulation has been started.</summary>
    public bool IsRunning => _sim is not null;

    /// <summary>Gets the tick the simulation has completed.</summary>
    public long CurrentTick => RequireSim().CurrentTick;

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
    /// Loads the content and its display text, loads the island and starts a new world on it, replacing any simulation
    /// already running. Problems in the locale files are logged as warnings and never stop the game.
    /// </summary>
    /// <param name="seed">The world seed.</param>
    /// <param name="mapId">The island to start on (a folder under <c>maps/</c>); the starter island by default.</param>
    /// <exception cref="ContentLoadException">The content has errors.</exception>
    /// <exception cref="MapLoadException">The map has errors.</exception>
    public void StartNewGame(ulong seed, string mapId = LaunchOptions.DefaultMap)
    {
        string root = ContentPaths.Resolve();
        ContentDb content = ContentLoader.Load(root).GetDbOrThrow();
        MapDef map = MapLoader.Load([root], mapId, content).GetMapOrThrow();

        LocaleLoadResult locale = LocaleTable.Load(root, DefaultLanguage);
        foreach (ContentDiagnostic diagnostic in locale.Diagnostics)
        {
            GD.PushWarning($"[Evenmere] locale: {diagnostic}");
        }

        // Held as the interface on purpose: the host exposes queries, never the concrete simulation or its state.
        _sim = Simulation.CreateNew(content, new NewGameOptions { Seed = seed, Map = map });
        _content = content;
        _locale = locale.Table;
        _stepper.Reset();

        // A fresh map marks every chunk dirty; the terrain renderer builds them all on load, so the marks wait in the
        // view's buffer until it takes them (and drops them as already drawn) instead of arriving mid-frame later.
        DrainDirtyChunks(_sim);
    }

    // TODO(M1-14): start from a save (Simulation.Restore) once the persistence task defines the header and the content check.

    /// <summary>Queues a command for the next tick.</summary>
    /// <param name="command">The command.</param>
    public void Enqueue(Command command) => RequireSim().Enqueue(command);

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
