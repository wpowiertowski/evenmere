using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Workforce;
using Evenmere.Sim.Systems.Workforce.Commands;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Workforce;

/// <summary>ADR-0016: idle workers step down to lower-tier jobs, nearest higher tier first, with same-tier workers always used before substitutes.</summary>
public sealed class WorkforceSteppingDownTests
{
    [Fact]
    public void StepsDown_FillsUnfilledJobsFromIdleHigherTier()
    {
        var h = WorkforceHarness.Create();
        EntityId workshop = h.Add("test_workshop", 0, 0);

        // Three settlers cover three of five jobs; ten idle villagers cover the other two.
        h.SetSupply("settlers", 3);
        h.SetSupply("villagers", 10);
        h.Allocate();

        h.Allocated(workshop).ShouldBe(5);
        h.Queries.AllocatedWorkers(workshop).ShouldBe(5);
        h.Queries.SubstitutedWorkers(workshop).ShouldBe(2);
        h.Queries.SubstituteSourceTier(workshop).ShouldBe(h.Tier("villagers"));
        h.Queries.WorkingBelowTier(h.Tier("villagers")).ShouldBe(2);
        h.Queries.FilledFromAbove(h.Tier("settlers")).ShouldBe(2);
        h.Queries.FillOf(workshop).PerMille.ShouldBe(1000);
    }

    [Fact]
    public void StepsDown_SameTierWorkersAreUsedFirst()
    {
        var h = WorkforceHarness.Create();
        EntityId workshop = h.Add("test_workshop", 0, 0);

        h.SetSupply("settlers", 5);
        h.SetSupply("villagers", 10);
        h.Allocate();

        h.Allocated(workshop).ShouldBe(5);
        h.Queries.SubstitutedWorkers(workshop).ShouldBe(0);
        h.Queries.WorkingBelowTier(h.Tier("villagers")).ShouldBe(0);
        h.Queries.FilledFromAbove(h.Tier("settlers")).ShouldBe(0);
    }

    [Fact]
    public void StepsDown_NearestHigherRankGoesFirst()
    {
        var h = WorkforceHarness.Create(ThreeTierContent.Db);
        EntityId workshop = h.Add("test_workshop", 0, 0);

        // No settlers; two villagers and ten of the top tier. Villagers are nearer in rank, so they go first
        // even though their tier ID sorts after the top tier's.
        h.SetSupply("settlers", 0);
        h.SetSupply("villagers", 2);
        h.SetSupply("test_high", 10);
        h.Allocate();

        h.Allocated(workshop).ShouldBe(5);
        h.Queries.SubstitutedWorkers(workshop).ShouldBe(5);
        h.Queries.SubstituteSourceTier(workshop).ShouldBe(h.Tier("villagers"));
        h.Queries.WorkingBelowTier(h.Tier("villagers")).ShouldBe(2);
        h.Queries.WorkingBelowTier(h.Tier("test_high")).ShouldBe(3);
        h.Queries.FilledFromAbove(h.Tier("settlers")).ShouldBe(5);
    }

    [Fact]
    public void StepsDown_HighPriorityIsStillRespected()
    {
        var h = WorkforceHarness.Create();
        EntityId plain = h.Add("test_workshop", 0, 0);
        EntityId priority = h.Add("test_workshop", 4, 0);
        h.Send(new SetWorkforcePriority(priority, true));

        // Five settlers go to the prioritised workshop first; three idle villagers cover the rest.
        h.SetSupply("settlers", 5);
        h.SetSupply("villagers", 3);
        h.Allocate();

        h.Allocated(priority).ShouldBe(5);
        h.Allocated(plain).ShouldBe(3);
        h.Queries.SubstitutedWorkers(priority).ShouldBe(0);
        h.Queries.SubstitutedWorkers(plain).ShouldBe(3);
        h.Queries.WorkingBelowTier(h.Tier("villagers")).ShouldBe(3);
    }

    [Fact]
    public void StepsDown_CountsReconcile()
    {
        var h = WorkforceHarness.Create();
        EntityId first = h.Add("test_workshop", 0, 0);
        EntityId second = h.Add("test_workshop", 4, 0);
        EntityId third = h.Add("test_workshop", 8, 0);

        // Thirteen workers against fifteen jobs: every worker is placed.
        h.SetSupply("settlers", 3);
        h.SetSupply("villagers", 10);
        h.Allocate();

        (h.Allocated(first) + h.Allocated(second) + h.Allocated(third)).ShouldBe(13);
        h.Queries.WorkingBelowTier(h.Tier("villagers")).ShouldBe(10);
        h.Queries.FilledFromAbove(h.Tier("settlers")).ShouldBe(10);

        // And the other way: twenty settlers against five jobs place only five.
        var full = WorkforceHarness.Create();
        EntityId workshop = full.Add("test_workshop", 0, 0);
        full.SetSupply("settlers", 20);
        full.Allocate();

        full.Allocated(workshop).ShouldBe(5);
    }

