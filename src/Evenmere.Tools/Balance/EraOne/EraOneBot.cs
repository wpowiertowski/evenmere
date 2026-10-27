using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;
using Evenmere.Sim.Systems.Exploration.Commands;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Happenings.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Tools.Balance.EraOne;

/// <summary>
/// The Era I island bot: an auto-player that plays a <see cref="BotScript"/> through Era I to both gateways using only
/// <see cref="ISimulation"/> and its queries, the way a player uses the UI. It reuses the v1 bot's search
/// (<see cref="PlacementSearch"/>), roads (<see cref="RoadPlanner"/>), script (<see cref="BotScript"/>) and report
/// (<see cref="BalanceReport"/>) pieces; only the orchestration loop is its own, because the Era I behaviours live
/// here: expeditions from idle lodges, tradition slotting from <see cref="TraditionHeuristics"/>, the Harvest Fair
/// when affordable, happening answers on day 5, affordable-only reactive buildings, and era tracking to Era II.
/// The run is deterministic for one seed and script, except for the wall-clock <see cref="BalanceTimings"/>.
/// </summary>
/// <remarks>
/// The runner is split across focused files: <c>EraOneGoals</c> (script goals), <c>EraOneReactive</c> (shortage,
/// coverage and road rules), <c>EraOneCulture</c> (expeditions, traditions, festivals, happenings),
/// <c>EraOneNetwork</c> (bank, reachability and link search), <c>EraOnePlacement</c> (anchors and bookkeeping)
/// and <c>EraOneTracking</c> (tick events, stalls and era detection). Shared goal state lives in
/// <see cref="Balance.BotGoals"/> together with the v1 bot's.
/// </remarks>
public static partial class EraOneBot
{
    /// <summary>Runs the script to Era II.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="map">The island to settle.</param>
    /// <param name="script">The goals, already validated against <paramref name="content"/>.</param>
    /// <param name="options">The seed and the cap on ticks.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static EraOneReport Run(ContentDb content, MapDef map, BotScript script, BalanceOptions options) =>
        RunDetailed(content, map, script, options).Report;

