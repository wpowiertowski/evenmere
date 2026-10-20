using Evenmere.Core.Time;
using Evenmere.Sim.Diagnostics;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class PipelineTests
{
    // architecture.md, System order: steps 2 to 15, preceded by the four services (never ticked).
    private static readonly (string Name, Cadence Cadence)[] _expected =
    [
        ("map", Cadence.None),
        ("roads", Cadence.None),
        ("storage", Cadence.None),
        ("modifiers", Cadence.None),
        ("construction", Cadence.EveryTick),
        ("workforce", Cadence.Daily),
        ("production", Cadence.EveryTick),
        ("logistics", Cadence.EveryTick),
        ("exploration", Cadence.EveryTick),
        ("needs", Cadence.DailyBucketed),
        ("population", Cadence.DailyBucketed),
        ("economy", Cadence.DailyBucketed),
        ("research", Cadence.Daily),
        ("culture", Cadence.Daily),
        ("progression", Cadence.Daily),
        ("happenings", Cadence.Daily),
        ("advisors", Cadence.Daily),
        ("statistics", Cadence.Daily),
    ];

    [Fact]
    public void Entries_DefaultPipeline_HaveExactNamesOrderAndCadences()
    {
        Simulation sim = new WorldBuilder().Build();

        (string Name, Cadence Cadence)[] actual = [.. sim.Pipeline.Entries.Select(static e => (e.System.Name, e.Cadence))];

        actual.ShouldBe(_expected);
    }

    [Fact]
    public void Entries_DefaultPipeline_TickedSystemsFollowArchitectureOrder()
    {
        Simulation sim = new WorldBuilder().Build();

        string[] ticked = [.. sim.Pipeline.Entries.Where(static e => e.Cadence != Cadence.None).Select(static e => e.System.Name)];

        ticked.ShouldBe(
        [
            "construction", "workforce", "production", "logistics", "exploration", "needs", "population",
            "economy", "research", "culture", "progression", "happenings", "advisors", "statistics",
        ]);
    }

    [Fact]
    public void Entries_Services_AreNeverTicked()
    {
        Simulation sim = new WorldBuilder().Build();

        string[] services = [.. sim.Pipeline.Entries.Where(static e => e.Cadence == Cadence.None).Select(static e => e.System.Name)];

        services.ShouldBe(["map", "roads", "storage", "modifiers"]);
    }

    [Fact]
    public void Entries_EverySystemId_AppearsExactlyOnce()
    {
        Simulation sim = new WorldBuilder().Build();

        SystemId[] ids = [.. sim.Pipeline.Entries.Select(static e => e.Id)];

        ids.Order().ShouldBe(Enum.GetValues<SystemId>());
        ids.Select(static id => SystemNames.Of(id)).ShouldBe(sim.Pipeline.Entries.Select(static e => e.System.Name));
    }

    [Fact]
    public void Constructor_SystemWithWrongName_Throws()
    {
        var mismatched = new PipelineEntry(SystemId.Map, new SpySystem(SystemId.Roads, []), Cadence.None);

        Should.Throw<ArgumentException>(() => new SystemPipeline([mismatched], new SimProfiler()));
    }

    [Fact]
    public void Constructor_SameSystemTwice_Throws()
    {
        var first = new PipelineEntry(SystemId.Map, new SpySystem(SystemId.Map, []), Cadence.None);
        var second = new PipelineEntry(SystemId.Map, new SpySystem(SystemId.Map, []), Cadence.EveryTick);

        Should.Throw<ArgumentException>(() => new SystemPipeline([first, second], new SimProfiler()));
    }

    [Fact]
    public void SystemNames_Of_MatchesTheEnumMemberForMember()
    {
        string[] names = Enum.GetNames<SystemId>();

        names.Length.ShouldBe(SystemNames.Count);
        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            SystemNames.Of(id).ShouldBe(id.ToString().ToLowerInvariant());
        }
    }

    [Fact]
    public void SystemNames_Of_UndefinedId_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SystemNames.Of((SystemId)SystemNames.Count));
        Should.Throw<ArgumentOutOfRangeException>(() => SystemNames.Of((SystemId)(-1)));
    }

    [Fact]
    public void IsDue_EachCadence_FollowsTheClock()
    {
        var dayStart = new GameClock(120);
        var midDay = new GameClock(121);

        SystemPipeline.IsDue(Cadence.None, dayStart).ShouldBeFalse();
        SystemPipeline.IsDue(Cadence.EveryTick, midDay).ShouldBeTrue();
        SystemPipeline.IsDue(Cadence.DailyBucketed, midDay).ShouldBeTrue();
        SystemPipeline.IsDue(Cadence.Daily, dayStart).ShouldBeTrue();
        SystemPipeline.IsDue(Cadence.Daily, midDay).ShouldBeFalse();
    }
}
