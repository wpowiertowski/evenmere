using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Map;

/// <summary>The property test of M1-01: random operations against a shadow model, checking the registry and the occupancy grid never drift apart.</summary>
public sealed class MapConsistencyTests
{
    private const int Width = 40;
    private const int Height = 36;

    private static readonly BuildingTypeId[] _types =
    [
        MapWorld.Type("settler_house"), MapWorld.Type("founders_hall"), MapWorld.Type("long_shed"), MapWorld.Type("long_barn"), MapWorld.Type("dirt_road"),
    ];

    // What a building should look like, kept by the test independently of the registry.
    private sealed record Shadow(BuildingTypeId Type, int X, int Y, Rotation Rotation, int Width, int Height, BuildingState State, int Stage = 0);

    private sealed class Model
    {
        public SortedDictionary<int, Shadow> Buildings { get; } = [];

        public int[] Grid { get; } = new int[Width * Height];

        public bool Fits(int x, int y, int width, int height)
        {
            if (x < 0 || y < 0 || x + width > Width || y + height > Height)
            {
                return false;
            }

            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    if (Grid[((y + dy) * Width) + x + dx] != 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void Fill(Shadow b, int id)
        {
            for (int dy = 0; dy < b.Height; dy++)
            {
                for (int dx = 0; dx < b.Width; dx++)
                {
                    Grid[((b.Y + dy) * Width) + b.X + dx] = id;
                }
            }
        }
    }

    private static (int Width, int Height) FootprintOf(BuildingTypeId type, Rotation rotation)
    {
        Footprint footprint = rotation.Rotate(MapWorld.Content.Get(type).Footprint);
        return (footprint.Width, footprint.Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomAddRemoveRotate_ThousandOperations_KeepOccupancyAndRegistryConsistent(int seed)
    {
        var world = MapWorld.Plains(Width, Height);
        var model = new Model();
        var random = new Random(seed);
        int refused = 0;
        int performed = 0;

        for (int step = 0; step < 1000; step++)
        {
            switch (random.Next(10))
            {
                case <= 4:
                    if (!TryAdd(world, model, random))
                    {
                        refused++;
                    }

                    break;
                case <= 6:
                    Remove(world, model, random);
                    break;
                case <= 8:
                    Rotate(world, model, random);
                    break;
                default:
                    ChangeState(world, model, random);
                    break;
            }

            performed++;
            if (step % 25 == 0)
            {
                AssertConsistent(world, model);
            }
        }

        AssertConsistent(world, model);
        performed.ShouldBe(1000);

        // The run must have exercised both outcomes of an add: placements that fit and placements that were refused.
        model.Buildings.Count.ShouldBeGreaterThan(5);
        refused.ShouldBeGreaterThan(0);

        // The saved form holds exactly the same, and a restored world answers the same questions.
        MapWorld restored = world.SaveAndRestore();
        restored.Sim.ComputeStateHash().ShouldBe(world.Sim.ComputeStateHash());
        AssertConsistent(restored, model);
    }

    private static bool TryAdd(MapWorld world, Model model, Random random)
    {
        BuildingTypeId type = _types[random.Next(_types.Length)];
        var rotation = (Rotation)random.Next(4);
        (int width, int height) = FootprintOf(type, rotation);
        int x = random.Next(-1, Width + 1);
        int y = random.Next(-1, Height + 1);
        var state = (BuildingState)new[] { 0, 1, 3 }[random.Next(3)];
        int nextId = world.Sim.World.EntityIds.NextValue;

        if (!model.Fits(x, y, width, height))
        {
            Should.Throw<ArgumentException>(() => world.Registry.Add(type, x, y, rotation, state));
            world.Sim.World.EntityIds.NextValue.ShouldBe(nextId);
            return false;
        }

        EntityId id = world.Registry.Add(type, x, y, rotation, state);
        id.Value.ShouldBe(nextId);
        var shadow = new Shadow(type, x, y, rotation, width, height, state);
        model.Buildings.Add(id.Value, shadow);
        model.Fill(shadow, id.Value);
        return true;
    }

    private static void Remove(MapWorld world, Model model, Random random)
    {
        if (model.Buildings.Count == 0 || random.Next(8) == 0)
        {
            world.Registry.Remove(new EntityId(1_000_000 + random.Next(1000))).ShouldBeFalse();
            return;
        }

        int id = model.Buildings.Keys.ElementAt(random.Next(model.Buildings.Count));
        model.Fill(model.Buildings[id], 0);
        model.Buildings.Remove(id);
        world.Registry.Remove(new EntityId(id)).ShouldBeTrue();
    }

    // The registry has no rotate operation: turning a building is removing it and adding it again with the next rotation.
    private static void Rotate(MapWorld world, Model model, Random random)
    {
        if (model.Buildings.Count == 0)
        {
            return;
        }

        int id = model.Buildings.Keys.ElementAt(random.Next(model.Buildings.Count));
        Shadow old = model.Buildings[id];
        model.Fill(old, 0);
        model.Buildings.Remove(id);
        world.Registry.Remove(new EntityId(id)).ShouldBeTrue();

        Rotation turned = old.Rotation.Next();
        (int width, int height) = FootprintOf(old.Type, turned);
        Rotation chosen = model.Fits(old.X, old.Y, width, height) ? turned : old.Rotation;
        (width, height) = FootprintOf(old.Type, chosen);

        EntityId again = world.Registry.Add(old.Type, old.X, old.Y, chosen, old.State == BuildingState.Paused ? BuildingState.Active : old.State);
        var shadow = new Shadow(old.Type, old.X, old.Y, chosen, width, height, old.State == BuildingState.Paused ? BuildingState.Active : old.State);
        model.Buildings.Add(again.Value, shadow);
        model.Fill(shadow, again.Value);
    }

    private static void ChangeState(MapWorld world, Model model, Random random)
    {
        if (model.Buildings.Count == 0)
        {
            return;
        }

        int id = model.Buildings.Keys.ElementAt(random.Next(model.Buildings.Count));
        Shadow building = model.Buildings[id];
        switch (building.State)
        {
            case BuildingState.Site when random.Next(2) == 0:
                int stage = random.Next(11);
                world.Registry.SetStage(new EntityId(id), stage);
                model.Buildings[id] = building with { Stage = stage };
                break;
            case BuildingState.Site:
                world.Registry.SetState(new EntityId(id), BuildingState.Active);
                model.Buildings[id] = building with { State = BuildingState.Active, Stage = 0 };
                break;
            case BuildingState.Active:
                world.Registry.SetState(new EntityId(id), BuildingState.Paused);
                model.Buildings[id] = building with { State = BuildingState.Paused };
                break;
            case BuildingState.Paused:
                world.Registry.SetState(new EntityId(id), BuildingState.Active);
                model.Buildings[id] = building with { State = BuildingState.Active };
                break;
            default:
                // A ruin has no transitions the registry promises, and a stage means nothing to it.
                world.Registry.SetStage(new EntityId(id), random.Next(5));
                break;
        }
    }

    private static void AssertConsistent(MapWorld world, Model model)
    {
        // Every tile agrees with the shadow grid.
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                world.Queries.BuildingAt(x, y).Value.ShouldBe(model.Grid[(y * Width) + x], $"tile ({x}, {y})");
            }
        }

        // The registry lists exactly the shadow's buildings, in ID order, with the same fields.
        List<BuildingInfo> all = world.AllBuildings();
        all.Select(b => b.Id.Value).ShouldBe([.. model.Buildings.Keys]);
        foreach (BuildingInfo info in all)
        {
            Shadow shadow = model.Buildings[info.Id.Value];
            info.Type.ShouldBe(shadow.Type);
            info.Origin.ShouldBe(new TileCoord(shadow.X, shadow.Y));
            info.Rotation.ShouldBe(shadow.Rotation);
            info.Width.ShouldBe(shadow.Width);
            info.Height.ShouldBe(shadow.Height);
            info.State.ShouldBe(shadow.State);
            info.Stage.ShouldBe(shadow.Stage);
            world.Queries.TryGetBuilding(info.Id, out BuildingInfo single).ShouldBeTrue();
            single.ShouldBe(info);
        }

        world.ActiveBuildings().Select(b => b.Id.Value)
            .ShouldBe([.. model.Buildings.Where(e => e.Value.State is BuildingState.Active or BuildingState.Paused).Select(e => e.Key)]);

        // The saved form is self-consistent: this is the same check a restored save goes through.
        world.Sim.World.Map.Validate(MapWorld.Content, world.Sim.World.EntityIds.NextValue);
    }
}
