using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Construction;

namespace Evenmere.Sim.Tests.Construction;

/// <summary>Site timing, storehouse reach, the ten material steps, stalls and the construction-speed modifier.</summary>
public sealed class SiteProgressTests
{
    private static void AddSpeedBonus(ConstructionWorld world, int percent) =>
        world.Sim.Queries.Progression.AddModifier(
            Stat.ConstructionSpeed, new ModifierContribution(EffectSource.ForTech(new TechId(0)), percent, true));

    [Fact]
    public void Site_WithReachAndMaterials_CompletesExactlyItsBuildTimeAfterPlacement()
    {
        // A sawpit takes one day: 120 ticks. It is placed in tick 1 and works from tick 2, so tick 121 finishes it.
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        var activated = new List<BuildingInfo>();
        world.Sim.Services.BuildingLifecycle.Subscribe(new RecordingListener(activated, [], []));
        EntityId id = world.Place("sawpit", 5, 5);
        world.GrantReach(id);

        world.Tick(119);
        world.Info(id).State.ShouldBe(BuildingState.Site);
        activated.ShouldBeEmpty();
        world.Tick();

        BuildingInfo done = world.Info(id);
        (done.State, done.Stage).ShouldBe((BuildingState.Active, 0));
        activated.ShouldHaveSingleItem().ShouldBe(done);
        world.System.Queries.TryGetSite(id, out _).ShouldBeFalse();
        world.Stocked("timber").ShouldBe(Amount.FromUnits(7));
        world.Sim.DrainEvents().OfType<ConstructionCompleted>().ShouldHaveSingleItem()
            .ShouldBe(new ConstructionCompleted(121, id, ConstructionWorld.Type("sawpit")));
    }

    [Fact]
    public void Site_PlacementTick_DoesNoWork()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId id = world.Place("sawpit", 5, 5);
        world.GrantReach(id);

        SiteInfo site = world.Site(id);

