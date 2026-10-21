using System.Reflection;
using Evenmere.Content;
using Evenmere.Core.Hashing;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Roads.Commands;
using Evenmere.Sim.Tests.Fixtures;
using MessagePack;

namespace Evenmere.Sim.Tests.Roads;

/// <summary>
/// Saving and loading the roads, and determinism. Only the road layer is saved: the distance fields and the lookups derived
/// from them are rebuilt from the layer, the building registry and the content the first time they are needed.
/// </summary>
public sealed class RoadsStateTests
{
    private static RoadsHarness City()
    {
        var h = new RoadsHarness(size: 48);
        h.Unlock(RoadsHarness.Cobbled, RoadsHarness.Paved);
        h.Add("storehouse", 0, 8);
        h.Add("depot", 20, 9);
        h.Add("stall", 6, 9);
        h.Add("shrine", 12, 8);
        h.Add("founders_hall", 30, 6);
        h.DirtLine((0, 10), (20, 10));
        h.Line(RoadsHarness.Cobbled, (21, 10), (30, 10));
        h.Line(RoadsHarness.Paved, (31, 10), (40, 10));
        foreach (int x in new[] { 3, 9, 15, 25, 35 })
        {
            h.Add("hut", x, 11);
        }

        return h;
    }

    // --- Saving and loading ---

    [Fact]
    public void Save_OnlyTheLayerIsPartOfTheState()
    {
        string[] members = [.. typeof(RoadsState).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(static p => p.Name).Order()];

        members.ShouldBe(["Height", "Layer", "Width"], "fields and lookups are derived data and must not be saved");
    }

