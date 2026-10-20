using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Tests.Architecture;

namespace Evenmere.Sim.Tests.Progression;

public sealed class ProgressionStateTests
{
    public static TheoryData<string, Action<ProgressionState>> WrongSizes() => new()
    {
        { nameof(ProgressionState.Buildings), static s => s.Buildings = new ulong[5] },
        { nameof(ProgressionState.Recipes), static s => s.Recipes = new ulong[5] },
        { nameof(ProgressionState.Tiers), static s => s.Tiers = new ulong[5] },
        { nameof(ProgressionState.Traditions), static s => s.Traditions = new ulong[5] },
        { nameof(ProgressionState.Festivals), static s => s.Festivals = new ulong[5] },
        { nameof(ProgressionState.GreatWorks), static s => s.GreatWorks = new ulong[5] },
        { nameof(ProgressionState.TraditionSlots), static s => s.TraditionSlots = new int[2] },
        { nameof(ProgressionState.Points), static s => s.Points = new Amount[2] },
        { nameof(ProgressionState.Produced), static s => s.Produced = new long[2] },
        { nameof(ProgressionState.Consumed), static s => s.Consumed = new long[2] },
        { nameof(ProgressionState.Built), static s => s.Built = new long[2] },
    };

    public static TheoryData<string, Action<ProgressionState>> Impossible() => new()
    {
        { "era past the last", static s => s.Era = 99 },
        { "negative era", static s => s.Era = -1 },
        { "negative road tiles", static s => s.RoadTilesLaid = -1 },
        { "negative produced", static s => s.Produced[0] = -1 },
        { "negative built", static s => s.Built[0] = -1 },
        { "negative slots", static s => s.TraditionSlots[0] = -1 },
        { "negative points", static s => s.Points[0] = Amount.FromUnits(-1) },
        { "unsorted sources", static s => s.AppliedSources = [EffectSource.ForCivic(new CivicId(0)), EffectSource.ForTech(new TechId(0))] },
        { "repeated sources", static s => s.AppliedSources = [EffectSource.ForTech(new TechId(0)), EffectSource.ForTech(new TechId(0))] },
    };

    [Fact]
    public void FitTo_NewState_SizesEveryArrayToTheContent()
    {
        ContentDb content = ProgressionPack.Content;
        var state = new ProgressionState();

        state.FitTo(content);

        state.Buildings.Length.ShouldBe(Bits.WordCount(content.Buildings.Count));
        state.Recipes.Length.ShouldBe(Bits.WordCount(content.Recipes.Count));
        state.Tiers.Length.ShouldBe(Bits.WordCount(content.Tiers.Count));
        state.Traditions.Length.ShouldBe(Bits.WordCount(content.Traditions.Count));
        state.Festivals.Length.ShouldBe(Bits.WordCount(content.Festivals.Count));
        state.GreatWorks.Length.ShouldBe(Bits.WordCount(content.GreatWorks.Count));
        state.TraditionSlots.Length.ShouldBe(Enum.GetValues<TraditionSlotType>().Length);
        state.Points.Length.ShouldBe(Enum.GetValues<PointKind>().Length);
        state.Produced.Length.ShouldBe(content.Goods.Count);
        state.Consumed.Length.ShouldBe(content.Goods.Count);
        state.Built.Length.ShouldBe(content.Buildings.Count);
    }

    [Fact]
    public void FitTo_AlreadyFittedState_KeepsItsArrays()
    {
        var state = new ProgressionState();
        state.FitTo(ProgressionPack.Content);
        ulong[] buildings = state.Buildings;
        long[] produced = state.Produced;

        state.FitTo(ProgressionPack.Content);

        state.Buildings.ShouldBeSameAs(buildings);
        state.Produced.ShouldBeSameAs(produced);
    }

    [Theory]
    [MemberData(nameof(WrongSizes))]
    public void FitTo_ArrayOfTheWrongLength_Throws(string member, Action<ProgressionState> corrupt)
    {
        var state = new ProgressionState();
        state.FitTo(ProgressionPack.Content);
        corrupt(state);

        InvalidDataException thrown = Should.Throw<InvalidDataException>(() => state.FitTo(ProgressionPack.Content));

        thrown.Message.ShouldContain(member);
    }

