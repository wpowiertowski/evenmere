using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Services;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Tests.Production;

public sealed class ProductionStateTests
{
    // --- Lifecycle ---

    [Fact]
    public void Lifecycle_ActivatingAProducer_CreatesItsRecord()
    {
        var h = ProductionHarness.Create();

        EntityId farm = h.Add("sheep_farm", 2, 2);

        h.System.Queries.TryGet(farm, out ProductionInfo info).ShouldBeTrue();
        info.Recipe.ShouldBe(h.Content.IdOf<RecipeId>("shear_wool"));
        info.Stall.ShouldBe(StallReason.None);
        info.OutputBuffer.Single().Good.ShouldBe(h.Good("wool"));
    }

    [Fact]
    public void Lifecycle_BuildingsThatAreNoProducers_HaveNoRecord()
    {
        var h = ProductionHarness.Create();
        EntityId house = h.Add("settler_house", 2, 2);
        EntityId storehouse = h.Add("storehouse", 6, 2);

        h.System.Queries.TryGet(house, out _).ShouldBeFalse();
        h.System.Queries.TryGet(storehouse, out _).ShouldBeFalse();
        h.System.Queries.TryGet(new EntityId(999), out _).ShouldBeFalse();
    }

    [Fact]
    public void Lifecycle_ASiteBecomesAProducerOnlyWhenItIsFinished()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.Add("sheep_farm", 2, 2, BuildingState.Site);
        h.System.Queries.TryGet(farm, out _).ShouldBeFalse();

        h.Sim.Services.BuildingRegistry.SetState(farm, BuildingState.Active);

