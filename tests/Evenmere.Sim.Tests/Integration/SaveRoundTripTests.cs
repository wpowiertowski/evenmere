using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction.Commands;
using Evenmere.Sim.Systems.Population.Commands;
using Evenmere.Sim.Systems.Roads.Commands;

namespace Evenmere.Sim.Tests.Integration;

/// <summary>A save round trip mid-scenario: serialising, restoring and continuing reaches the same hash as an uninterrupted run.</summary>
public sealed class SaveRoundTripTests
{
    [Fact]
    public void SaveRoundTrip_MidScenario_RestoreAndContinue_MatchesUninterrupted()
    {
        ulong direct = RunTo(ticks: 2400, restoreAt: -1);
        ulong resumed = RunTo(ticks: 2400, restoreAt: 1200);

        resumed.ShouldBe(direct);
    }

    // Plays a small settlement (a road, four houses, a market and a fishing hut, locked against advancing) to the given
    // tick. When restoreAt is not negative, the world is serialised and restored on that tick and the run continues from
    // the restored simulation, so any state the serialisation drops or scrambles changes the hash.
    private static ulong RunTo(int ticks, int restoreAt)
    {
        Simulation sim = SiteFinder.NewGame();
        Evenmere.Content.ContentDb content = SiteFinder.Content;
        BuildingTypeId road = content.Required.DirtRoad;

        for (int tick = 1; tick <= ticks; tick++)
        {
            if (tick == 1)
            {
                List<TileCoord> path = [];
                for (int rx = 14; rx <= 40; rx++)
                {
                    path.Add(new TileCoord(rx, 95));
                }

                sim.Enqueue(new PlaceRoad([.. path], road));
                sim.Enqueue(new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 20, 96, Rotation.Deg0));
                sim.Enqueue(new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 23, 96, Rotation.Deg0));
                sim.Enqueue(new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 26, 96, Rotation.Deg0));
                sim.Enqueue(new PlaceBuilding(content.IdOf<BuildingTypeId>("settler_house"), 20, 99, Rotation.Deg0));
                sim.Enqueue(new PlaceBuilding(content.IdOf<BuildingTypeId>("market"), 26, 101, Rotation.Deg0));
                sim.Enqueue(new PlaceBuilding(content.IdOf<BuildingTypeId>("fishing_hut"), 5, 99, Rotation.Deg0));
            }

            if (tick == 300)
            {
                for (int id = 2; id <= 5; id++)
                {
                    sim.Enqueue(new SetHouseUpgradeLock(new EntityId(id), true));
                }
            }

            sim.Tick();

            if (tick == restoreAt)
            {
                byte[] bytes = sim.SerializeState();
                sim = Simulation.Restore(content, bytes);
            }
        }

        return sim.ComputeStateHash();
    }
}