    [Theory]
    [MemberData(nameof(Impossible))]
    public void FitTo_ValueNoGameCouldHaveProduced_Throws(string what, Action<ProgressionState> corrupt)
    {
        var state = new ProgressionState();
        state.FitTo(ProgressionPack.Content);
        corrupt(state);

        Should.Throw<InvalidDataException>(() => state.FitTo(ProgressionPack.Content), what);
    }

    [Fact]
    public void ProgressionSystem_OverAStateForOtherContent_Throws()
    {
        var state = new ProgressionState { Produced = new long[2] };
        var modifiers = new ModifiersSystem(new ModifiersState(), ProgressionPack.Content);

        Should.Throw<InvalidDataException>(() => new ProgressionSystem(state, ProgressionPack.Content, modifiers));
    }

    [Fact]
    public void ModifiersSystem_OverAModifierTheContentCannotExplain_Throws()
    {
        var state = new ModifiersState
        {
            Active = [new ActiveModifier { Source = EffectSource.ForTech(new TechId(0)), Stat = Stat.Productivity, TargetKind = ModifierTargetKind.Building, TargetValue = 999, Percent = 5 }],
        };

        Should.Throw<InvalidDataException>(() => new ModifiersSystem(state, ProgressionPack.Content));
    }

    [Fact]
    public void ModifiersSystem_OverNullList_StartsEmpty()
    {
        var state = new ModifiersState { Active = null! };

        var system = new ModifiersSystem(state, ProgressionPack.Content);

        system.Count.ShouldBe(0);
        state.Active.ShouldBeEmpty();
    }

    [Fact]
    public void Save_AfterUnlocksAndCounters_ReloadsToTheSameBytesAndAnswers()
    {
        var harness = ProgressionHarness.Create();
        harness.ApplyStartUnlocks();
        harness.ApplyTech("milling");
        harness.ApplyTech("kitchen_tech");
        harness.Context.Counters.Add(CounterKey.Produced(harness.Good("grain")), 3300);
        harness.Context.PointsLedger.Credit(PointKind.Culture, Amount.FromUnits(2));
        byte[] saved = harness.Save();

        var reloaded = ProgressionHarness.Reload(harness);

        reloaded.Save().ShouldBe(saved);
        for (int i = 0; i < harness.Content.Buildings.Count; i++)
        {
            var id = BuildingTypeId.From((ushort)i);
            reloaded.Queries.IsUnlocked(id).ShouldBe(harness.Queries.IsUnlocked(id), harness.Content.Buildings.Names[i]);
        }

        reloaded.Queries.CurrentEra.ShouldBe(harness.Queries.CurrentEra);
        reloaded.Queries.HasAccess(TerrainAccess.NarrowRivers).ShouldBeTrue();
        reloaded.Queries.HasFeature(FeatureKind.Coin).ShouldBeTrue();
        reloaded.Queries.TraditionSlots(TraditionSlotType.Craft).ShouldBe(2);
        reloaded.State.AppliedSources.ShouldBe(harness.State.AppliedSources);
        reloaded.Queries.Counter(CounterKey.Produced(reloaded.Good("grain"))).ShouldBe(3300);
    }

    [Fact]
    public void Reload_AppliedSources_StillMakeApplyingThemANoOp()
    {
        var harness = ProgressionHarness.Create();
        harness.ApplyTech("kitchen_tech");
        var reloaded = ProgressionHarness.Reload(harness);
        byte[] before = reloaded.Save();

        reloaded.ApplyTech("kitchen_tech");

        reloaded.Save().ShouldBe(before);
        reloaded.Modifiers.Count.ShouldBe(7);
    }

    [Fact]
    public void StateHash_Unlock_ChangesTheHash()
    {
        var harness = ProgressionHarness.Create();
        ulong before = StateSerializer.ComputeHash(harness.Fake.World);

        harness.Apply(EffectSource.ForTech(new TechId(0)), new UnlockBuildingEffect(harness.Building("bakery")));

        StateSerializer.ComputeHash(harness.Fake.World).ShouldNotBe(before);
    }