        (site.Stage, site.Progress.PerMille, site.Stall).ShouldBe((0, 0, StallReason.None));
        world.Stocked("timber").ShouldBe(Amount.FromUnits(10));
    }

    [Fact]
    public void Site_AdvancesInTenSteps_ReportingStageAndProgress()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId id = world.Place("sawpit", 5, 5);
        world.GrantReach(id);

        world.Tick(6);
        SiteInfo half = world.Site(id);
        world.Tick(6);
        SiteInfo first = world.Site(id);
        world.Tick(12 * 8);
        SiteInfo ninth = world.Site(id);

        (half.Stage, half.Progress.PerMille, half.Steps).ShouldBe((0, 50, 10));
        (first.Stage, first.Progress.PerMille).ShouldBe((1, 100));
        (ninth.Stage, ninth.Progress.PerMille).ShouldBe((9, 900));
        world.Info(id).Stage.ShouldBe(9, "the registry shows the same stage the site reports");
    }

    [Fact]
    public void Site_BuildDaysZero_CompletesOnTheNextTick()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "5");
        EntityId id = world.Place("test_instant", 5, 5);
        world.GrantReach(id);
        world.Info(id).State.ShouldBe(BuildingState.Site);

        world.Tick();

        world.Info(id).State.ShouldBe(BuildingState.Active);
        world.Stocked("timber").ShouldBe(Amount.FromUnits(4));
        world.Sim.DrainEvents().OfType<ConstructionCompleted>().ShouldHaveSingleItem().Tick.ShouldBe(2);
    }

    [Fact]
    public void Site_WithoutStorehouseReach_WaitsUntouchedThenResumes()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        EntityId id = world.Place("sawpit", 5, 5);

        world.Tick(10);
        SiteInfo waiting = world.Site(id);
        world.GrantReach(id);
        world.Tick(119);
        BuildingState almost = world.Info(id).State;
        world.Tick();

        (waiting.Stall, waiting.Progress.PerMille, waiting.WaitingFor).ShouldBe((StallReason.NoStorehouseReach, 0, null));
        world.Stocked("timber").ShouldBe(Amount.FromUnits(7));
        almost.ShouldBe(BuildingState.Site);
        world.Info(id).State.ShouldBe(BuildingState.Active);
    }

    [Fact]
    public void Site_PartialMaterials_StallsWithMissingMaterialThenResumesWhenTheyArrive()
    {
        // One timber is three steps of a sawpit (0.3 each); the fourth step waits.
        var world = ConstructionWorld.Create();
        world.Stock("timber", "1");
        EntityId id = world.Place("sawpit", 5, 5);
        world.GrantReach(id);

        world.Tick(36);
        SiteInfo stalled = world.Site(id);
        world.Tick(4);
        SiteInfo stillStalled = world.Site(id);
        world.Stock("timber", "2.1");
        world.Tick(83);
        BuildingState beforeLastTick = world.Info(id).State;
        world.Tick();

        (stalled.Stage, stalled.Progress.PerMille, stalled.Stall, stalled.WaitingFor).ShouldBe(
            (3, 300, StallReason.MissingMaterial, ConstructionWorld.Good("timber")));
        stillStalled.ShouldBe(stalled, "no work is done while a step waits");
        beforeLastTick.ShouldBe(BuildingState.Site);
        world.Info(id).State.ShouldBe(BuildingState.Active);
        world.Stocked("timber").ShouldBe(Amount.Zero, "every step was drawn exactly once");
        world.Sim.DrainEvents().OfType<ConstructionCompleted>().ShouldHaveSingleItem().Tick.ShouldBe(1 + 120 + 4);
    }

    [Fact]
    public void Site_StepNeedingTwoGoods_TakesNeitherWhileOneIsMissing()
    {
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        world.Stock("wood", "0");
        EntityId id = world.Place("test_lodge", 5, 5);
        world.GrantReach(id);

        world.Tick(5);

        SiteInfo site = world.Site(id);
        (site.Stall, site.WaitingFor).ShouldBe((StallReason.MissingMaterial, ConstructionWorld.Good("wood")));
        world.Stocked("timber").ShouldBe(Amount.FromUnits(10), "a step is drawn whole or not at all");
    }

    [Fact]
    public void Site_MaterialSteps_AddUpToTheBillExactly()
    {
        // 3.001 timber is nine steps of 0.300 and a last step of 0.301; 1 wood is ten steps of 0.100.
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        world.Stock("wood", "5");
        EntityId id = world.Place("test_lodge", 5, 5);
        world.GrantReach(id);
        var timberSteps = new List<long>();
        var woodSteps = new List<long>();
        Amount timber = world.Stocked("timber");
        Amount wood = world.Stocked("wood");

        for (int i = 0; i < 125; i++)
        {
            world.Tick();
            Amount nowTimber = world.Stocked("timber");
            Amount nowWood = world.Stocked("wood");
            if (nowTimber < timber)
            {
                timberSteps.Add((timber - nowTimber).Milli);
            }

            if (nowWood < wood)
            {
                woodSteps.Add((wood - nowWood).Milli);
            }

            timber = nowTimber;
            wood = nowWood;
        }

        timberSteps.ShouldBe([300, 300, 300, 300, 300, 300, 300, 300, 300, 301]);
        woodSteps.ShouldBe([100, 100, 100, 100, 100, 100, 100, 100, 100, 100]);
        world.Info(id).State.ShouldBe(BuildingState.Active);
        world.Stocked("timber").ShouldBe(Amount.Parse("6.999"));
        world.Stocked("wood").ShouldBe(Amount.Parse("4"));
    }

    [Fact]
    public void Site_ConstructionSpeedBonus_ShortensTheBuildTime()
    {
        // +25% makes 1250 work per tick, so one day (120 000) takes 96 ticks.
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        AddSpeedBonus(world, 25);
        EntityId id = world.Place("sawpit", 5, 5);
        world.GrantReach(id);

        world.Tick(95);
        BuildingState before = world.Info(id).State;
        world.Tick();

        before.ShouldBe(BuildingState.Site);
        world.Info(id).State.ShouldBe(BuildingState.Active);
        world.Stocked("timber").ShouldBe(Amount.FromUnits(7), "the bill does not change with speed");
    }

    [Fact]
    public void Site_ConstructionSpeedPenalty_SlowsButNeverStops()
    {
        // −100% would be zero speed; the floor is a tenth, so a day takes 1200 ticks.
        var world = ConstructionWorld.Create();
        world.Stock("timber", "10");
        AddSpeedBonus(world, -100);
        EntityId id = world.Place("sawpit", 5, 5);
        world.GrantReach(id);

        world.Tick(1199);
        BuildingState before = world.Info(id).State;
        world.Tick();

        before.ShouldBe(BuildingState.Site);
        world.Info(id).State.ShouldBe(BuildingState.Active);
    }

    [Fact]
    public void SiteMath_StepsAndTotals_AreExactForAwkwardAmounts()
    {
        var bill = Amount.FromMilli(3001);
        long sum = 0;
        for (int step = 1; step <= SiteMath.Steps; step++)
        {
            sum += SiteMath.StepAmount(bill, step).Milli;
            SiteMath.DrawnAfter(bill, step).Milli.ShouldBe(sum);
        }

        sum.ShouldBe(3001);
        SiteMath.DrawnAfter(bill, 0).ShouldBe(Amount.Zero);
        SiteMath.TotalTicks(120_000, 1250).ShouldBe(96);
        SiteMath.TotalTicks(0, 1000).ShouldBe(1, "even an instant building waits for its first tick");
        SiteMath.StepBoundary(1000, 10).ShouldBe(1000);
        SiteMath.SpeedFor(0).ShouldBe(1000);
        SiteMath.SpeedFor(-500).ShouldBe(100);
    }

    [Fact]
    public void Run_RecordWhoseBuildingIsGone_IsDropped()
    {
        var world = ConstructionWorld.Create();
        world.State.Sites.Add(new EntityId(77), new SiteRecord { Kind = SiteKind.Build, Target = ConstructionWorld.Type("sawpit") });

        world.Tick();

        world.State.Sites.Count.ShouldBe(0);
    }

    [Fact]
    public void TryGetSite_FinishedOrUnknownBuilding_IsFalse()
    {
        var world = ConstructionWorld.Create();
        EntityId finished = world.Build("sawpit", 5, 5);

        world.System.Queries.TryGetSite(finished, out _).ShouldBeFalse();
        world.System.Queries.TryGetSite(new EntityId(500), out _).ShouldBeFalse();
    }
}
