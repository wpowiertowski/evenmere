using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Culture;
using Evenmere.Game.Ui.Research;
using Evenmere.Sim;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Debug.Commands;

namespace Evenmere.Game.Tests;

/// <summary>
/// The M2-36 stale-tree regression: a panel that only watches the content object keeps the old world's answers
/// after a load, because the host loads content once and reuses it. A save with every Era I tech and civic done
/// must read back done through fresh view-models built from the loaded simulation's queries, and the loaded
/// queries must be a new object, which is what the panels now watch to rebuild their models.
/// </summary>
public sealed class TreeReloadTests
{
    [Fact]
    public void ResearchModel_ShowsLoadedSaveState()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());
        Simulation sim = StartWith(content, AllTechs(content), AllCivics(content));

        var before = new ResearchViewModel(content, strings, sim.Query.Research, sim.Query.Progression);
        before.Advance(10.0);
        before.Snapshot.DoneCount.ShouldBe(content.Techs.Count);

        ISimulation loaded = RoundTrip(content, sim);

        var after = new ResearchViewModel(content, strings, loaded.Query.Research, loaded.Query.Progression);
        after.Advance(10.0);
        after.Snapshot.DoneCount.ShouldBe(content.Techs.Count);
        loaded.ComputeStateHash().ShouldBe(sim.ComputeStateHash());
        ReferenceEquals(sim.Query, loaded.Query).ShouldBeFalse();
    }

    [Fact]
    public void CultureModel_ShowsLoadedSaveState()
    {
        ContentDb content = TestContent.Db;
        var strings = new UiStrings(TestLocales.Table());
        Simulation sim = StartWith(content, AllTechs(content), AllCivics(content));

        var before = new CultureViewModel(content, strings, sim.Query.Culture, sim.Query.Progression);
        before.Advance(10.0);
        before.Snapshot.DoneCount.ShouldBe(content.Civics.Count);

        ISimulation loaded = RoundTrip(content, sim);

        var after = new CultureViewModel(content, strings, loaded.Query.Culture, loaded.Query.Progression);
        after.Advance(10.0);
        after.Snapshot.DoneCount.ShouldBe(content.Civics.Count);
        loaded.ComputeStateHash().ShouldBe(sim.ComputeStateHash());
        ReferenceEquals(sim.Query, loaded.Query).ShouldBeFalse();
    }

    private static List<TechId> AllTechs(ContentDb content)
    {
        var ids = new List<TechId>(content.Techs.Count);
        for (int i = 0; i < content.Techs.Count; i++)
        {
            ids.Add(TechId.From(checked((ushort)i)));
        }

        return ids;
    }

    private static List<CivicId> AllCivics(ContentDb content)
    {
        var ids = new List<CivicId>(content.Civics.Count);
        for (int i = 0; i < content.Civics.Count; i++)
        {
            ids.Add(CivicId.From(checked((ushort)i)));
        }

        return ids;
    }

    private static Simulation StartWith(ContentDb content, List<TechId> techs, List<CivicId> civics)
    {
        var sim = Simulation.CreateNew(content, new NewGameOptions { Seed = 7 });
        sim.CheatsUsed = true;
        foreach (TechId tech in techs)
        {
            sim.Enqueue(new CheatCompleteNode(tech, null));
        }

        foreach (CivicId civic in civics)
        {
            sim.Enqueue(new CheatCompleteNode(null, civic));
        }

        for (int i = 0; i < 4; i++)
        {
            sim.Tick();
        }

        _ = sim.DrainEvents();
        return sim;
    }

    private static ISimulation RoundTrip(ContentDb content, ISimulation sim)
    {
        using var stream = new MemoryStream();
        SaveGame.Write(stream, sim, new SaveMeta(content, "test", "starter"));
        stream.Position = 0;
        (ISimulation loaded, SaveReport _) = SaveGame.Read(stream, content);
        return loaded;
    }
}
