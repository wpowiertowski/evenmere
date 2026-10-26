using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Progression.Eras;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Research.Commands;
using Evenmere.Sim.Systems.Culture.Commands;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;
using Evenmere.Sim.Tests.Research;

namespace Evenmere.Sim.Tests.Progression.Eras;

/// <summary>
/// Era advancement end to end (M2-11, per ADR-0010): the island stays in its era while only one gateway is complete,
/// advances the same tick both gateways are complete, and only then opens the next era's nodes. The full simulation
/// runs on the research pack, which carries the minimal pack's gateway eras plus era-2 nodes to test the lock.
/// </summary>
public sealed class EraAdvancementTests
{
    private static ContentDb Content => ResearchPack.Content;

    private static Simulation NewSimulation()
    {
        var sim = Simulation.CreateNew(Content, new NewGameOptions { Seed = 7 });
        ProgressionOf(sim).ApplyStartUnlocks(sim.Context);
        return sim;
    }

    private static Systems.Progression.ProgressionSystem ProgressionOf(Simulation sim) =>
        (Systems.Progression.ProgressionSystem)sim.Pipeline.Entries.Single(static e => e.Id == SystemId.Progression).System;

    private static List<SimEvent> Tick(Simulation sim, int ticks)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Tick();
            events.AddRange(sim.DrainEvents());
        }

        return events;
    }

    [Fact]
    public void TechGatewayAlone_DoesNotAdvanceTheEra()
    {
        Simulation sim = NewSimulation();
        EraId township = Content.IdOf<EraId>("township");
        TechId charter = Content.IdOf<TechId>("town_charter");

        sim.Enqueue(new SetResearchTarget(Content.IdOf<TechId>("net_weaving")));
        sim.Enqueue(new SetCivicTarget(Content.IdOf<CivicId>("village_moot")));
        sim.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(1000));
        List<SimEvent> events = Tick(sim, 120);
        events.ShouldNotContain(static e => e is Systems.Progression.EraBegan);

        sim.Enqueue(new SetResearchTarget(charter));
        events.AddRange(Tick(sim, 120));

        events.OfType<TechResearched>().Select(static e => e.Tech).ShouldBe([Content.IdOf<TechId>("net_weaving"), charter]);
        events.ShouldNotContain(static e => e is Systems.Progression.EraBegan);
        sim.Query.Progression.CurrentEra.ShouldBe(Content.IdOf<EraId>("founding"));
        sim.Query.Progression.GatewayStatus(township).ShouldBe(new GatewayStatus(true, false));
    }

    [Fact]
    public void BothGateways_AdvanceTheSameTickAndOpenEraTwoNodes()
    {
        Simulation sim = NewSimulation();
        EraId township = Content.IdOf<EraId>("township");
        TechId future = Content.IdOf<TechId>("test_future");
        CivicId futureCivic = Content.IdOf<CivicId>("test_future_civic");

        // Era-2 nodes stay unavailable while the island is still in the founding era, even with the tech gateway done.
        sim.Enqueue(new SetResearchTarget(Content.IdOf<TechId>("net_weaving")));
        sim.Enqueue(new SetCivicTarget(Content.IdOf<CivicId>("village_moot")));
        sim.Context.PointsLedger.Credit(PointKind.Knowledge, Amount.FromUnits(1000));
        Tick(sim, 120);
        sim.Enqueue(new SetResearchTarget(Content.IdOf<TechId>("town_charter")));
        List<SimEvent> events = Tick(sim, 120);
        sim.Query.Research.IsAvailable(future).ShouldBeFalse();
        sim.Query.Culture.IsAvailable(futureCivic).ShouldBeFalse();

        sim.Context.PointsLedger.Credit(PointKind.Culture, Amount.FromUnits(1000));
        events.AddRange(Tick(sim, 120));

        CivicAdopted adopted = events.OfType<CivicAdopted>().ShouldHaveSingleItem();
        Systems.Progression.EraBegan began = events.OfType<Systems.Progression.EraBegan>().ShouldHaveSingleItem();
        began.Tick.ShouldBe(adopted.Tick);
        began.Era.ShouldBe(township);
        sim.Query.Progression.CurrentEra.ShouldBe(township);
        sim.Query.Progression.GatewayStatus(township).ShouldBe(new GatewayStatus(true, true));

        sim.Query.Research.IsAvailable(future).ShouldBeTrue();
        sim.Query.Culture.IsAvailable(futureCivic).ShouldBeTrue();

        // The era does not advance again on later days: one gateway pair, one era.
        events.AddRange(Tick(sim, 240));
        events.OfType<Systems.Progression.EraBegan>().ShouldHaveSingleItem();
        sim.Query.Progression.CurrentEra.ShouldBe(township);
    }

    [Fact]
    public void Check_WithoutAGatewayPair_DoesNothing()
    {
        // The last era has no later era, so even completed gateways change nothing.
        var fake = FakeSim.Create();
        EraId township = WorldBuilder.Content.IdOf<EraId>("township");
        TechId charter = WorldBuilder.Content.IdOf<TechId>("town_charter");
        CivicId moot = WorldBuilder.Content.IdOf<CivicId>("village_moot");

        fake.World.Progression.Era = township.Value;
        fake.Queries.Research.SetTech(charter, Amount.FromUnits(160), done: true);
        fake.Queries.Culture.SetCivic(moot, Amount.FromUnits(140), done: true);

        EraAdvancement.Check(fake.World.Progression, fake.Context);

        fake.World.Progression.Era.ShouldBe(township.Value);
        fake.DrainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Check_WithOneGatewayMissing_DoesNothing()
    {
        var fake = FakeSim.Create();
        TechId charter = WorldBuilder.Content.IdOf<TechId>("town_charter");
        fake.Queries.Research.SetTech(charter, Amount.FromUnits(160), done: true);

        EraAdvancement.Check(fake.World.Progression, fake.Context);

        fake.World.Progression.Era.ShouldBe(WorldBuilder.Content.IdOf<EraId>("founding").Value);
        fake.DrainEvents().ShouldBeEmpty();
    }
}
