using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>One raise-and-clear test per daily rule, over the fake queries.</summary>
public sealed class AdvisorsRuleTests
{
    [Fact]
    public void NoRoadAccess_BuildingWithoutRoad_RaisesAndClears()
    {
        var h = AdvisorsHarness.Create();
        EntityId hut = h.Add("woodcutters_hut", 0, 0);
        h.AddReachedStorehouse(hut, 8, 8);

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.NoRoadAccess);
        active[0].Subject.ShouldBe(SubjectLink.ForBuilding(hut));
        active[0].FirstSeenTick.ShouldBe(0);
        active[0].LastSeenTick.ShouldBe(0);
        h.Events().ShouldBe([new AdviceRaised(0, active[0])]);

        // A road beside it clears the problem, and the showing note emits AdviceCleared.
        h.Sim.Queries.Roads.GrantRoadAccess(hut);
        h.AdvanceDays(1);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(120, active[0].Id, AdviceKind.NoRoadAccess)]);
    }

    [Fact]
    public void NoRoadAccess_RoadsAndRoadlessTypes_AreExempt()
    {
        var h = AdvisorsHarness.Create();

        // Dirt roads need no road themselves, and the founders' hall's placement needs none either.
        h.Add("dirt_road", 0, 0);
        h.Add("founders_hall", 4, 4, BuildingState.Site);

        h.Run();

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void NoRoadAccess_SiteWithoutRoad_Raises()
    {
        var h = AdvisorsHarness.Create();
        EntityId site = h.Add("woodcutters_hut", 0, 0, BuildingState.Site);
        h.AddReachedStorehouse(site, 8, 8);

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.NoRoadAccess);
        active[0].Subject.ShouldBe(SubjectLink.ForBuilding(site));
    }

    [Fact]
    public void OutsideStorehouseReach_BuildingWithRoadButNoReach_RaisesAndClears()
    {
        var h = AdvisorsHarness.Create();
        EntityId hut = h.Add("woodcutters_hut", 0, 0);
        h.Sim.Queries.Roads.GrantRoadAccess(hut);

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.OutsideStorehouseReach);
        active[0].Subject.ShouldBe(SubjectLink.ForBuilding(hut));
        h.Events().ShouldBe([new AdviceRaised(0, active[0])]);

        // A storehouse reaching it clears the problem.
        h.AddReachedStorehouse(hut, 8, 8);
        h.AdvanceDays(1);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(120, active[0].Id, AdviceKind.OutsideStorehouseReach)]);
    }

    [Fact]
    public void OutsideStorehouseReach_Storehouses_AreExempt()
    {
        var h = AdvisorsHarness.Create();
        EntityId storehouse = h.Add("storehouse", 0, 0);
        h.Sim.Queries.Roads.GrantRoadAccess(storehouse);

        h.Run();

        // Road access is granted and the storehouse is in its own reach, so no note may appear.
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void GoodShortage_ShortTwoDays_RaisesAndClears()
    {
        var h = AdvisorsHarness.Create();
        GoodId fish = h.Good("fish");
        h.Sim.Queries.Needs.SetDemandYesterday(fish, Amount.FromUnits(10));
        h.Sim.Queries.Needs.SetFulfilment(fish, Ratio.FromPerMille(799));

        // One bad day is not enough.
        h.Run();
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();

        // The second consecutive bad day raises the note.
        h.AdvanceDays(1);
        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.GoodShortage);
        active[0].Subject.ShouldBe(SubjectLink.ForGood(fish));
        active[0].FirstSeenTick.ShouldBe(120);
        h.Events().ShouldBe([new AdviceRaised(120, active[0])]);

        // A recovered fulfilment clears it on the next evaluation.
        h.Sim.Queries.Needs.SetFulfilment(fish, Ratio.One);
        h.AdvanceDays(1);
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(240, active[0].Id, AdviceKind.GoodShortage)]);
    }

    [Fact]
    public void GoodShortage_NoDemand_NeverRaises()
    {
        var h = AdvisorsHarness.Create();
        GoodId fish = h.Good("fish");
        h.Sim.Queries.Needs.SetFulfilment(fish, Ratio.Zero);

        h.Run();
        h.AdvanceDays(1);

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void HouseCannotAdvance_BlockedHouse_RaisesAndClears()
    {
        var h = AdvisorsHarness.Create();
        EntityId house = h.Add("settler_house", 0, 0);
        h.Sim.Queries.Roads.GrantRoadAccess(house);
        h.AddReachedStorehouse(house, 8, 8);
        TierId settlers = h.Tier("settlers");
        h.Sim.Queries.Population.SetHouse(new HouseInfo(house, settlers, 6, 6, 6, 3, 0, false, false, AdvanceBlocker.MissingMaterials, 1000));

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.HouseCannotAdvance);
        active[0].Subject.ShouldBe(SubjectLink.ForBuilding(house));
        h.Events().ShouldBe([new AdviceRaised(0, active[0])]);

        // Once nothing blocks it, the note clears.
        h.Sim.Queries.Population.SetHouse(new HouseInfo(house, settlers, 6, 6, 6, 3, 0, false, false, AdvanceBlocker.None, 1000));
        h.AdvanceDays(1);
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(120, active[0].Id, AdviceKind.HouseCannotAdvance)]);
    }

    [Fact]
    public void HouseCannotAdvance_LockedTier_Raises()
    {
        var h = AdvisorsHarness.Create();
        EntityId house = h.Add("settler_house", 0, 0);
        h.Sim.Queries.Roads.GrantRoadAccess(house);
        h.AddReachedStorehouse(house, 8, 8);
        h.Sim.Queries.Population.SetHouse(new HouseInfo(house, h.Tier("settlers"), 6, 6, 6, 3, 0, false, false, AdvanceBlocker.NextTierLocked, 1000));

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.HouseCannotAdvance);
    }

    [Fact]
    public void NoWorkforce_ShortTwoDays_RaisesAndClears()
    {
        var h = AdvisorsHarness.Create();
        TierId settlers = h.Tier("settlers");
        h.Sim.Queries.Workforce.SetSupply(settlers, 2);
        h.Sim.Queries.Workforce.SetDemand(settlers, 5);

        // One bad day is not enough.
        h.Run();
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();

        // The second consecutive bad day raises the note.
        h.AdvanceDays(1);
        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.NoWorkforce);
        active[0].Subject.ShouldBe(SubjectLink.ForTier(settlers));
        h.Events().ShouldBe([new AdviceRaised(120, active[0])]);

        // Enough workers clear it on the next evaluation.
        h.Sim.Queries.Workforce.SetSupply(settlers, 5);
        h.AdvanceDays(1);
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(240, active[0].Id, AdviceKind.NoWorkforce)]);
    }

    [Fact]
    public void ResearchIdle_PointsWithNoTarget_RaisesAndClears()
    {
        var h = AdvisorsHarness.Create();
        h.Sim.Queries.Research.PointsPerDay = Amount.FromUnits(4);
        h.Sim.Queries.Research.SetTech(h.Content.IdOf<TechId>("net_weaving"), Amount.FromUnits(50), available: true);

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.ResearchIdle);
        active[0].Subject.ShouldBe(SubjectLink.None);
        h.Events().ShouldBe([new AdviceRaised(0, active[0])]);

        // Choosing a target clears it.
        h.Sim.Queries.Research.Target = h.Content.IdOf<TechId>("net_weaving");
        h.AdvanceDays(1);
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(120, active[0].Id, AdviceKind.ResearchIdle)]);
    }

    [Fact]
    public void CivicIdle_PointsWithNoTarget_RaisesAndClears()
    {
        var h = AdvisorsHarness.Create();
        h.Sim.Queries.Culture.PointsPerDay = Amount.FromUnits(2);
        h.Sim.Queries.Culture.SetCivic(h.Content.IdOf<CivicId>("village_moot"), Amount.FromUnits(140), available: true);

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.CivicIdle);
        active[0].Subject.ShouldBe(SubjectLink.None);
        h.Events().ShouldBe([new AdviceRaised(0, active[0])]);

        // Adopting a civic target clears it.
        h.Sim.Queries.Culture.Target = h.Content.IdOf<CivicId>("village_moot");
        h.AdvanceDays(1);
        h.Active().ShouldBeEmpty();
        h.Events().ShouldBe([new AdviceCleared(120, active[0].Id, AdviceKind.CivicIdle)]);
    }

    [Fact]
    public void NoRoadAccess_RoadlessBuilding_RaisesOnlyNoRoadAccess()
    {
        var h = AdvisorsHarness.Create();
        EntityId hut = h.Add("woodcutters_hut", 0, 0);

        // No road and no reach entry: without a road the building cannot be in reach either, so only the road note appears.
        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.NoRoadAccess);
        active[0].Subject.ShouldBe(SubjectLink.ForBuilding(hut));
        h.State.Records.Length.ShouldBe(1);
    }

    [Fact]
    public void OutsideStorehouseReach_RoadAddedOutsideReach_SwapsNotes()
    {
        var h = AdvisorsHarness.Create();
        EntityId hut = h.Add("woodcutters_hut", 0, 0);

        h.Run();
        AdviceView road = h.Active().ShouldHaveSingleItem();
        road.Kind.ShouldBe(AdviceKind.NoRoadAccess);
        h.Events();

        // A road beside it, still outside every reach: the road note clears and the reach note is raised instead.
        h.Sim.Queries.Roads.GrantRoadAccess(hut);
        h.AdvanceDays(1);

        AdviceView[] active = h.Active();
        AdviceView reach = active.ShouldHaveSingleItem();
        reach.Kind.ShouldBe(AdviceKind.OutsideStorehouseReach);
        reach.Subject.ShouldBe(SubjectLink.ForBuilding(hut));
        reach.Id.ShouldNotBe(road.Id);
        h.Events().ShouldBe(
        [
            new AdviceCleared(120, road.Id, AdviceKind.NoRoadAccess),
            new AdviceRaised(120, reach),
        ]);
    }

    [Fact]
    public void HouseCannotAdvance_BlockedHousesOfOneTier_ProduceOneRecord()
    {
        var h = AdvisorsHarness.Create();
        TierId settlers = h.Tier("settlers");
        var houses = new EntityId[8];
        for (int i = 0; i < houses.Length; i++)
        {
            houses[i] = AddBlockedHouse(h, i * 3, settlers, AdvanceBlocker.MissingMaterials);
        }

        h.Run();

        AdviceView[] active = h.Active();
        AdviceView note = active.ShouldHaveSingleItem();
        note.Kind.ShouldBe(AdviceKind.HouseCannotAdvance);
        note.Subject.ShouldBe(SubjectLink.ForBuilding(houses[0]));
        h.State.Records.Length.ShouldBe(1);
    }

    [Fact]
    public void HouseCannotAdvance_NoteStaysWithSubject_WhenLowerIdHouseBlocksLater()
    {
        var h = AdvisorsHarness.Create();
        TierId settlers = h.Tier("settlers");
        EntityId first = AddBlockedHouse(h, 0, settlers, AdvanceBlocker.None);
        EntityId second = AddBlockedHouse(h, 4, settlers, AdvanceBlocker.MissingMaterials);

        h.Run();
        AdviceView note = h.Active().ShouldHaveSingleItem();
        note.Subject.ShouldBe(SubjectLink.ForBuilding(second));
        h.Events();

        // The lower-ID house blocks too, but the note stays with its subject: no new record, no events.
        h.Sim.Queries.Population.SetHouse(Blocked(first, settlers, AdvanceBlocker.NextTierLocked));
        h.AdvanceDays(1);

        AdviceView[] active = h.Active();
        AdviceView kept = active.ShouldHaveSingleItem();
        kept.Id.ShouldBe(note.Id);
        kept.FirstSeenTick.ShouldBe(note.FirstSeenTick);
        kept.LastSeenTick.ShouldBe(120);
        kept.Subject.ShouldBe(SubjectLink.ForBuilding(second));
        h.State.Records.Length.ShouldBe(1);
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void HouseCannotAdvance_SubjectDemolished_NamesLowestRemaining()
    {
        var h = AdvisorsHarness.Create();
        TierId settlers = h.Tier("settlers");
        EntityId first = AddBlockedHouse(h, 0, settlers, AdvanceBlocker.MissingMaterials);
        EntityId second = AddBlockedHouse(h, 4, settlers, AdvanceBlocker.MissingMaterials);
        AddBlockedHouse(h, 8, settlers, AdvanceBlocker.MissingMaterials);

        h.Run();
        AdviceView note = h.Active().ShouldHaveSingleItem();
        note.Subject.ShouldBe(SubjectLink.ForBuilding(first));
        h.Events();

        // Demolishing the subject lapses its note and names the lowest-ID house that is still blocked.
        h.Sim.Queries.Map.RemoveBuilding(first);
        h.AdvanceDays(1);

        AdviceView[] active = h.Active();
        AdviceView renamed = active.ShouldHaveSingleItem();
        renamed.Subject.ShouldBe(SubjectLink.ForBuilding(second));
        renamed.Id.ShouldNotBe(note.Id);
        h.Events().ShouldBe(
        [
            new AdviceCleared(120, note.Id, AdviceKind.HouseCannotAdvance),
            new AdviceRaised(120, renamed),
        ]);
    }

    [Fact]
    public void HouseCannotAdvance_TwoTiersBlocked_ProduceTwoRecords()
    {
        var h = AdvisorsHarness.Create();
        EntityId settlerHouse = AddBlockedHouse(h, 0, h.Tier("settlers"), AdvanceBlocker.MissingMaterials);
        AddBlockedHouse(h, 4, h.Tier("settlers"), AdvanceBlocker.MissingMaterials);
        EntityId villagerHouse = AddBlockedHouse(h, 8, h.Tier("villagers"), AdvanceBlocker.NextTierLocked);
        AddBlockedHouse(h, 12, h.Tier("villagers"), AdvanceBlocker.NextTierLocked);

        h.Run();

        AdviceView[] active = h.Active();
        active.Length.ShouldBe(2);
        active.ShouldAllBe(static a => a.Kind == AdviceKind.HouseCannotAdvance);
        active.Select(static a => a.Subject).ShouldBe(
            [SubjectLink.ForBuilding(settlerHouse), SubjectLink.ForBuilding(villagerHouse)],
            "each tier names its lowest-ID blocked house");
    }

    private static EntityId AddBlockedHouse(AdvisorsHarness h, int x, TierId tier, AdvanceBlocker blocker)
    {
        EntityId house = h.Add("settler_house", x, 0);
        h.Sim.Queries.Roads.GrantRoadAccess(house);
        h.AddReachedStorehouse(house, x, 8);
        h.Sim.Queries.Population.SetHouse(Blocked(house, tier, blocker));
        return house;
    }

    private static HouseInfo Blocked(EntityId house, TierId tier, AdvanceBlocker blocker) =>
        new(house, tier, 6, 6, 6, 3, 0, false, false, blocker, 1000);
}
