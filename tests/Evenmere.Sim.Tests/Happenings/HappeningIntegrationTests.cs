using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Happenings.Commands;

namespace Evenmere.Sim.Tests.Happenings;

/// <summary>The happenings end to end in a real simulation: the roll uses the real trigger evaluation, choosing grants
/// goods into the real pool and moves the real modifier index, and winter stays unruggable in spring.</summary>
public sealed class HappeningIntegrationTests
{
    private static Simulation NewSim() => Simulation.CreateNew(HappeningsPack.Content, new NewGameOptions { Seed = 42 });

    private static void TickDays(Simulation sim, int days)
    {
        for (long i = 0; i < (long)days * 120; i++)
        {
            sim.Tick();
        }
    }

    private static HappeningId Happening(Simulation sim, string name) => sim.Context.Content.IdOf<HappeningId>(name);

    private static GoodId Good(Simulation sim, string name) => sim.Context.Content.IdOf<GoodId>(name);

    [Fact]
    public void FullSim_RaiseAndChoose_GrantsGoodsIntoThePool()
    {
        Simulation sim = NewSim();
        HappeningId feast = Happening(sim, "test_feast");

        // The first daily step raises the feast: its spring trigger holds and its chance is certain.
        TickDays(sim, 1);
        sim.DrainEvents();
        sim.Query.Happenings.PendingHappening().ShouldBe(new PendingHappening(feast, 120));

        sim.Enqueue(new ChooseHappeningOption(feast, 0));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBe(new HappeningResolved(121, feast, 0));
        sim.Query.Storage.Stock(Good(sim, "fish")).ShouldBe(Amount.FromUnits(10));
        sim.Query.Happenings.PendingHappening().ShouldBeNull();
        sim.Query.Happenings.IsOnCooldown(feast).ShouldBeTrue();
    }

    [Fact]
    public void FullSim_ChooseModifierOption_MovesBonusPercent()
    {
        Simulation sim = NewSim();
        HappeningId feast = Happening(sim, "test_feast");
        TickDays(sim, 1);
        sim.DrainEvents();

        sim.Enqueue(new ChooseHappeningOption(feast, 1));
        sim.Tick();

        sim.DrainEvents().ShouldHaveSingleItem().ShouldBe(new HappeningResolved(121, feast, 1));
        sim.Query.Progression.BonusPercent(Stat.Productivity, ModifierContext.Island).ShouldBe(10);
    }

    [Fact]
    public void FullSim_WinterTrigger_EvaluatesFalseInSpring()
    {
        Simulation sim = NewSim();
        HappeningId winter = Happening(sim, "test_winter_tale");

        sim.Query.Progression.Evaluate(sim.Context.Content.Get(winter).Trigger).ShouldBeFalse();

        // The first raise is the feast, not the winter tale, even though both roll at certainty.
        TickDays(sim, 1);
        sim.Query.Happenings.PendingHappening().ShouldBe(new PendingHappening(Happening(sim, "test_feast"), 120));
    }
}