        h.System.Queries.TryGet(farm, out _).ShouldBeTrue();
    }

    [Fact]
    public void Lifecycle_RemovingAProducer_DropsItsRecordAndItsGoods()
    {
        var h = ProductionHarness.Create();
        EntityId farm = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(240);

        h.Sim.Services.BuildingRegistry.Remove(farm);

        h.System.Queries.TryGet(farm, out _).ShouldBeFalse();
        h.Context.ProductionBuffers.TryTakeOutput(farm, h.Good("wool"), Amount.FromUnits(1)).ShouldBeFalse();
        h.State.Producers.Count.ShouldBe(0);
        h.RunTicks(10);
    }

    [Fact]
    public void Lifecycle_ATypeChangeToAnotherRecipe_StartsTheNewRecipeFromScratch()
    {
        var h = ProductionHarness.Create();
        EntityId building = h.AddReady("sawpit", 2, 2);
        h.Context.ProductionBuffers.AddInput(building, h.Good("wood"), Amount.FromUnits(2));
        h.RunTicks(30);

        h.Sim.Services.BuildingRegistry.SetType(building, h.Type("weavers_cottage"));

        ProductionInfo info = h.Info(building);
        info.Recipe.ShouldBe(h.Content.IdOf<RecipeId>("weave_homespun"));
        info.InputBuffer.ShouldBe([new GoodAmount(h.Good("wool"), Amount.Zero)]);
        info.OutputBuffer.ShouldBe([new GoodAmount(h.Good("homespun"), Amount.Zero)]);
        info.CycleProgress.PerMille.ShouldBe(0);
    }

    [Fact]
    public void Lifecycle_ATypeChangeKeepingTheRecipe_KeepsTheGoodsAndTheProgress()
    {
        var h = ProductionHarness.Create(ExtraContent.Db);
        EntityId building = h.AddReady("sheep_farm", 2, 2);
        h.RunTicks(60);
        h.Info(building).CycleProgress.PerMille.ShouldBe(500);

        // The lookout runs the same recipe as the farm.
        h.Sim.Services.BuildingRegistry.SetType(building, h.Type("lookout"));

        h.Info(building).CycleProgress.PerMille.ShouldBe(500);
        h.RunTicks(60);
        h.Output(building, "wool").ShouldBe(Amount.FromUnits(1));
    }

    [Fact]
    public void Lifecycle_ATypeChangeToANonProducer_DropsTheRecord()
    {
        var h = ProductionHarness.Create();
        EntityId building = h.AddReady("sheep_farm", 2, 2);

        h.Sim.Services.BuildingRegistry.SetType(building, h.Type("storehouse"));

        h.System.Queries.TryGet(building, out _).ShouldBeFalse();
    }

    // --- Save and restore ---

    // Runs a small island for a number of ticks, the way Logistics would drive it: every 40 ticks a cart takes full loads of
    // output and brings grain to the mill.
    private static void Drive(ProductionHarness h, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            h.RunTicks(1);
            if (h.Sim.World.Clock.Tick % 40 != 0)
            {
                continue;
            }

            foreach (EntityId producer in new[] { new EntityId(1), new EntityId(2), new EntityId(3) })
            {
                foreach (GoodAmount held in h.Info(producer).OutputBuffer)
                {
                    h.Context.ProductionBuffers.TryTakeOutput(producer, held.Good, Amount.FromUnits(2));
                }
            }

            if (h.Input(new EntityId(1), "grain") < Amount.FromUnits(2))
            {
                h.Context.ProductionBuffers.AddInput(new EntityId(1), h.Good("grain"), Amount.FromUnits(1));
            }
        }
    }

    // A mill (ID 1) on hills, a fishing hut on a coast (ID 2) with net weaving, and a half-staffed sheep farm (ID 3).
    private static void Arrange(ProductionHarness h)
    {
        for (int y = 0; y < h.Map.Height; y++)
        {
            for (int x = 10; x < h.Map.Width; x++)
            {
                h.Map.SetTile(x, y, h.Tile("sea"));
            }
        }

        for (int y = 4; y < 6; y++)
        {
            for (int x = 2; x < 4; x++)
            {
                h.Map.SetTile(x, y, h.Tile("hills"));
            }
        }

        EntityId mill = h.Add("windmill", 2, 4);
        EntityId hut = h.Add("fishing_hut", 8, 10);
        EntityId farm = h.Add("sheep_farm", 2, 12);
        h.GrantReach(mill);
        h.GrantReach(hut);
        h.GrantReach(farm);
        h.SetFill(farm, 600);

        BuildingTypeId fishingHut = h.Type("fishing_hut");
        h.Sim.Queries.Progression.AddModifier(
            Stat.Productivity,
            new ModifierContribution(EffectSource.ForTech(h.Content.IdOf<TechId>("net_weaving")), 25, true),
            context => context.BuildingType == fishingHut);
    }

    [Fact]
    public void Restore_ASavedState_ContinuesExactlyLikeTheRunThatWasNeverSaved()
    {
        // The uninterrupted run, saved after 500 ticks and carried on to 1,300.
        var original = ProductionHarness.Create();
        Arrange(original);
        Drive(original, 500);
        original.Events();
        byte[] saved = StateSerializer.Write(original.Sim.World);
        Drive(original, 800);
        IReadOnlyList<SimEvent> originalEvents = original.Events();

        // A second world with the same map, buildings and fakes, whose production state comes from the saved bytes.
        // Its buildings were added while nothing listened, so no lifecycle call recreates a record.
        WorldState restored = StateSerializer.Read(saved, ProductionHarness.SliceContent);
        var resumed = ProductionHarness.CreateStopped();
        Arrange(resumed);
        resumed.Sim.World.Clock = restored.Clock;
        resumed.Start(restored.Production);
        Drive(resumed, 800);

        StateSerializer.ComputeHash(resumed.Sim.World).ShouldBe(StateSerializer.ComputeHash(original.Sim.World));
        resumed.Events().ShouldBe(originalEvents);
        foreach (EntityId producer in new[] { new EntityId(1), new EntityId(2), new EntityId(3) })
        {
            resumed.Info(producer).ShouldBe(original.Info(producer));
        }
    }

    [Fact]
    public void Restore_TheProductionState_RoundTripsThroughTheSerialiserUnchanged()
    {
        var h = ProductionHarness.Create();
        Arrange(h);
        Drive(h, 700);

        byte[] bytes = StateSerializer.Write(h.Sim.World);
        WorldState restored = StateSerializer.Read(bytes, ProductionHarness.SliceContent);

        StateSerializer.Write(restored).ShouldBe(bytes);
        restored.Production.Producers.Count.ShouldBe(3);
        restored.Production.Totals.Yesterday.ShouldBe(h.State.Totals.Yesterday);
    }

    [Fact]
    public void Run_TwoIdenticalWorlds_ReachTheSameStateHash()
    {
        var first = ProductionHarness.Create();
        var second = ProductionHarness.Create();
        Arrange(first);
        Arrange(second);

        Drive(first, 1000);
        Drive(second, 1000);

        StateSerializer.ComputeHash(first.Sim.World).ShouldBe(StateSerializer.ComputeHash(second.Sim.World));
        first.System.Queries.ProducedYesterday(first.Good("fish")).ShouldBeGreaterThan(Amount.Zero);
    }
}
