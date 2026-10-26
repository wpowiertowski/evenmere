using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Advisors;

/// <summary>
/// The no-lower-tier-fallback rule (M2-30): when every active maker of a tier's basic need is staffed by the tier
/// itself, the advisors warn that the town has upgraded away its fallback. Needs with no active maker at all stay
/// silent, and a restored lower-tier maker clears the note.
/// </summary>
public sealed class AdvisorsFallbackTests
{
    [Fact]
    public void FallbackLost_EveryActiveProducerIsSameTierStaffed_RaisesForTheTier()
    {
        var h = FallbackHarness.Create();
        EntityId hut = h.Add("fishing_hut", 0, 0);
        h.GrantReach(hut);

        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.NoLowerTierFallback);
        active[0].Subject.ShouldBe(SubjectLink.ForTier(h.Tier("villagers")));
        h.Events().ShouldBe([new AdviceRaised(0, active[0])]);
    }

    [Fact]
    public void FallbackLost_LowerTierProducerAdded_Clears()
    {
        var h = FallbackHarness.Create();
        EntityId hut = h.Add("fishing_hut", 0, 0);
        h.GrantReach(hut);
        h.Run();
        h.Active().ShouldHaveSingleItem();

        // A settler-staffed skiff restores the fallback: the note clears the next day.
        EntityId skiff = h.Add("old_skiff", 8, 0);
        h.GrantReach(skiff);
        h.AdvanceDays(1);

        h.Active().ShouldBeEmpty();
        h.Events().OfType<AdviceCleared>().ShouldContain(cleared => cleared.Kind == AdviceKind.NoLowerTierFallback);
    }

    [Fact]
    public void FallbackLost_EveryActiveProviderIsSameTierStaffed_RaisesForTheTier()
    {
        var h = FallbackHarness.Create();

        // The founders' hall is the only market in the overlay, and it is villagers-staffed there.
        h.Add("founders_hall", 0, 0);
        h.Run();

        AdviceView[] active = h.Active();
        active.ShouldHaveSingleItem();
        active[0].Kind.ShouldBe(AdviceKind.NoLowerTierFallback);
        active[0].Subject.ShouldBe(SubjectLink.ForTier(h.Tier("villagers")));
    }

    [Fact]
    public void FallbackLost_NoActiveMaker_StaysSilent()
    {
        var h = FallbackHarness.Create();

        // Nobody produces or provides anything yet: nothing was upgraded away, so nothing warns.
        h.Run();

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    [Fact]
    public void FallbackLost_LowerTierMakerActive_StaysSilent()
    {
        var h = FallbackHarness.Create();
        EntityId skiff = h.Add("old_skiff", 8, 0);
        h.GrantReach(skiff);

        // The settler-staffed skiff keeps the fish fallback standing from the start.
        h.Run();

        h.Active().ShouldBeEmpty();
        h.Events().ShouldBeEmpty();
    }

    /// <summary>
    /// The advisors wiring over fallback overlay content: the minimal pack with the fishing hut and the founders' hall
    /// re-staffed by villagers, plus a settler-staffed skiff that restores the fish fallback.
    /// </summary>
    private sealed class FallbackHarness
    {
        private readonly CommandQueue _commands = new();
        private readonly CommandRouter _router = new();
        private EntityId? _storehouse;

        private FallbackHarness(FakeSim sim)
        {
            Sim = sim;
        }

        public FakeSim Sim { get; }

        public AdvisorsSystem System { get; private set; } = null!;

        public ContentDb Content => Sim.Content;

        public static FallbackHarness Create()
        {
            var harness = new FallbackHarness(FakeSim.Create(FallbackContent.Db));
            harness.Start();
            return harness;
        }

        public TierId Tier(string name) => Content.IdOf<TierId>(name);

        public EntityId Add(string type, int x, int y, BuildingState state = BuildingState.Active) =>
            Sim.Services.BuildingRegistry.Add(Content.IdOf<BuildingTypeId>(type), x, y, Rotation.Deg0, state);

        public void GrantReach(EntityId building)
        {
            _storehouse ??= Sim.Services.BuildingRegistry.Add(
                Content.IdOf<BuildingTypeId>("storehouse"), 20, 20, Rotation.Deg0, BuildingState.Active);
            Sim.Queries.Roads.GrantRoadAccess(_storehouse.Value);
            Sim.Queries.Roads.GrantRoadAccess(building);
            Sim.Queries.Roads.SetReach(_storehouse.Value, building, 1000);
        }

        public void Run() => System.Run(Sim.Context);

        public void AdvanceDays(int days)
        {
            for (int day = 0; day < days; day++)
            {
                Sim.SetTick(Sim.World.Clock.Tick + Core.Time.GameTime.TicksPerDay);
                Run();
            }
        }

        public AdviceView[] Active() => System.Queries.Active().ToArray();

        public IReadOnlyList<SimEvent> Events() => Sim.DrainEvents();

        private void Start()
        {
            System = new AdvisorsSystem(Sim.World.Advisors, Content);
            System.Register(_router);
        }
    }

    /// <summary>
    /// The minimal content pack with villagers-staffed fallback losers: the fishing hut and the founders' hall need
    /// villagers, and a settler-staffed skiff fishes beside them.
    /// </summary>
    private static class FallbackContent
    {
        private const string Skiff = """
            {
              "id": "old_skiff",
              "category": "production",
              "era": 1,
              "footprint": [1, 1],
              "cost": { "timber": 1 },
              "buildDays": 1,
              "workforce": { "tier": "settlers", "count": 2 },
              "recipe": "catch_fish"
            }
            """;

        private static readonly Lazy<ContentDb> _db = new(Load);

        public static ContentDb Db => _db.Value;

        private static ContentDb Load()
        {
            DirectoryInfo copy = Directory.CreateTempSubdirectory("evenmere-fallback-content-");
            try
            {
                CopyDirectory(WorldBuilder.MinimalPackPath, copy.FullName);
                Replace(
                    Path.Combine(copy.FullName, "buildings", "fishing_hut.json"),
                    "\"tier\": \"settlers\"",
                    "\"tier\": \"villagers\"");
                Replace(
                    Path.Combine(copy.FullName, "buildings", "founders_hall.json"),
                    "\"outputs\": { \"knowledge\": 2, \"culture\": 1 },",
                    "\"workforce\": { \"tier\": \"villagers\", \"count\": 2 },\n  \"outputs\": { \"knowledge\": 2, \"culture\": 1 },");
                File.WriteAllText(Path.Combine(copy.FullName, "buildings", "old_skiff.json"), Skiff);
                return ContentLoader.Load(copy.FullName).GetDbOrThrow();
            }
            finally
            {
                copy.Delete(recursive: true);
            }
        }

        private static void Replace(string path, string oldText, string newText) =>
            File.WriteAllText(path, File.ReadAllText(path).Replace(oldText, newText, StringComparison.Ordinal));

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
}
