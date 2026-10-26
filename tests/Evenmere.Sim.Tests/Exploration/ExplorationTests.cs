using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Maps;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Debug.Commands;
using Evenmere.Sim.Systems.Exploration;
using Evenmere.Sim.Systems.Exploration.Commands;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Sim.Tests.Integration;
using Evenmere.Sim.Tests.Persistence;

namespace Evenmere.Sim.Tests.Exploration;

/// <summary>
/// Exploration (M2-06): fog, expeditions and discoveries. New games reveal the start region plus its neighbours;
/// expeditions reveal one adjacent region after their duration; discovery effects apply exactly once.
/// </summary>
public sealed class ExplorationTests
{
    [Fact]
    public void NewGame_RevealsStartPlusNeighbours()
    {
        Simulation sim = SiteFinder.NewGame();
        sim.Tick();

        ushort hallRegion = HallRegion(sim);
        sim.Query.Exploration.IsRevealed(hallRegion).ShouldBeTrue();
        ushort[] revealed = sim.Query.Exploration.RevealedRegions();
        revealed.ShouldContain(hallRegion);

        // A far region stays fogged: the starter island's shoals (region 9) do not border the landing.
        ushort far = FarRegion(sim, revealed);
        sim.Query.Exploration.IsRevealed(far).ShouldBeFalse();
    }

    [Fact]
    public void Placement_OnFoggedTile_IsRejectedWithFogged()
    {
        Simulation sim = SiteFinder.NewGame();
        sim.Tick();

        ushort[] revealed = sim.Query.Exploration.RevealedRegions();
        (int X, int Y) fogged = FoggedTile(sim, revealed);
        BuildingTypeId house = SiteFinder.Content.IdOf<BuildingTypeId>("settler_house");

        PlacementResult result = sim.Query.Placement.Validate(house, fogged.X, fogged.Y, Rotation.Deg0);
        result.CanPlace.ShouldBeFalse();
        result.Has(PlacementIssue.Fogged).ShouldBeTrue();
    }

    [Fact]
    public void Placement_OnRevealedTile_PassesFog()
    {
        Simulation sim = SiteFinder.NewGame();
        sim.Tick();

        (int X, int Y) spot = RevealedTile(sim);
        BuildingTypeId house = SiteFinder.Content.IdOf<BuildingTypeId>("settler_house");

        PlacementResult result = sim.Query.Placement.Validate(house, spot.X, spot.Y, Rotation.Deg0);
        result.Has(PlacementIssue.Fogged).ShouldBeFalse();
    }

