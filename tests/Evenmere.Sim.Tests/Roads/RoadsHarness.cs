using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>
/// One roads system over a <see cref="FakeSim"/>: a plains map, the fake building registry wired to the roads system as a
/// lifecycle listener (as M1-13 will do), and a command router with the road handlers. Tests arrange buildings and terrain
/// through it, send commands, and read answers through <see cref="Queries"/> or the system's network.
/// </summary>
internal sealed class RoadsHarness
{
    private readonly CommandRouter _router = new();
    private readonly CommandQueue _queue = new();

    /// <summary>Initializes a new instance of the <see cref="RoadsHarness"/> class.</summary>
    /// <param name="size">The width and height of the square map.</param>
    /// <param name="content">The content; the roads test pack by default.</param>
    /// <param name="subscribe">Whether the roads system listens to the building lifecycle, which is how M1-13 wires it.</param>
    public RoadsHarness(int size = 32, ContentDb? content = null, bool subscribe = true)
    {
        Sim = FakeSim.Create(content ?? RoadContent.Db);
        Sim.Queries.Map.Width = size;
        Sim.Queries.Map.Height = size;
        Sim.Queries.Map.DefaultTile = Tile("plains");
        Sim.Queries.Progression.Unlock(Dirt);
        Roads = new RoadsSystem(Sim.World.Roads, Sim.Content, () => Sim.Queries);
        Roads.Register(_router);
        if (subscribe)
        {
            Sim.Services.BuildingLifecycle.Subscribe(Roads);
        }
    }

    /// <summary>Gets the fake simulation.</summary>
    public FakeSim Sim { get; }

    /// <summary>Gets the roads system under test.</summary>
    public RoadsSystem Roads { get; }

    /// <summary>Gets the road network, for the instrumented accessors.</summary>
    public RoadNetwork Network => Roads.Network;

    /// <summary>Gets the road queries.</summary>
    public IRoadsQueries Queries => Roads.Queries;

    /// <summary>Gets the dirt road type.</summary>
    public static BuildingTypeId Dirt => RoadContent.Building("dirt_road");

    /// <summary>Gets the cobbled road type.</summary>
    public static BuildingTypeId Cobbled => RoadContent.Building("cobbled_road");

    /// <summary>Gets the paved road type.</summary>
    public static BuildingTypeId Paved => RoadContent.Building("paved_road");

    /// <summary>Makes a flat tile of a terrain.</summary>
    /// <param name="terrain">The terrain's string ID.</param>
    /// <param name="features">The tile's features.</param>
    /// <returns>The tile.</returns>
    public static TileInfo Tile(string terrain, TileFeatures features = TileFeatures.None) =>
        new(RoadContent.Terrain(terrain), 0, SlopeClass.Flat, features, RiverWidthClass.None, 0, null, 0);

    /// <summary>Unlocks road types.</summary>
    /// <param name="types">The road types.</param>
    public void Unlock(params BuildingTypeId[] types) => Sim.Queries.Progression.Unlock(types);

    /// <summary>Paints a tile with a terrain.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="terrain">The terrain's string ID.</param>
    /// <param name="features">The tile's features.</param>
    public void SetTerrain(int x, int y, string terrain, TileFeatures features = TileFeatures.None) =>
        Sim.Queries.Map.SetTile(x, y, Tile(terrain, features));

    /// <summary>Adds a finished (or other) building through the fake registry, which tells the lifecycle listeners.</summary>
    /// <param name="type">The building type's string ID.</param>
    /// <param name="x">The column of the top-left tile.</param>
    /// <param name="y">The row of the top-left tile.</param>
    /// <param name="state">The state to add it in.</param>
    /// <returns>The building's entity ID.</returns>
    public EntityId Add(string type, int x, int y, BuildingState state = BuildingState.Active) =>
        Sim.Services.BuildingRegistry.Add(RoadContent.Building(type), x, y, Rotation.Deg0, state);

    /// <summary>Removes a building through the fake registry.</summary>
    /// <param name="building">The building.</param>
    public void Remove(EntityId building) => Sim.Services.BuildingRegistry.Remove(building);

    /// <summary>Sends a command through the router and returns the events it caused.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The events emitted, in order.</returns>
    public IReadOnlyList<SimEvent> Send(Command command)
    {
        _queue.Enqueue(command);
        _router.ApplyQueued(_queue, Sim.Context);
        return Sim.DrainEvents();
    }

    /// <summary>Lays a road along tiles.</summary>
    /// <param name="type">The road type's building type.</param>
    /// <param name="tiles">The tiles.</param>
    /// <returns>The events emitted.</returns>
    public IReadOnlyList<SimEvent> Place(BuildingTypeId type, params (int X, int Y)[] tiles) =>
        Send(new PlaceRoad([.. tiles.Select(static t => new TileCoord(t.X, t.Y))], type));

    /// <summary>Lays a dirt road along tiles.</summary>
    /// <param name="tiles">The tiles.</param>
    /// <returns>The events emitted.</returns>
    public IReadOnlyList<SimEvent> PlaceDirt(params (int X, int Y)[] tiles) => Place(Dirt, tiles);

    /// <summary>Lays a straight road of one type along a row or a column.</summary>
    /// <param name="type">The road type.</param>
    /// <param name="from">The first tile.</param>
    /// <param name="to">The last tile; it shares a row or a column with <paramref name="from"/>.</param>
    /// <returns>The events emitted.</returns>
    public IReadOnlyList<SimEvent> Line(BuildingTypeId type, (int X, int Y) from, (int X, int Y) to) =>
        Send(new PlaceRoad(Straight(from, to), type));

    /// <summary>Lays a straight dirt road.</summary>
    /// <param name="from">The first tile.</param>
    /// <param name="to">The last tile.</param>
    /// <returns>The events emitted.</returns>
    public IReadOnlyList<SimEvent> DirtLine((int X, int Y) from, (int X, int Y) to) => Line(Dirt, from, to);

    /// <summary>Clears roads from tiles.</summary>
    /// <param name="tiles">The tiles.</param>
    /// <returns>The events emitted.</returns>
    public IReadOnlyList<SimEvent> RemoveRoads(params (int X, int Y)[] tiles) =>
        Send(new RemoveRoad([.. tiles.Select(static t => new TileCoord(t.X, t.Y))]));

    /// <summary>Lists the tiles of a straight run, both ends included.</summary>
    /// <param name="from">The first tile.</param>
    /// <param name="to">The last tile.</param>
    /// <returns>The tiles in order.</returns>
    public static TileCoord[] Straight((int X, int Y) from, (int X, int Y) to)
    {
        int dx = Math.Sign(to.X - from.X);
        int dy = Math.Sign(to.Y - from.Y);
        int length = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) + 1;
        return [.. Enumerable.Range(0, length).Select(i => new TileCoord(from.X + (dx * i), from.Y + (dy * i)))];
    }

    /// <summary>Computes the hash of the whole world state, as the simulation would.</summary>
    /// <returns>The state hash.</returns>
    public ulong StateHash() => StateSerializer.ComputeHash(Sim.World);

    /// <summary>
    /// Builds a second roads system over a copy of the state that went through a save and a load, reading the same map. It is
    /// what a loaded game has: the layer, and nothing else; the fields must be rebuilt.
    /// </summary>
    /// <returns>The reloaded system, with no fields built yet.</returns>
    public RoadsSystem Reload()
    {
        WorldState restored = StateSerializer.Read(StateSerializer.Write(Sim.World), Sim.Content);
        return new RoadsSystem(restored.Roads, Sim.Content, () => Sim.Queries);
    }
}