    [Fact]
    public void StepsDown_PlannedDemand_CountsOnlySites()
    {
        var h = WorkforceHarness.Create();
        h.Add("test_workshop", 0, 0);
        EntityId site = h.Add("test_workshop", 4, 0, BuildingState.Site);
        EntityId paused = h.Add("test_workshop", 8, 0, BuildingState.Paused);

        // The active workshop asks for five; the site will ask for five once finished; the paused one asks for none.
        h.Queries.Demand(h.Tier("settlers")).ShouldBe(5);
        h.Queries.PlannedDemand(h.Tier("settlers")).ShouldBe(5);
        site.ShouldNotBe(paused);
    }

    [Fact]
    public void StepsDown_PlaytestShortage_IsCoveredByIdleVillagers()
    {
        // The developer's playtest shape, scaled to workshops of five: fifty settler jobs, forty-four settlers,
        // and plenty of idle villagers. Every job fills; nothing stays unfilled after stepping down.
        var h = WorkforceHarness.Create();
        for (int i = 0; i < 10; i++)
        {
            h.Add("test_workshop", (i % 5) * 4, (i / 5) * 4);
        }

        h.SetSupply("settlers", 44);
        h.SetSupply("villagers", 60);
        h.Allocate();

        int total = 0;
        foreach (BuildingInfo building in h.Sim.Queries.Map.ActiveBuildings)
        {
            total += h.Allocated(building.Id);
        }

        total.ShouldBe(50);
        h.Queries.WorkingBelowTier(h.Tier("villagers")).ShouldBe(6);
        h.Queries.FilledFromAbove(h.Tier("settlers")).ShouldBe(6);
        (h.Queries.Demand(h.Tier("settlers"))
            > h.Queries.Supply(h.Tier("settlers")) + h.Queries.FilledFromAbove(h.Tier("settlers"))).ShouldBeFalse();
    }

    [Fact]
    public void StepsDown_SameScriptedDays_AreDeterministic()
    {
        static ulong RunScript()
        {
            var h = WorkforceHarness.Create();
            EntityId first = h.Add("test_workshop", 0, 0);
            h.Add("test_workshop", 4, 0);
            h.Send(new SetWorkforcePriority(first, true));
            h.SetSupply("settlers", 3);
            h.SetSupply("villagers", 10);
            h.Allocate();
            return h.Hash();
        }

        RunScript().ShouldBe(RunScript());
    }

    [Fact]
    public void StepsDown_SaveRoundTrip_PreservesTheSubstitution()
    {
        var h = WorkforceHarness.Create();
        EntityId workshop = h.Add("test_workshop", 0, 0);
        h.SetSupply("settlers", 3);
        h.SetSupply("villagers", 10);
        h.Allocate();

        byte[] bytes = StateSerializer.Write(h.Sim.World);
        WorldState restored = StateSerializer.Read(bytes, h.Content);

        restored.Workforce.WorkingBelow.ShouldBe(h.State.WorkingBelow);
        restored.Workforce.FilledFromAbove.ShouldBe(h.State.FilledFromAbove);
        restored.Workforce.Workplaces.TryGet(workshop, out WorkplaceRecord? record).ShouldBeTrue();
        record!.Allocated.ShouldBe(5);
        record.Substituted.ShouldBe(2);
        record.SubstituteTier.ShouldBe(h.Tier("villagers").Value);
    }
}

/// <summary>
/// The slice content with a third tier on top, so the nearest-higher-tier rule has two higher tiers to choose from.
/// The tier is worker-only: nothing advances to it and no building asks for it.
/// </summary>
internal static class ThreeTierContent
{
    private const string Tier = """
        {
          "id": "test_high", "order": 3, "era": 1, "capacity": 10
        }
        """;

    private const string Workshop = """
        {
          "id": "test_workshop", "category": "production", "era": 1, "footprint": [2, 2],
          "cost": { "timber": 1 }, "buildDays": 1,
          "workforce": { "tier": "settlers", "count": 5 },
          "start": true,
          "tags": ["processing"]
        }
        """;

    private static readonly Lazy<ContentDb> _db = new(Load);

    /// <summary>Gets the slice content with the extra workshop and tier.</summary>
    public static ContentDb Db => _db.Value;

    private static ContentDb Load()
    {
        DirectoryInfo copy = Directory.CreateTempSubdirectory("evenmere-workforce-threetier-");
        try
        {
            CopyDirectory(Path.Combine(WorldBuilder.RepoRoot, "content"), copy.FullName);
            File.WriteAllText(Path.Combine(copy.FullName, "buildings", "test_workshop.json"), Workshop);
            File.WriteAllText(Path.Combine(copy.FullName, "tiers", "test_high.json"), Tier);
            return ContentLoader.Load(copy.FullName).GetDbOrThrow();
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