    /// <summary>Runs the script and keeps the final simulation, so tests and tools can inspect the end state.</summary>
    /// <param name="content">The loaded content.</param>
    /// <param name="map">The island to settle.</param>
    /// <param name="script">The goals, already validated against <paramref name="content"/>.</param>
    /// <param name="options">The seed and the cap on ticks.</param>
    /// <returns>The report and the simulation at the last tick.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static (EraOneReport Report, ISimulation Simulation) RunDetailed(ContentDb content, MapDef map, BotScript script, BalanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(options);
        var runner = new Runner(content, map, script, options);
        return (runner.Run(), runner.Final);
    }

    private sealed partial class Runner
    {
        private const int RetryDelayTicks = 120;
        private const int ShortageCooldownTicks = 600;
        private const int AdviceCooldownTicks = 600;
        private const int PlacementRetryTicks = 600;
        private const int TraditionCooldownTicks = 600;
        private const int CoverageBackoffTicks = 6000;
        private const int DerelictTicks = 2400;
        private const int StalledSiteTicks = 4800;

        private readonly ContentDb _content;
        private readonly BotScript _script;
        private readonly BalanceOptions _options;
        private readonly Simulation _simulation;
        private readonly BuildingTypeId _roadType;
        private readonly EraId _township;
        private readonly string _milestoneTier;
        private readonly TierId _milestoneTierId;
        private readonly string _targetTier;
        private readonly int _targetResidents;

        private readonly SortedDictionary<string, int> _issued = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _rejected = new(StringComparer.Ordinal);
        private readonly List<string> _notes = [];
        private readonly List<BalanceStall> _stalls = [];
        private readonly List<BalanceGoalResult> _goalResults = [];
        private readonly Dictionary<GoodId, int> _shortageStreak = [];
        private readonly Dictionary<GoodId, int> _emptyStreak = [];
        private readonly List<(string Key, long Tick)> _cooldowns = [];
        private readonly List<BotPendingPlacement> _reactive = [];
        private readonly List<(string Type, int X, int Y, Rotation Rotation)> _banned = [];
        private readonly List<(string Type, int X, int Y, Rotation Rotation)> _linkBanned = [];
        private int _roadsSeen;
        private bool _bansCleared;        private readonly List<(int X, int Y, int Width, int Height, long Tick)> _claims = [];
        private readonly List<(int X, int Y, int Width, int Height)> _reservedFarms = [];
        private readonly bool[] _bank;
        private readonly BuildingTypeId _bridgeType;
        private bool _farmsReserved;
        private readonly Dictionary<EntityId, long> _siteSeen = [];
        private ImmutableArray<string>? _queueOverride;
        private bool _needBridges;
        private readonly HashSet<EntityId> _expeditionSent = [];
        private readonly HashSet<int> _expeditionRegions = [];

        private readonly BotGoalState[] _states;
        private int _goalIndex;
        private long? _firstTick;
        private long? _fiftyTick;
        private long? _hundredTick;
        private long? _eraTwoTick;
        private int? _residentsAtFirst;
        private int? _residentsAtHundred;
        private int? _residentsAtEraTwo;
        private int? _villagersAtEraTwo;
        private int _bestResidents;
        private long _bestTick;
        private long _timingTotal;
        private long _timingPeak;

        internal ISimulation Final => _simulation;

        internal Runner(ContentDb content, MapDef map, BotScript script, BalanceOptions options)
        {
            _content = content;
            _script = script;
            _options = options;
            _simulation = Simulation.CreateNew(content, new NewGameOptions { Seed = options.Seed, Map = map });
            _roadType = CheapestRoad(content);
            _bridgeType = content.Buildings.TryGet("timber_bridge", out BuildingTypeId bridge) ? bridge : default;
            _township = content.Eras.TryGet("township", out EraId township) ? township : content.ErasByOrder[^1];
            _states = new BotGoalState[script.Goals.Length];
            for (int i = 0; i < _states.Length; i++)
            {
                _states[i] = new BotGoalState();
            }
            _bank = ComputeBank();

            if (content.Tiers.TryGet("villagers", out TierId villagers))
            {
                _milestoneTier = "villagers";
                _milestoneTierId = villagers;
            }
            else
            {
                HousesUntilGoal? houses = null;
                foreach (BotGoal goal in script.Goals)
                {
                    if (goal is HousesUntilGoal h)
                    {
                        houses = h;
                    }
                }

                _milestoneTier = houses?.Tier ?? content.Tiers.Names[0];
                _milestoneTierId = content.Tiers.IdOf(_milestoneTier);
            }

            HousesUntilGoal? last = null;
            foreach (BotGoal goal in script.Goals)
            {
                if (goal is HousesUntilGoal h)
                {
                    last = h;
                }
            }

            _targetTier = last?.Tier ?? _milestoneTier;
            _targetResidents = last?.Residents ?? 100;
        }

        internal EraOneReport Run()
        {
            // A real player plans the orchard belt before the houses sprawl: reserve it at tick 0 while the
            // ground is still free (see ReserveFarmSites). Deterministic: the island is exactly as generated.
            ReserveFarmSites();
            for (long i = 0; i < _options.MaxTicks; i++)
            {
                bool dayBoundary = _simulation.CurrentTick > 0 && _simulation.CurrentTick % GameTime.TicksPerDay == 0;
                RunGoals();
                if (dayBoundary)
                {
                    RunReactive();
                }

                long start = Stopwatch.GetTimestamp();
                _simulation.Tick();
                long elapsed = Stopwatch.GetTimestamp() - start;
                _timingTotal += elapsed;
                _timingPeak = Math.Max(_timingPeak, elapsed);

                PostTick();
                if (_eraTwoTick is not null)
                {
                    break;
                }

                if (_goalIndex >= _states.Length)
                {
                    // The script is done but Era II has not begun: the reactive rules keep playing (festivals,
                    // expeditions, traditions, shortages) until the gateways complete or the budget runs out.
                    // Unresolved reactive sites stay sites in the final hash.
                    continue;
                }
            }

            return Assemble();
        }

        private EraOneReport Assemble()
        {
            while (_goalIndex < _states.Length)
            {
                BotGoalState state = _states[_goalIndex];
                BotGoal goal = _script.Goals[_goalIndex];
                if (state.Status is null)
                {
                    state.Status = BalanceGoalStatus.Failed;
                    _goalResults.Add(new BalanceGoalResult(_goalIndex, BotGoals.KindOf(goal), state.Detail ?? "run ended", state.Status.Value, _simulation.CurrentTick - state.StartTick));
                }

                _goalIndex++;
            }

            ImmutableSortedDictionary<string, int>.Builder residents = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach (TierId tier in _content.TiersByOrder)
            {
                residents[_content.Tiers.Names[tier.Value]] = _simulation.Query.Population.Residents(tier);
            }

            ImmutableSortedDictionary<string, double>.Builder stock = ImmutableSortedDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);
            foreach (GoodDef def in _content.Goods.Items)
            {
                Amount amount = _simulation.Query.Storage.Stock(def.Id);
                stock[_content.Goods.Names[def.Id.Value]] = amount.Milli / 1000.0;
            }

            ImmutableSortedDictionary<string, int>.Builder issued = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach ((string name, int count) in _issued)
            {
                issued[name] = count;
            }

            ImmutableSortedDictionary<string, int>.Builder rejected = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            foreach ((string name, int count) in _rejected)
            {
                rejected[name] = count;
            }

            bool reached = _simulation.Query.Population.Residents(_content.Tiers.IdOf(_targetTier)) >= _targetResidents;
            long? reachedTick = reached ? _hundredTick ?? _simulation.CurrentTick : null;
            if (_targetTier == _milestoneTier && _hundredTick is not null)
            {
                reachedTick = _hundredTick;
            }

            double frequency = Stopwatch.Frequency;
            long ticks = _simulation.CurrentTick;
            GatewayStatus gateway = _simulation.Query.Progression.GatewayStatus(_township);
            _goalResults.Sort(static (left, right) => left.Index.CompareTo(right.Index));
            var @base = new BalanceReport(
                _script.Name ?? "bot",
                _options.Seed,
                ticks,
                new BalanceTargetResult(_targetTier, _targetResidents, reached, reachedTick),
                new BalanceMilestones(_milestoneTier, _firstTick, _fiftyTick, _hundredTick),
                [.. _goalResults],
                [.. _stalls],
                issued.ToImmutable(),
                rejected.ToImmutable(),
                residents.ToImmutable(),
                stock.ToImmutable(),
                [.. _notes],
                _simulation.ComputeStateHash().ToString("x16", CultureInfo.InvariantCulture),
                new BalanceTimings(ticks, ticks == 0 ? 0 : (_timingTotal / frequency * 1000) / ticks, _timingPeak / frequency * 1000));
            return new EraOneReport(
                @base,
                new EraOneEraResult(_eraTwoTick is not null, _eraTwoTick, _residentsAtEraTwo, _villagersAtEraTwo, _residentsAtFirst, _residentsAtHundred),
                gateway.TechDone,
                gateway.CivicDone);
        }

        // A repair that the coverage check did not confirm is removed again, and its spot is banned, so the
        // next repair tries elsewhere instead of rebuilding the same failure.
    }
}
