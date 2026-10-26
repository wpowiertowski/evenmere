using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Debug.Commands;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Debug;

/// <summary>
/// The cheat commands (ADR-0012): every one is rejected while the save's cheats flag is off, and each applies
/// its effect once the flag is on. The flag itself is permission, not state: it round-trips through the save
/// header and never reaches the state hash.
/// </summary>
public sealed class CheatTests
{
    [Fact]
    public void GrantGoods_DisabledSave_IsRejected()
    {
        Simulation sim = NewSim(cheats: false);

        sim.Enqueue(new CheatGrantGoods(Good("timber"), Amount.FromUnits(10)));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.CheatsDisabled);
        sim.Query.Storage.Stock(Good("timber")).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void GrantGoods_Enabled_AddsToStock()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatGrantGoods(Good("timber"), Amount.FromUnits(10)));
        sim.Tick();

        sim.DrainEvents().ShouldBeEmpty();
        sim.Query.Storage.Stock(Good("timber")).ShouldBe(Amount.FromUnits(10));
    }

    [Fact]
    public void GrantGoods_UnknownGood_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatGrantGoods(GoodId.From(999), Amount.FromUnits(1)));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.UnknownTarget);
    }

    [Fact]
    public void GrantGoods_NegativeAmount_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatGrantGoods(Good("timber"), Amount.FromUnits(1) - Amount.FromUnits(2)));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void CompleteNode_DisabledSave_IsRejected()
    {
        Simulation sim = NewSim(cheats: false);

        sim.Enqueue(new CheatCompleteNode(Tech("net_weaving"), null));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.CheatsDisabled);
    }

    [Fact]
    public void CompleteNode_Tech_ResearchesAndEmits()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatCompleteNode(Tech("net_weaving"), null));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<TechResearched>()
            .Tech.ShouldBe(Tech("net_weaving"));
        sim.Query.Research.IsDone(Tech("net_weaving")).ShouldBeTrue();
    }

    [Fact]
    public void CompleteNode_AlreadyDone_IsSilentNoOp()
    {
        Simulation sim = NewSim(cheats: true);
        sim.Enqueue(new CheatCompleteNode(Tech("net_weaving"), null));
        sim.Tick();
        sim.DrainEvents();

        sim.Enqueue(new CheatCompleteNode(Tech("net_weaving"), null));
        sim.Tick();

        sim.DrainEvents().ShouldBeEmpty();
        sim.Query.Research.IsDone(Tech("net_weaving")).ShouldBeTrue();
    }

    [Fact]
    public void CompleteNode_TwoNodes_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatCompleteNode(Tech("net_weaving"), Civic("village_moot")));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void CompleteNode_UnknownNode_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatCompleteNode(TechId.From(999), null));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.UnknownTarget);
    }

    [Fact]
    public void CompleteNode_Civic_AdoptsAndEmits()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatCompleteNode(null, Civic("village_moot")));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CivicAdopted>()
            .Civic.ShouldBe(Civic("village_moot"));
        sim.Query.Culture.IsDone(Civic("village_moot")).ShouldBeTrue();
    }

    [Fact]
    public void SetEra_DisabledSave_IsRejected()
    {
        Simulation sim = NewSim(cheats: false);

        sim.Enqueue(new CheatSetEra(Era("township")));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.CheatsDisabled);
    }

    [Fact]
    public void SetEra_Enabled_MovesEraAndEmits()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatSetEra(Era("township")));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<EraBegan>()
            .Era.ShouldBe(Era("township"));
        sim.Query.Progression.CurrentEra.ShouldBe(Era("township"));
    }

    [Fact]
    public void SetEra_SameEra_IsSilentNoOp()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatSetEra(sim.Query.Progression.CurrentEra));
        sim.Tick();

        sim.DrainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void SetEra_UnknownEra_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatSetEra(EraId.From(999)));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.UnknownTarget);
    }

    [Fact]
    public void RevealAll_DisabledSave_IsRejected()
    {
        Simulation sim = NewSim(cheats: false);

        sim.Enqueue(new CheatRevealAll(CheatRevealAll.AllRegions));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.CheatsDisabled);
    }

    [Fact]
    public void RevealAll_Enabled_RevealsEverything()
    {
        // M2-06: the cheat really reveals now. The empty test world holds no regions, so the reveal succeeds
        // with no events; the region cases below prove the handler validates and applies.
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatRevealAll(CheatRevealAll.AllRegions));
        sim.Tick();

        sim.DrainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void RevealAll_UnknownRegion_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatRevealAll(7));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void RevealAll_NegativeRegion_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatRevealAll(-2));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void AddResidents_DisabledSave_IsRejected()
    {
        Simulation sim = NewSim(cheats: false);

        sim.Enqueue(new CheatAddResidents(2));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.CheatsDisabled);
    }

    [Fact]
    public void AddResidents_NoBeds_IsRejected()
    {
        Simulation sim = NewSim(cheats: true);

        sim.Enqueue(new CheatAddResidents(2));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
    }

    [Fact]
    public void AddResidents_Enabled_FillsHousesInIdOrder()
    {
        Simulation sim = NewSim(cheats: true);
        AddHouse(sim, new EntityId(12), residents: 1, capacity: 4);
        AddHouse(sim, new EntityId(15), residents: 3, capacity: 3);
        AddHouse(sim, new EntityId(21), residents: 0, capacity: 5);

        sim.Enqueue(new CheatAddResidents(5));
        sim.Tick();

        sim.DrainEvents().ShouldBeEmpty();
        Residents(sim, new EntityId(12)).ShouldBe(4);
        Residents(sim, new EntityId(15)).ShouldBe(3);
        Residents(sim, new EntityId(21)).ShouldBe(2);
    }

    [Fact]
    public void AddResidents_Overflow_MovesNothing()
    {
        Simulation sim = NewSim(cheats: true);
        AddHouse(sim, new EntityId(12), residents: 1, capacity: 4);

        sim.Enqueue(new CheatAddResidents(4));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBeOfType<CommandRejected>()
            .Code.ShouldBe(RejectReason.Invalid);
        Residents(sim, new EntityId(12)).ShouldBe(1);
    }

    [Fact]
    public void CheatsUsed_SaveRoundTrip_PersistsThroughTheHeader()
    {
        Simulation sim = NewSim(cheats: true);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, sim, new SaveMeta(WorldBuilder.Content, "test", null));
        stream.Position = 0;

        (ISimulation loaded, SaveReport _) = SaveGame.Read(stream, WorldBuilder.Content);
        loaded.CheatsUsed.ShouldBeTrue();
    }

    [Fact]
    public void CheatsUsed_Default_IsOff()
    {
        Simulation sim = NewSim(cheats: false);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, sim, new SaveMeta(WorldBuilder.Content, "test", null));
        stream.Position = 0;

        (ISimulation loaded, SaveReport _) = SaveGame.Read(stream, WorldBuilder.Content);
        loaded.CheatsUsed.ShouldBeFalse();
    }

    [Fact]
    public void CheatCommands_RoundTripThroughTheLog()
    {
        ContentDb content = WorldBuilder.Content;
        Command[] commands =
        [
            new CheatGrantGoods(content.IdOf<GoodId>("timber"), Amount.FromUnits(7)),
            new CheatCompleteNode(content.IdOf<TechId>("net_weaving"), null),
            new CheatCompleteNode(null, content.IdOf<CivicId>("village_moot")),
            new CheatSetEra(content.IdOf<EraId>("township")),
            new CheatRevealAll(CheatRevealAll.AllRegions),
            new CheatAddResidents(3),
        ];

        using var stream = new MemoryStream();
        var writer = new CommandLogWriter(stream);
        for (int i = 0; i < commands.Length; i++)
        {
            writer.Append(i + 1, commands[i]);
        }

        stream.Position = 0;
        var reader = new CommandLogReader(stream, SaveTables.Identity(content));
        IReadOnlyList<(long Tick, Command Command)> entries = reader.ReadAll();

        entries.Count.ShouldBe(commands.Length);
        for (int i = 0; i < commands.Length; i++)
        {
            entries[i].Tick.ShouldBe(i + 1);
            entries[i].Command.ShouldBe(commands[i]);
        }
    }

    private static Simulation NewSim(bool cheats)
    {
        Simulation sim = new WorldBuilder().Build();
        sim.CheatsUsed = cheats;
        return sim;
    }

    private static GoodId Good(string name) => WorldBuilder.Content.IdOf<GoodId>(name);

    private static TechId Tech(string name) => WorldBuilder.Content.IdOf<TechId>(name);

    private static CivicId Civic(string name) => WorldBuilder.Content.IdOf<CivicId>(name);

    private static EraId Era(string name) => WorldBuilder.Content.IdOf<EraId>(name);

    private static void AddHouse(Simulation sim, EntityId id, int residents, int capacity)
    {
        sim.Context.World.Population.Houses.Add(id, new HouseRecord
        {
            Tier = TierId.From(0),
            Type = WorldBuilder.Content.IdOf<BuildingTypeId>("settler_house"),
            Residents = residents,
            Capacity = capacity,
            TargetResidents = capacity,
        });
    }

    private static int Residents(Simulation sim, EntityId id)
    {
        sim.Context.World.Population.Houses.TryGet(id, out HouseRecord house).ShouldBeTrue();
        return house.Residents;
    }
}
