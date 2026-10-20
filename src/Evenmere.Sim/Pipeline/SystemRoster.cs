using Evenmere.Sim.Queries;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Economy;
using Evenmere.Sim.Systems.Exploration;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Logistics;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Needs;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Statistics;
using Evenmere.Sim.Systems.Storage;
using Evenmere.Sim.Systems.Workforce;

namespace Evenmere.Sim.Pipeline;

/// <summary>
/// The composition root: builds every system over a world state, and from them the pipeline entries (the one place
/// that fixes the tick order and the cadences) and the query facade. When a stub system grows a constructor that
/// takes its state slice or another service, the task that fills it passes that here.
/// </summary>
internal sealed class SystemRoster
{
    public SystemRoster(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Services first: they are never ticked, but they register command handlers and answer queries.
        var map = new MapSystem();
        var roads = new RoadsSystem();
        var storage = new StorageSystem(world.Storage);
        var modifiers = new ModifiersSystem();

        // Ticked systems, in the exact order of architecture.md (System order), steps 2 to 15.
        var construction = new ConstructionSystem();
        var workforce = new WorkforceSystem();
        var production = new ProductionSystem();
        var logistics = new LogisticsSystem();
        var exploration = new ExplorationSystem();
        var needs = new NeedsSystem();
        var population = new PopulationSystem();
        var economy = new EconomySystem();
        var research = new ResearchSystem();
        var culture = new CultureSystem();
        var progression = new ProgressionSystem();
        var happenings = new HappeningsSystem();
        var advisors = new AdvisorsSystem();
        var statistics = new StatisticsSystem();

        Storage = storage.Pool;
        Entries =
        [
            new(SystemId.Map, map, Cadence.None),
            new(SystemId.Roads, roads, Cadence.None),
            new(SystemId.Storage, storage, Cadence.None),
            new(SystemId.Modifiers, modifiers, Cadence.None),
            new(SystemId.Construction, construction, Cadence.EveryTick),
            new(SystemId.Workforce, workforce, Cadence.Daily),
            new(SystemId.Production, production, Cadence.EveryTick),
            new(SystemId.Logistics, logistics, Cadence.EveryTick),
            new(SystemId.Exploration, exploration, Cadence.EveryTick),
            new(SystemId.Needs, needs, Cadence.DailyBucketed),
            new(SystemId.Population, population, Cadence.DailyBucketed),
            new(SystemId.Economy, economy, Cadence.DailyBucketed),
            new(SystemId.Research, research, Cadence.Daily),
            new(SystemId.Culture, culture, Cadence.Daily),
            new(SystemId.Progression, progression, Cadence.Daily),
            new(SystemId.Happenings, happenings, Cadence.Daily),
            new(SystemId.Advisors, advisors, Cadence.Daily),
            new(SystemId.Statistics, statistics, Cadence.Daily),
        ];

        Queries = new SimQueries
        {
            Map = map.Queries,
            Roads = roads.Queries,
            Storage = storage.Queries,
            Modifiers = modifiers.Queries,
            Construction = construction.Queries,
            Workforce = workforce.Queries,
            Production = production.Queries,
            Logistics = logistics.Queries,
            Exploration = exploration.Queries,
            Needs = needs.Queries,
            Population = population.Queries,
            Economy = economy.Queries,
            Research = research.Queries,
            Culture = culture.Queries,
            Progression = progression.Queries,
            Happenings = happenings.Queries,
            Advisors = advisors.Queries,
            Statistics = statistics.Queries,
            Placement = new PlacementQueries(),
            Explain = new ExplainQueries(),
        };
    }

    /// <summary>Gets every system with its cadence: the four services, then the ticked systems in tick order.</summary>
    public IReadOnlyList<PipelineEntry> Entries { get; }

    /// <summary>Gets the query facade over every system.</summary>
    public SimQueries Queries { get; }

    /// <summary>Gets the write side of the storage service, for <see cref="SimContext"/>.</summary>
    public StoragePool Storage { get; }
}