    [Fact]
    public void Expedition_RevealsRegionOnTheRightTick_AndCountsOnce()
    {
        Simulation sim = SiteFinder.NewGame();
        sim.CheatsUsed = true;
        sim.Tick();

        EntityId lodge = PlaceFinishedLodge(sim);
        ushort target = AdjacentFoggedRegion(sim);
        long expeditionsBefore = sim.Query.Progression.Counter(CounterKey.Expeditions);

        sim.Enqueue(new StartExpedition(lodge, target));
        sim.Tick();
        sim.DrainEvents();

        ExpeditionView? view = sim.Query.Exploration.ExpeditionForLodge(lodge);
        view.ShouldNotBeNull();
        view.Value.TargetRegion.ShouldBe(target);
        int daysLeft = view.Value.DaysLeft;

        // One expedition per lodge: a second start is rejected while one is under way.
        sim.Enqueue(new StartExpedition(lodge, target));
        sim.Tick();
        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
        sim.Query.Exploration.ExpeditionForLodge(lodge).ShouldNotBeNull();

        // Tick to the morning of the reveal: daysLeft day-starts later the region opens.
        for (int i = 0; i < daysLeft; i++)
        {
            TickToNextDayStart(sim);
        }

        sim.Query.Exploration.IsRevealed(target).ShouldBeTrue();
        sim.Query.Exploration.ExpeditionForLodge(lodge).ShouldBeNull();
        sim.Query.Progression.Counter(CounterKey.Expeditions).ShouldBe(expeditionsBefore + 1);

        // Already revealed: starting again is rejected.
        sim.Enqueue(new StartExpedition(lodge, target));
        sim.Tick();
        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void Expedition_NonAdjacentRegion_IsRejected()
    {
        Simulation sim = SiteFinder.NewGame();
        sim.CheatsUsed = true;
        sim.Tick();

        EntityId lodge = PlaceFinishedLodge(sim);
        ushort far = NonAdjacentFoggedRegion(sim);

        sim.Enqueue(new StartExpedition(lodge, far));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void Discovery_AppliesExactlyOnce()
    {
        // The generated island carries placed features; revealing every region applies each once, and revealing
        // again grants nothing more.
        ContentDb content = SiteFinder.Content;
        MapDef island = IslandGenerator.Generate(7, IslandGenerator.DefaultSize, content);
        island.Discoveries.Length.ShouldBeGreaterThan(0);
        var sim = Simulation.CreateNew(content, new NewGameOptions { Seed = 7, Map = island });
        sim.CheatsUsed = true;
        sim.Tick();

        sim.Enqueue(new CheatRevealAll(CheatRevealAll.AllRegions));
        sim.Tick();
        sim.DrainEvents();

        long discovered = 0;
        foreach (DiscoveryKind kind in Enum.GetValues<DiscoveryKind>())
        {
            discovered += sim.Query.Progression.Counter(CounterKey.Discovered(kind));
        }

        discovered.ShouldBe(island.Discoveries.Length + DepositRegions(island));

        Amount timberBefore = sim.Query.Storage.Stock(content.IdOf<GoodId>("timber"));
        sim.Enqueue(new CheatRevealAll(CheatRevealAll.AllRegions));
        sim.Tick();
        sim.DrainEvents();
        sim.Query.Storage.Stock(content.IdOf<GoodId>("timber")).ShouldBe(timberBefore);
    }

    [Fact]
    public void SaveRoundTrip_MidExpedition_ContinuesDeterministically()
    {
        Simulation sim = SiteFinder.NewGame();
        sim.CheatsUsed = true;
        sim.Tick();

        EntityId lodge = PlaceFinishedLodge(sim);
        ushort target = AdjacentFoggedRegion(sim);
        sim.Enqueue(new StartExpedition(lodge, target));
        sim.Tick();
        sim.DrainEvents();
        sim.Query.Exploration.ExpeditionForLodge(lodge).ShouldNotBeNull();

        var restored = Simulation.Restore(SiteFinder.Content, sim.SerializeState());
        restored.CheatsUsed = true;

        ExpeditionView? before = sim.Query.Exploration.ExpeditionForLodge(lodge);
        ExpeditionView? after = restored.Query.Exploration.ExpeditionForLodge(lodge);
        after.ShouldBe(before);

        // Both continue to the same hash after the same ticks.
        const int Ticks = 500;
        for (int i = 0; i < Ticks; i++)
        {
            sim.Tick();
            restored.Tick();
        }

        restored.ComputeStateHash().ShouldBe(sim.ComputeStateHash());
    }

    [Fact]
    public void OldSaves_LoadFullyExplored()
    {
        // Saves written before fog have no exploration state: loading reveals every region, so every existing
        // building's region is revealed, and the load stays lossless.
        foreach (string fixture in new[] { SaveFixtureTests.FixturePath, SaveFixtureTests.CurrentFixturePath })
        {
            if (!File.Exists(fixture))
            {
                continue;
            }

            using FileStream stream = File.OpenRead(fixture);
            (ISimulation simulation, SaveReport report) = SaveGame.Read(stream, SiteFinder.Content);
            report.IsLossless.ShouldBeTrue();

            // Pre-fog saves reveal on the first run after load; tick once so the fog lifts before checking.
            simulation.Tick();
            simulation.DrainEvents();

            foreach (BuildingInfo building in simulation.Query.Map.Buildings)
            {
                ushort region = simulation.Query.Map.GetTile(building.Origin.X, building.Origin.Y).Region;
                simulation.Query.Exploration.IsRevealed(region).ShouldBeTrue($"building {building.Id.Value} in region {region}");
            }
        }
    }

    private static ushort HallRegion(Simulation sim)
    {
        BuildingTypeId hall = SiteFinder.Content.Required.FoundersHall;
        foreach (BuildingInfo building in sim.Query.Map.Buildings)
        {
            if (building.Type == hall)
            {
                return sim.Query.Map.GetTile(building.Origin.X, building.Origin.Y).Region;
            }
        }

        throw new InvalidOperationException("The new game holds no Founders' Hall.");
    }

    private static ushort FarRegion(Simulation sim, ushort[] revealed)
    {
        HashSet<ushort> seen = new(revealed);
        for (int y = 0; y < sim.Query.Map.Height; y++)
        {
            for (int x = 0; x < sim.Query.Map.Width; x++)
            {
                ushort region = sim.Query.Map.GetTile(x, y).Region;
                if (!seen.Contains(region))
                {
                    return region;
                }
            }
        }

        throw new InvalidOperationException("The starter island is fully revealed; expected fog somewhere.");
    }

    private static (int X, int Y) FoggedTile(Simulation sim, ushort[] revealed)
    {
        HashSet<ushort> open = new(revealed);
        for (int y = 0; y < sim.Query.Map.Height; y++)
        {
            for (int x = 0; x < sim.Query.Map.Width; x++)
            {
                if (!open.Contains(sim.Query.Map.GetTile(x, y).Region))
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("No fogged tile found.");
    }

    private static (int X, int Y) RevealedTile(Simulation sim)
    {
        // Beside the hall: the start region is revealed and the hall's neighbours are occupied or reserved,
        // so scan the start region for a buildable spot the placement check accepts apart from fog.
        BuildingTypeId house = SiteFinder.Content.IdOf<BuildingTypeId>("settler_house");
        for (int y = 0; y < sim.Query.Map.Height; y++)
        {
            for (int x = 0; x < sim.Query.Map.Width; x++)
            {
                if (!sim.Query.Exploration.IsRevealed(sim.Query.Map.GetTile(x, y).Region))
                {
                    continue;
                }

                PlacementResult result = sim.Query.Placement.Validate(house, x, y, Rotation.Deg0);
                if (!result.Has(PlacementIssue.Fogged) && !result.Has(PlacementIssue.Occupied) && !result.Has(PlacementIssue.OutOfBounds))
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("No revealed tile found.");
    }

    private static EntityId PlaceFinishedLodge(Simulation sim)
    {
        ContentDb content = SiteFinder.Content;
        TechId pathfinding = content.IdOf<TechId>("pathfinding");
        sim.Enqueue(new CheatCompleteNode(pathfinding, null));
        sim.Tick();
        sim.DrainEvents();

        // The expedition test is about expeditions, not construction: register the lodge finished (as new-game
        // setup does for the hall) so no roads, materials or workforce are needed. Spiral out from the hall so it
        // lands in the revealed start.
        BuildingTypeId hallType = content.Required.FoundersHall;
        TileCoord hall = default;
        foreach (BuildingInfo existing in sim.Query.Map.Buildings)
        {
            if (existing.Type == hallType)
            {
                hall = existing.Origin;
                break;
            }
        }

        BuildingTypeId lodge = content.IdOf<BuildingTypeId>("scouts_lodge");
        for (int ring = 0; ring < 64; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    int x = hall.X + dx;
                    int y = hall.Y + dy;
                    if (x < 0 || y < 0 || x + 1 >= sim.Query.Map.Width || y + 1 >= sim.Query.Map.Height)
                    {
                        continue;
                    }

                    PlacementResult check = sim.Query.Placement.Validate(lodge, x, y, Rotation.Deg0);
                    if (!check.CanPlace)
                    {
                        continue;
                    }

                    return sim.Context.BuildingRegistry.Add(lodge, x, y, Rotation.Deg0, BuildingState.Active);
                }
            }
        }

        throw new InvalidOperationException("No placeable spot for a Scout's Lodge in the revealed start.");
    }

    private static ushort AdjacentFoggedRegion(Simulation sim)
    {
        HashSet<ushort> revealed = new(sim.Query.Exploration.RevealedRegions());
        for (int y = 0; y < sim.Query.Map.Height; y++)
        {
            for (int x = 0; x < sim.Query.Map.Width; x++)
            {
                ushort region = sim.Query.Map.GetTile(x, y).Region;
                if (revealed.Contains(region))
                {
                    continue;
                }

                if (BordersRevealed(sim, region, revealed))
                {
                    return region;
                }
            }
        }

        throw new InvalidOperationException("No adjacent fogged region found.");
    }

    private static ushort NonAdjacentFoggedRegion(Simulation sim)
    {
        HashSet<ushort> revealed = new(sim.Query.Exploration.RevealedRegions());
        foreach (ushort region in AllRegions(sim))
        {
            if (revealed.Contains(region))
            {
                continue;
            }

            if (!BordersRevealed(sim, region, revealed))
            {
                return region;
            }
        }

        // The starter island is small: every fogged region may border the start. Fall back to any fogged region
        // and let the caller assert the rejection only when it truly is non-adjacent.
        foreach (ushort region in AllRegions(sim))
        {
            if (!revealed.Contains(region))
            {
                return region;
            }
        }

        throw new InvalidOperationException("No fogged region found.");
    }

    private static bool BordersRevealed(Simulation sim, ushort region, HashSet<ushort> revealed)
    {
        int width = sim.Query.Map.Width;
        int height = sim.Query.Map.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (sim.Query.Map.GetTile(x, y).Region != region)
                {
                    continue;
                }

                if (x > 0 && revealed.Contains(sim.Query.Map.GetTile(x - 1, y).Region))
                {
                    return true;
                }

                if (x < width - 1 && revealed.Contains(sim.Query.Map.GetTile(x + 1, y).Region))
                {
                    return true;
                }

                if (y > 0 && revealed.Contains(sim.Query.Map.GetTile(x, y - 1).Region))
                {
                    return true;
                }

                if (y < height - 1 && revealed.Contains(sim.Query.Map.GetTile(x, y + 1).Region))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HashSet<ushort> AllRegions(Simulation sim)
    {
        HashSet<ushort> regions = new();
        for (int y = 0; y < sim.Query.Map.Height; y++)
        {
            for (int x = 0; x < sim.Query.Map.Width; x++)
            {
                regions.Add(sim.Query.Map.GetTile(x, y).Region);
            }
        }

        return regions;
    }

    private static void TickToNextDayStart(Simulation sim)
    {
        do
        {
            sim.Tick();
        }
        while (sim.CurrentTick % 120 != 0);
        sim.DrainEvents();
    }

    private static int DepositRegions(MapDef island)
    {
        HashSet<(ushort Region, byte Deposit)> seen = new();
        for (int i = 0; i < island.TileCount; i++)
        {
            if (island.Deposit[i] != 0)
            {
                seen.Add((island.Region[i], island.Deposit[i]));
            }
        }

        // One counter per distinct deposit kind per revealed region; all regions are revealed here, so count kinds per region.
        HashSet<(ushort Region, int Kind)> perRegionKind = new();
        foreach ((ushort region, byte deposit) in seen)
        {
            perRegionKind.Add((region, deposit));
        }

        return perRegionKind.Count;
    }
}
