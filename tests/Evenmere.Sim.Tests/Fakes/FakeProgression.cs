using System.Collections.Immutable;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// A settable stand-in for <see cref="IProgressionQueries"/>. Nothing is unlocked and no modifier applies until the test
/// says so, and every condition evaluates to false unless <see cref="ConditionEvaluator"/> is set.
/// </summary>
internal sealed class FakeProgressionQueries : IProgressionQueries
{
    private readonly HashSet<BuildingTypeId> _buildings = [];
    private readonly HashSet<RecipeId> _recipes = [];
    private readonly HashSet<TierId> _tiers = [];
    private readonly HashSet<TerrainAccess> _access = [];
    private readonly HashSet<(FeatureKind Feature, GreatWorkTypeId? Type)> _features = [];
    private readonly HashSet<TraditionId> _traditions = [];
    private readonly int[] _traditionSlots = new int[Enum.GetValues<TraditionSlotType>().Length];
    private readonly List<(Stat Stat, ModifierContribution Contribution, Func<ModifierContext, bool>? AppliesTo)> _modifiers = [];
    private readonly SortedDictionary<(CounterKind Kind, int Subject), long> _counters = [];

    /// <summary>Gets or sets the era the island has reached.</summary>
    public EraId CurrentEra { get; set; }

    /// <summary>Gets or sets how conditions evaluate; the default makes every condition false.</summary>
    public Func<Condition, bool> ConditionEvaluator { get; set; } = static _ => false;

    /// <summary>Unlocks building types.</summary>
    /// <param name="buildings">The building types.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries Unlock(params BuildingTypeId[] buildings)
    {
        _buildings.UnionWith(buildings);
        return this;
    }

    /// <summary>Unlocks recipes.</summary>
    /// <param name="recipes">The recipes.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries Unlock(params RecipeId[] recipes)
    {
        _recipes.UnionWith(recipes);
        return this;
    }

    /// <summary>Unlocks tiers.</summary>
    /// <param name="tiers">The tiers.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries Unlock(params TierId[] tiers)
    {
        _tiers.UnionWith(tiers);
        return this;
    }

    /// <summary>Opens a kind of ground.</summary>
    /// <param name="access">The access flag.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries Open(TerrainAccess access)
    {
        _access.Add(access);
        return this;
    }

    /// <summary>Switches a feature on.</summary>
    /// <param name="feature">The feature.</param>
    /// <param name="greatWorkType">The great-work type, for the great-works feature.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries SwitchOn(FeatureKind feature, GreatWorkTypeId? greatWorkType = null)
    {
        _features.Add((feature, greatWorkType));
        return this;
    }

    /// <summary>Adds a modifier to a stat.</summary>
    /// <param name="stat">The stat.</param>
    /// <param name="contribution">The modifier; only active ones add to <see cref="BonusPercent"/>.</param>
    /// <param name="appliesTo">Which contexts it matches; the default is every context.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries AddModifier(Stat stat, ModifierContribution contribution, Func<ModifierContext, bool>? appliesTo = null)
    {
        _modifiers.Add((stat, contribution, appliesTo));
        return this;
    }

    /// <summary>Sets a counter.</summary>
    /// <param name="key">The counter.</param>
    /// <param name="value">The value.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries SetCounter(CounterKey key, long value)
    {
        _counters[(key.Kind, key.Subject)] = value;
        return this;
    }

    /// <summary>Adds to a counter, as <see cref="FakeCounters"/> does.</summary>
    /// <param name="key">The counter.</param>
    /// <param name="amount">How much to add.</param>
    public void AddToCounter(CounterKey key, long amount) => _counters[(key.Kind, key.Subject)] = Counter(key) + amount;

    /// <inheritdoc />
    public bool IsUnlocked(BuildingTypeId building) => _buildings.Contains(building);

    /// <inheritdoc />
    public bool IsUnlocked(RecipeId recipe) => _recipes.Contains(recipe);

    /// <inheritdoc />
    public bool IsUnlocked(TierId tier) => _tiers.Contains(tier);

    /// <summary>Unlocks tradition cards.</summary>
    /// <param name="traditions">The traditions.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries UnlockTraditions(params TraditionId[] traditions)
    {
        _traditions.UnionWith(traditions);
        return this;
    }

