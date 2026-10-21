using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Sim.Systems.Roads;

/// <summary>
/// The roads service: road layer, connectivity and the reach and coverage fields. A shared service rather than a ticked system, so its cadence is
/// <see cref="Cadence.None"/> and <see cref="Run"/> is never called. It registers the road command handlers, answers the road
/// queries and the road half of the placement queries, and listens to the building lifecycle (M1-13 subscribes it) to learn
/// which buildings are storehouses or services.
/// </summary>
internal sealed class RoadsSystem : ISimSystem, IBuildingLifecycleListener
{
    private readonly RoadNetwork _network;
    private readonly RoadPlacement _placement;
    private readonly ContentDb _content;

    /// <summary>Initializes a new instance of the <see cref="RoadsSystem"/> class.</summary>
    /// <param name="state">The roads slice of the world state.</param>
    /// <param name="content">The loaded content.</param>
    /// <param name="queries">
    /// A late-bound accessor for the query facade. The roster builds this system before the facade exists, and queries and
    /// placement checks carry no <see cref="SimContext"/>, so the system reaches the map and progression through this.
    /// It is called only when a query, command or lifecycle notification arrives, never during construction.
    /// </param>
    /// <exception cref="InvalidDataException">The state's road layer does not match its size.</exception>
    public RoadsSystem(RoadsState state, ContentDb content, Func<ISimQueries> queries)
    {
        _content = content;
        _network = new RoadNetwork(state, content, queries);
        _placement = new RoadPlacement(_network, content, queries);
        Queries = new RoadsQueries(_network);
    }

    /// <inheritdoc />
    public string Name => SystemNames.Roads;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IRoadsQueries Queries { get; }

    /// <summary>Gets the road half of the placement queries, shared by the ghost preview and the <c>PlaceRoad</c> handler.</summary>
    public IRoadPlacement Placement => _placement;

    /// <summary>Gets the road network, for tests that inspect fields and count rebuilds.</summary>
    internal RoadNetwork Network => _network;

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        router.Register(new PlaceRoadHandler(_network, _placement, _content));
        router.Register(new RemoveRoadHandler(_network));
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // A service is never ticked (Cadence.None); it changes only through command handlers and the calls other systems make into it.
    }

    /// <inheritdoc />
    public void OnActivated(SimContext ctx, BuildingInfo building) => _network.SetSource(building);

    /// <inheritdoc />
    public void OnRemoved(SimContext ctx, BuildingInfo building) => _network.RemoveSource(building);

    /// <inheritdoc />
    public void OnTypeChanged(SimContext ctx, BuildingInfo building, BuildingTypeId previousType) => _network.SetSource(building);
}