    [Theory]
    [InlineData("building")]
    [InlineData("recipe")]
    [InlineData("tier")]
    [InlineData("tradition")]
    [InlineData("festival")]
    [InlineData("access")]
    [InlineData("feature")]
    [InlineData("great work")]
    [InlineData("era")]
    public void StateHash_EachKindOfUnlock_IsInTheHash(string kind)
    {
        // Both worlds applied the same source, so only the unlock itself can move the hash.
        var source = EffectSource.ForTech(new TechId(0));
        var baseline = ProgressionHarness.Create();
        baseline.Apply(source);
        var changed = ProgressionHarness.Create();
        changed.Apply(source);

        if (kind == "era")
        {
            changed.State.Era = changed.Content.IdOf<EraId>("township").Value;
        }
        else
        {
            changed.Context.ProgressionService.ApplyEffects(EffectSource.ForTech(new TechId(1)), [UnlockOf(kind, changed)]);
            baseline.Apply(EffectSource.ForTech(new TechId(1)));
        }

        StateSerializer.ComputeHash(changed.Fake.World).ShouldNotBe(StateSerializer.ComputeHash(baseline.Fake.World), kind);
    }

    private static Effect UnlockOf(string kind, ProgressionHarness harness) => kind switch
    {
        "building" => new UnlockBuildingEffect(harness.Building("bakery")),
        "recipe" => new UnlockRecipeEffect(harness.Content.IdOf<RecipeId>("bake_bread")),
        "tier" => new UnlockTierEffect(harness.Tier("townsfolk")),
        "tradition" => new UnlockTraditionEffect(harness.Content.IdOf<TraditionId>("harvest_festival")),
        "festival" => new UnlockFestivalEffect(harness.Content.IdOf<FestivalId>("harvest_fair")),
        "access" => new UnlockTerrainAccessEffect(TerrainAccess.Reefs),
        "feature" => new UnlockFeatureEffect(FeatureKind.Visitors, null),
        _ => new UnlockFeatureEffect(FeatureKind.GreatWorks, harness.Content.IdOf<GreatWorkTypeId>("relic")),
    };

    [Fact]
    public void StateTypes_FollowTheArchitectureRules()
    {
        foreach (Type type in new[] { typeof(ProgressionState), typeof(ModifiersState), typeof(ActiveModifier) })
        {
            ForbiddenFieldTypeRules.Check(type).ShouldBeEmpty(type.Name);
            MessagePackKeyRules.Check(type).ShouldBeEmpty(type.Name);
            MessagePackKeyRules.CheckContiguousFromZero(type).ShouldBeEmpty(type.Name);
        }
    }

    [Fact]
    public void ModifiersState_HoldsOnlyTheModifierList_NotTheDerivedIndex()
    {
        typeof(ModifiersState).GetProperties().Select(static p => p.Name).ShouldBe([nameof(ModifiersState.Active)]);
    }

    [Fact]
    public void Season_Boundaries_UseTheClockOfTheContext()
    {
        // The harness clock is the world clock, so moving it moves what the evaluator sees.
        var harness = ProgressionHarness.Create();

        harness.Fake.SetTick(GameTime.TicksPerYear);

        harness.Queries.Evaluate(new SeasonCondition(Season.Spring)).ShouldBeTrue();
    }
}

public sealed class BitsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(64, 1)]
    [InlineData(65, 2)]
    [InlineData(128, 2)]
    [InlineData(129, 3)]
    public void WordCount_BitCount_RoundsUpToWholeWords(int bits, int words)
    {
        Bits.WordCount(bits).ShouldBe(words);
    }

    [Fact]
    public void SetAndHas_AcrossWordBoundaries_AreIndependentBits()
    {
        ulong[] words = new ulong[3];

        foreach (int index in new[] { 0, 63, 64, 130 })
        {
            Bits.Set(words, index);
        }

        foreach (int index in new[] { 0, 63, 64, 130 })
        {
            Bits.Has(words, index).ShouldBeTrue(index.ToString());
        }

        foreach (int index in new[] { 1, 62, 65, 129, 131, 191 })
        {
            Bits.Has(words, index).ShouldBeFalse(index.ToString());
        }

        Bits.Count(words).ShouldBe(4);
    }

    [Fact]
    public void Has_PastTheEndOrNegative_IsFalse()
    {
        ulong[] words = [ulong.MaxValue];

        Bits.Has(words, 64).ShouldBeFalse();
        Bits.Has(words, -1).ShouldBeFalse();
        Bits.Has([], 0).ShouldBeFalse();
    }

    [Fact]
    public void Set_PastTheEndOrNegative_Throws()
    {
        ulong[] words = new ulong[1];

        Should.Throw<ArgumentOutOfRangeException>(() => Bits.Set(words, 64));
        Should.Throw<ArgumentOutOfRangeException>(() => Bits.Set(words, -1));
    }
}