    /// <summary>Sets how many tradition slots of a type have been granted.</summary>
    /// <param name="slot">The slot type.</param>
    /// <param name="count">The number of slots.</param>
    /// <returns>This fake.</returns>
    public FakeProgressionQueries SetTraditionSlots(TraditionSlotType slot, int count)
    {
        _traditionSlots[(int)slot] = count;
        return this;
    }

    /// <inheritdoc />
    public bool HasAccess(TerrainAccess access) => _access.Contains(access);

    /// <inheritdoc />
    public bool HasFeature(FeatureKind feature, GreatWorkTypeId? greatWorkType = null) => _features.Contains((feature, greatWorkType));

    /// <inheritdoc />
    public int BonusPercent(Stat stat, in ModifierContext context)
    {
        int sum = 0;
        foreach ((Stat modifierStat, ModifierContribution contribution, Func<ModifierContext, bool>? appliesTo) in _modifiers)
        {
            if (modifierStat == stat && contribution.IsActive && (appliesTo is null || appliesTo(context)))
            {
                sum += contribution.Percent;
            }
        }

        return sum;
    }

    /// <inheritdoc />
    public void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions)
    {
        foreach ((Stat modifierStat, ModifierContribution contribution, Func<ModifierContext, bool>? appliesTo) in _modifiers)
        {
            if (modifierStat == stat && (appliesTo is null || appliesTo(context)))
            {
                contributions.Add(contribution);
            }
        }
    }

    /// <inheritdoc />
    public long Counter(CounterKey key) => _counters.GetValueOrDefault((key.Kind, key.Subject));

    /// <inheritdoc />
    bool IProgressionQueries.IsTraditionUnlocked(TraditionId tradition) => _traditions.Contains(tradition);

    /// <inheritdoc />
    int IProgressionQueries.TraditionSlotCount(TraditionSlotType slot) =>
        (uint)slot < (uint)_traditionSlots.Length ? _traditionSlots[(int)slot] : 0;

    /// <inheritdoc />
    public bool Evaluate(Condition condition) => ConditionEvaluator(condition);
}

/// <summary>Records the effects that were applied, by source. Applying a source again is recorded but changes nothing, as the real service guarantees.</summary>
internal sealed class FakeProgressionService : IProgressionService
{
    private readonly List<(EffectSource Source, ImmutableArray<Effect> Effects)> _calls = [];

    /// <summary>Gets every call, in order.</summary>
    public IReadOnlyList<(EffectSource Source, ImmutableArray<Effect> Effects)> Calls => _calls;

    /// <summary>Gets the sources that were applied, in the order they first were.</summary>
    public IEnumerable<EffectSource> AppliedSources => _calls.Select(static c => c.Source).Distinct();

    /// <summary>Gets the sources that were removed, in order.</summary>
    public List<EffectSource> RemovedSources { get; } = [];

    /// <inheritdoc />
    public void ApplyEffects(EffectSource source, ImmutableArray<Effect> effects) => _calls.Add((source, effects));

    /// <inheritdoc />
    void IProgressionService.RemoveSource(EffectSource source) => RemovedSources.Add(source);
}

/// <summary>A points ledger that adds up credits until they are taken, as the real one does.</summary>
internal sealed class FakePointsLedger : IPointsLedger
{
    private readonly Dictionary<PointKind, Amount> _credited = [];

    /// <summary>Reads what has been credited and not yet taken, without taking it.</summary>
    /// <param name="kind">The kind of point.</param>
    /// <returns>The credited total.</returns>
    public Amount Peek(PointKind kind) => _credited.GetValueOrDefault(kind);

    /// <inheritdoc />
    public void Credit(PointKind kind, Amount amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, Amount.Zero);
        _credited[kind] = Peek(kind) + amount;
    }

    /// <inheritdoc />
    public Amount TakeToday(PointKind kind)
    {
        Amount total = Peek(kind);
        _credited[kind] = Amount.Zero;
        return total;
    }
}

/// <summary>Counters that feed a <see cref="FakeProgressionQueries"/>, so what one system adds another reads back.</summary>
/// <param name="queries">The queries whose counters are written.</param>
internal sealed class FakeCounters(FakeProgressionQueries queries) : ICounters
{
    /// <inheritdoc />
    public void Add(CounterKey key, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        queries.AddToCounter(key, amount);
    }
}
