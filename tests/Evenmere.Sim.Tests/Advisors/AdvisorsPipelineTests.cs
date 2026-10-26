using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Advisors.Commands;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>Determinism, save round trips and locale coverage, plus the tests that run the real pipeline.</summary>
public sealed class AdvisorsPipelineTests
{
    [Fact]
    public void ResearchIdle_OnTheRealPipeline_RaisesThroughResearchAndShowsCalmText()
    {
        Simulation sim = new WorldBuilder().Build();
        sim.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(5));

        // One full day: research banks the knowledge (nothing is targeted), then advisors notice.
        for (int i = 0; i < GameTime.TicksPerDay; i++)
        {
            sim.Tick();
        }

        AdviceView raised = sim.Query.Advisors.Active().ToArray().ShouldHaveSingleItem();
        raised.Kind.ShouldBe(AdviceKind.ResearchIdle);
        raised.Subject.ShouldBe(SubjectLink.None);
        raised.FirstSeenTick.ShouldBe(GameTime.TicksPerDay);
        sim.DrainEvents().ShouldBe([new AdviceRaised(GameTime.TicksPerDay, raised)]);
    }

    [Fact]
    public void DismissAdvice_OnTheRealPipeline_UnknownIdIsRejected()
    {
        Simulation sim = new WorldBuilder().Build();

        sim.Enqueue(new DismissAdvice(1));
        sim.Tick();

        CommandRejected rejected = sim.DrainEvents().OfType<CommandRejected>().ShouldHaveSingleItem();
        rejected.Code.ShouldBe(RejectReason.UnknownTarget);
        rejected.Command.ShouldBe(new DismissAdvice(1));
    }

    [Fact]
    public void Run_SameOperations_Twice_GivesTheSameHashAndAdvice()
    {
        var h1 = AdvisorsHarness.Create();
        var h2 = AdvisorsHarness.Create();
        Populate(h1);
        Populate(h2);

        h1.Hash().ShouldBe(h2.Hash());
        h1.Active().ShouldBe(h2.Active());
    }

    [Fact]
    public void State_SaveRoundTrip_ContinuesIdentically()
    {
        var h = AdvisorsHarness.Create();
        Populate(h);
        ulong before = h.Hash();
        AdviceView[] active = h.Active();

        // A save round trip keeps the records, the counter, the streaks and the suppressions.
        var reloaded = AdvisorsHarness.Reload(h);
        reloaded.Hash().ShouldBe(before);

        // The fakes stand in for the other systems' state, which a real save would also restore: rebuilding the same
        // fake world (the allocator round-tripped too, so the IDs match) lets the loaded slice continue identically.
        (EntityId[] huts, EntityId[] halls) = BuildHuts(reloaded);
        ArrangeReach(reloaded, huts, halls);
        reloaded.Active().ShouldBe(active);
        reloaded.AdvanceDays(3);
        h.AdvanceDays(3);
        reloaded.Hash().ShouldBe(h.Hash());
        reloaded.Active().ShouldBe(h.Active());
    }

    [Fact]
    public void Locale_EveryKind_HasCalmTitleAndBody()
    {
        string contentRoot = Path.Combine(WorldBuilder.RepoRoot, "content");
        Content.Locale.LocaleTable table = Content.Locale.LocaleTable.Load(contentRoot, "en").Table;

        foreach (AdviceKind kind in Enum.GetValues<AdviceKind>())
        {
            string prefix = $"advisor.{ToSnake(kind)}";
            table.TryGet($"{prefix}.title", out string title).ShouldBeTrue($"missing {prefix}.title");
            title.ShouldNotBeNullOrWhiteSpace();
            table.TryGet($"{prefix}.body", out string body).ShouldBeTrue($"missing {prefix}.body");
            body.ShouldNotBeNullOrWhiteSpace();
        }
    }

    private static void Populate(AdvisorsHarness h)
    {
        (EntityId[] huts, EntityId[] halls) = BuildHuts(h);
        ArrangeReach(h, huts, halls);
        h.Run();
        h.AdvanceDays(2);
        h.Send(new DismissAdvice(h.Active()[0].Id));
        h.AdvanceDays(1);
    }

    private static (EntityId[] Huts, EntityId[] Halls) BuildHuts(AdvisorsHarness h)
    {
        var huts = new EntityId[6];
        var halls = new EntityId[6];
        for (int i = 0; i < 6; i++)
        {
            huts[i] = h.Add("woodcutters_hut", i * 3, 0);
            halls[i] = h.Add("storehouse", i * 3, 8);
            h.Sim.Queries.Roads.GrantRoadAccess(halls[i]);
        }

        return (huts, halls);
    }

    private static void ArrangeReach(AdvisorsHarness h, EntityId[] huts, EntityId[] halls)
    {
        for (int i = 0; i < huts.Length; i++)
        {
            h.Sim.Queries.Roads.SetReach(halls[i], huts[i], 1000);
        }

        h.Sim.Queries.Research.PointsPerDay = Amount.FromUnits(3);
    }

    private static string ToSnake(AdviceKind kind) => kind switch
    {
        AdviceKind.NoRoadAccess => "no_road_access",
        AdviceKind.OutsideStorehouseReach => "outside_storehouse_reach",
        AdviceKind.GoodShortage => "good_shortage",
        AdviceKind.HouseCannotAdvance => "house_cannot_advance",
        AdviceKind.NoWorkforce => "no_workforce",
        AdviceKind.ResearchIdle => "research_idle",
        AdviceKind.CivicIdle => "civic_idle",
        AdviceKind.NoLowerTierFallback => "no_lower_tier_fallback",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
