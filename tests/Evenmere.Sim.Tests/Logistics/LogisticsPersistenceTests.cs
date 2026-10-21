using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;

namespace Evenmere.Sim.Tests.Logistics;

public sealed class LogisticsPersistenceTests
{
    [Fact]
    public void Run_TwoIdenticalWorlds_ReachTheSameStateHash()
    {
        var first = LogisticsHarness.Create();
        var second = LogisticsHarness.Create();
        Arrange(first);
        Arrange(second);

        Drive(first, 1000);
        Drive(second, 1000);

        StateSerializer.ComputeHash(first.Sim.World).ShouldBe(StateSerializer.ComputeHash(second.Sim.World));
        first.Jobs().ShouldBe(second.Jobs());
        first.Events().ShouldBe(second.Events());
    }

    [Fact]
    public void Restore_ASavedState_ContinuesExactlyLikeTheRunThatWasNeverSaved()
    {
        var original = LogisticsHarness.Create();
        Arrange(original);
        Drive(original, 100);
        original.Events();
        byte[] saved = StateSerializer.Write(original.Sim.World);

        // The fake buffers live outside the world state, so snapshot them to replay the same request.
        Amount savedPool = original.Sim.Services.Storage.Get(original.Good("wood"));
        Amount savedOutput = original.Sim.Services.ProductionBuffers.OutputOf(original.Hut, original.Good("wood"));
        Amount savedInput = original.Sim.Services.ProductionBuffers.InputOf(original.Bakery, original.Good("wood"));
        Drive(original, 200);
        IReadOnlyList<SimEvent> originalEvents = original.Events();

        var resumed = LogisticsHarness.CreateUnstarted();
        Arrange(resumed);
        WorldState restored = StateSerializer.Read(saved, LogisticsHarness.SliceContent);
        resumed.Sim.World.Clock = restored.Clock;
        resumed.Sim.World.EntityIds = restored.EntityIds;
        resumed.Sim.Services.Storage.Set(resumed.Good("wood"), savedPool);
        resumed.SetOutput(resumed.Hut, "wood", savedOutput);
        resumed.Sim.Services.ProductionBuffers.AddInput(resumed.Bakery, resumed.Good("wood"), savedInput);
        resumed.Start(restored.Logistics);
        Drive(resumed, 200);

        StateSerializer.ComputeHash(resumed.Sim.World).ShouldBe(StateSerializer.ComputeHash(original.Sim.World));
        resumed.Events().ShouldBe(originalEvents);
        resumed.Jobs().ShouldBe(original.Jobs());
    }

    [Fact]
    public void Restore_TheLogisticsState_RoundTripsThroughTheSerialiserUnchanged()
    {
        var h = LogisticsHarness.Create();
        Arrange(h);
        Drive(h, 100);

        byte[] bytes = StateSerializer.Write(h.Sim.World);
        WorldState restored = StateSerializer.Read(bytes, LogisticsHarness.SliceContent);

        StateSerializer.Write(restored).ShouldBe(bytes);
        restored.Logistics.Jobs.Count.ShouldBe(h.State.Jobs.Count);
    }

    private static void Arrange(LogisticsHarness h)
    {
        EntityId storehouse = h.Add("storehouse", 0, 0);
        h.Hut = h.Add("woodcutters_hut", 10, 10);
        h.Bakery = h.Add("sawpit", 16, 16);
        h.SetReach(storehouse, h.Hut, 10000);
        h.SetReach(storehouse, h.Bakery, 6000);
        h.SetOutput(h.Hut, "wood", h.CartLoad);
        h.Sim.Services.Storage.Set(h.Good("wood"), Amount.FromUnits(4));
        h.OpenPickup(h.Hut, "wood");
        h.OpenDelivery(h.Bakery, "wood");
    }

    private static void Drive(LogisticsHarness h, int ticks) => h.RunTicks(ticks);
}