    [Fact]
    public void Load_FieldsAndLookupsAreRebuiltFromTheLayerAndTheRegistry_WithoutReplayingTheLifecycle()
    {
        RoadsHarness h = City();
        RoadsSystem reloaded = h.Reload();

        // The reloaded system was never subscribed to the lifecycle and saw no command, yet it must answer exactly alike.
        DistanceFieldTests.AssertSameAnswers(h, h.Roads, reloaded);
        h.Network.FieldCount.ShouldBe(reloaded.Network.FieldCount);
        reloaded.Network.FieldCount.ShouldBe(5);
        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                reloaded.Queries.RoadAt(x, y).ShouldBe(h.Queries.RoadAt(x, y));
            }
        }
    }

    [Fact]
    public void Load_ThroughTheStateSerializer_KeepsTheLayerAndTheHash()
    {
        RoadsHarness h = City();
        ulong hash = h.StateHash();

        WorldState restored = StateSerializer.Read(StateSerializer.Write(h.Sim.World), h.Sim.Content);

        restored.Roads.Layer.ShouldBe(h.Sim.World.Roads.Layer);
        (restored.Roads.Width, restored.Roads.Height).ShouldBe((48, 48));
        StateSerializer.ComputeHash(restored).ShouldBe(hash);
    }

    [Fact]
    public void Load_ThenFurtherChanges_BehaveLikeTheOriginal()
    {
        RoadsHarness h = City();
        RoadsSystem reloaded = h.Reload();
        EntityId hut = h.Sim.Queries.Map.AllBuildings.Last(b => b.Type == RoadContent.Building("hut")).Id;
        reloaded.Queries.IsInStorehouseReach(hut).ShouldBeTrue();

        // Cut the same road in the original; the reloaded system shares the map but has its own layer, so cut it there too.
        h.RemoveRoads((33, 10));
        reloaded.Network.RemoveRoads(h.Sim.Context, [new TileCoord(33, 10)]);

        h.Queries.IsInStorehouseReach(hut).ShouldBe(reloaded.Queries.IsInStorehouseReach(hut));
        DistanceFieldTests.AssertSameAnswers(h, h.Roads, reloaded);
    }

    [Fact]
    public void Load_ALayerThatDoesNotMatchItsSize_IsRefused()
    {
        ContentDb content = WorldBuilder.Content;
        foreach (RoadsState bad in new RoadsState[]
        {
            new() { Width = 2, Height = 2, Layer = new ushort[3] },
            new() { Width = -1, Height = -4, Layer = [] },
            new() { Width = 0, Height = 0, Layer = new ushort[1] },
        })
        {
            Should.Throw<InvalidDataException>(() => new RoadsSystem(bad, content, () => throw new InvalidOperationException()));
        }
    }

    [Fact]
    public void Restore_ARealSimulationWithRoads_KeepsThemAndRefusesADamagedLayer()
    {
        Simulation sim = new WorldBuilder().Build();
        BuildingTypeId dirt = WorldBuilder.Content.Buildings.IdOf("dirt_road");
        ushort[] layer = new ushort[16];
        layer[5] = (ushort)(dirt.Value + 1);
        layer[6] = (ushort)(dirt.Value + 1);
        sim.World.Roads = new RoadsState { Width = 4, Height = 4, Layer = layer };

        var restored = Simulation.Restore(WorldBuilder.Content, sim.SerializeState());

        restored.ComputeStateHash().ShouldBe(sim.ComputeStateHash());
        restored.Query.Roads.RoadAt(1, 1).ShouldBe(dirt);
        restored.Query.Roads.RoadAt(2, 1).ShouldBe(dirt);
        restored.Query.Roads.RoadAt(3, 1).ShouldBeNull();

        sim.World.Roads = new RoadsState { Width = 4, Height = 4, Layer = new ushort[15] };
        Should.Throw<InvalidDataException>(() => Simulation.Restore(WorldBuilder.Content, sim.SerializeState()));
    }

    [Fact]
    public void Load_ATileWhoseRoadTypeIsNoLongerARoad_ReadsAsNoRoad()
    {
        var h = new RoadsHarness();
        BuildingTypeId storehouse = RoadContent.Building("storehouse");
        var state = new RoadsState { Width = 32, Height = 32, Layer = new ushort[32 * 32] };
        state.Layer[(4 * 32) + 4] = (ushort)(storehouse.Value + 1);
        state.Layer[(4 * 32) + 5] = (ushort)(RoadsHarness.Dirt.Value + 1);
        state.Layer[(4 * 32) + 6] = ushort.MaxValue;
        var system = new RoadsSystem(state, h.Sim.Content, () => h.Sim.Queries);

        system.Queries.RoadAt(4, 4).ShouldBeNull();
        system.Queries.RoadAt(5, 4).ShouldBe(RoadsHarness.Dirt);
        system.Queries.RoadAt(6, 4).ShouldBeNull();
    }

    // --- Determinism ---

    [Fact]
    public void Hash_TheSameCommandsInTwoWorlds_GiveTheSameHashAndTheSameEvents()
    {
        List<SimEvent> Run(out ulong hash)
        {
            RoadsHarness h = City();
            var events = new List<SimEvent>();
            events.AddRange(h.Place(RoadsHarness.Paved, (2, 3), (3, 3)));
            events.AddRange(h.RemoveRoads((5, 10)));
            events.AddRange(h.Place(RoadsHarness.Dirt, (5, 10)));
            hash = h.StateHash();
            return events;
        }

        List<SimEvent> first = Run(out ulong a);
        List<SimEvent> second = Run(out ulong b);

        a.ShouldBe(b);
        second.ShouldBe(first);
    }

    [Fact]
    public void Hash_DifferentRoads_GiveDifferentHashes()
    {
        var one = new RoadsHarness();
        var two = new RoadsHarness();
        var three = new RoadsHarness();
        one.PlaceDirt((3, 3), (4, 3));
        two.PlaceDirt((3, 3), (4, 4));
        three.Place(RoadsHarness.Dirt, (3, 3));

        new[] { one.StateHash(), two.StateHash(), three.StateHash() }.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void Hash_RoadsLaidInAnotherOrder_GiveTheSameHash()
    {
        var forward = new RoadsHarness();
        var backward = new RoadsHarness();
        forward.Unlock(RoadsHarness.Cobbled);
        backward.Unlock(RoadsHarness.Cobbled);

        forward.DirtLine((2, 2), (9, 2));
        forward.Line(RoadsHarness.Cobbled, (9, 3), (9, 9));
        backward.Line(RoadsHarness.Cobbled, (9, 9), (9, 3));
        backward.DirtLine((9, 2), (2, 2));

        backward.StateHash().ShouldBe(forward.StateHash());
    }

    [Fact]
    public void Hash_AskingEveryQuery_DoesNotChangeIt()
    {
        RoadsHarness h = City();
        ulong before = h.StateHash();

        foreach (BuildingInfo b in h.Sim.Queries.Map.AllBuildings)
        {
            h.Queries.HasRoadAccess(b.Id);
            h.Queries.IsInStorehouseReach(b.Id);
            h.Queries.TryNearestStorehouse(b.Id, out _, out _);
            h.Queries.IsCovered(b.Id, Evenmere.Content.Definitions.ServiceKind.Market);
        }

        h.Queries.TryFindViewPath(new TileCoord(0, 10), new TileCoord(40, 10), []);
        h.Roads.Placement.ValidateRoad([new TileCoord(1, 1), new TileCoord(99, 99)], RoadsHarness.Dirt);

        h.StateHash().ShouldBe(before);
    }

    [Fact]
    public void Hash_AfterALongRandomRunOfCommands_IsTheSameInTwoWorlds()
    {
        ulong Run(int seed, out int rejected)
        {
            var random = new Random(seed);
            RoadsHarness h = City();
            h.Unlock(RoadsHarness.Cobbled, RoadsHarness.Paved);
            BuildingTypeId[] types = [RoadsHarness.Dirt, RoadsHarness.Cobbled, RoadsHarness.Paved];
            rejected = 0;
            for (int i = 0; i < 300; i++)
            {
                (int X, int Y) start = (random.Next(48), random.Next(48));
                (int X, int Y) end = random.Next(2) == 0 ? (random.Next(48), start.Y) : (start.X, random.Next(48));
                IReadOnlyList<SimEvent> events = random.Next(3) == 0
                    ? h.Send(new RemoveRoad(RoadsHarness.Straight(start, end)))
                    : h.Send(new PlaceRoad(RoadsHarness.Straight(start, end), types[random.Next(3)]));
                rejected += events.OfType<CommandRejected>().Count();
            }

            return h.StateHash();
        }

        Run(11, out int rejectedA).ShouldBe(Run(11, out int rejectedB));
        rejectedA.ShouldBe(rejectedB);
        Run(11, out _).ShouldNotBe(Run(12, out _));
    }

    [Fact]
    public void Hash_OfTheSavedLayout_IsPinned()
    {
        // The bytes of the roads state are the save format and part of the state hash. If this changes, the layout of
        // RoadsState changed: that needs an ADR and a save migration, not just a new number here.
        ushort[] layer = new ushort[64];
        layer[9] = 1;
        layer[10] = 3;
        layer[27] = 2;
        layer[63] = 65535;
        var state = new RoadsState { Width = 8, Height = 8, Layer = layer };

        var hasher = new StableHasher();
        hasher.Add(MessagePackSerializer.Serialize(state, SimResolver.Options, TestContext.Current.CancellationToken));

        hasher.ToUInt64().ShouldBe(0xEC5E3205C8487E30UL);
    }
}
