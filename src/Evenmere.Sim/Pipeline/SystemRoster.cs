using Evenmere.Content;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;
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
/// that fixes the tick order and the cadences), the write services and the query facade. When a stub system grows a
/// constructor that takes its state slice, the content or another service, the task that fills it passes that here.
/// </summary>
/// <remarks>
/// Ten tasks edit this file in parallel, each on its own lines. Keep every task's lines apart (a blank line between
/// groups), because neighbouring edits by different tasks are merge conflicts.
/// </remarks>
internal sealed class SystemRoster
{
    public SystemRoster(WorldState world, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(world);

        // The content is passed so a task whose system needs it in a constructor does not have to change this signature.
        ArgumentNullException.ThrowIfNull(content);

        // Services first: they are never ticked, but they register command handlers and answer queries.
        // M1-01 changes the next line to pass the map state, the entity ID allocator and the content.
        var map = new MapSystem();

        // M1-05 changes the next line to pass the roads state.
        var roads = new RoadsSystem();

        var storage = new StorageSystem(world.Storage);

        // M1-04 changes the next line to pass the modifiers state.
        var modifiers = new ModifiersSystem();

        // Ticked systems, in the exact order of architecture.md (System order), steps 2 to 15.
        // M1-06 changes the next line to pass the construction state.
        var construction = new ConstructionSystem(world.Construction, content);

        // M1-07 changes the next line to pass the workforce state.
        var workforce = new WorkforceSystem();

        // M1-08 changes the next line to pass the production state.
        var production = new ProductionSystem();

        // M1-09 changes the next line to pass the logistics state.
        var logistics = new LogisticsSystem();

        var exploration = new ExplorationSystem();

        // M1-10 changes the next line to pass the needs state.
        var needs = new NeedsSystem();

        // M1-10 changes the next line to pass the population state.
        var population = new PopulationSystem();

        var economy = new EconomySystem();

        // M1-11 changes the next line to pass the research state.
        var research = new ResearchSystem();

        // M1-11 changes the next line to pass the culture state.
        var culture = new CultureSystem();

        // M1-04 changes the next line to pass the progression state.
        var progression = new ProgressionSystem();

        var happenings = new HappeningsSystem();

        // M1-15 changes the next line to pass the advisors state.
        var advisors = new AdvisorsSystem();

        // M1-12 changes the next line to pass the statistics state.
        var statistics = new StatisticsSystem();

        // The write services. Each is a throwing stand-in until the task named beside it replaces its line(s).
        Services = new SimServices
        {
            // M1-01: the map service (building registry, lifecycle, dirty chunks).
            BuildingRegistry = new UnimplementedBuildingRegistry(),
            BuildingLifecycle = new UnimplementedBuildingLifecycle(),
            DirtyChunks = new UnimplementedChunkDirtyTracker(),

            // M0-05: the island goods pool.
            Storage = new StorageService(storage.Pool),

            // M1-04: progression (effects, points ledger, counters).
            ProgressionService = new UnimplementedProgressionService(),
            PointsLedger = new UnimplementedPointsLedger(),
            Counters = new UnimplementedCounters(),

            // M1-08: production buffers, as Logistics sees them.
            ProductionBuffers = new UnimplementedProductionBuffers(),

            // M1-07: the workforce supply that Population sets.
            WorkforceSupply = new UnimplementedWorkforceSupply(),
        };

        // Lifecycle subscriptions: M1-13 subscribes the listeners here, in pipeline order, once M1-01 has landed.

        // The two halves of IPlacementQueries. PlacementQueries forwards to them.
        // M1-06: Construction's building placement check.
        IBuildingPlacement buildingPlacement = new BuildingPlacement(
            content, world.Construction, map.Queries, roads.Queries, progression.Queries);

        // M1-05: Roads' road placement check.
        IRoadPlacement roadPlacement = new UnimplementedRoadPlacement();

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
            Placement = new PlacementQueries(buildingPlacement, roadPlacement),

            // M1-12: the explain service.
            Explain = new ExplainQueries(),
        };
    }

    /// <summary>Gets every system with its cadence: the four services, then the ticked systems in tick order.</summary>
    public IReadOnlyList<PipelineEntry> Entries { get; }

    /// <summary>Gets the query facade over every system.</summary>
    public SimQueries Queries { get; }

    /// <summary>Gets the write services that <see cref="SimContext"/> exposes to the systems.</summary>
    public SimServices Services { get; }
}
