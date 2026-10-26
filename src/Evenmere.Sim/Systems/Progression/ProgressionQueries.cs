using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Systems.Modifiers;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// Implements <see cref="IProgressionQueries"/> over the progression state, the modifier index, the milestone counters and
/// the condition evaluator. It also answers a few questions the interface does not carry yet (tradition and festival
/// unlocks, tradition slots, the coastline flag), which the culture and exploration systems read in M2. The era
/// gateway status reads the content's era definitions.
/// </summary>
internal sealed class ProgressionQueries : IProgressionQueries
{
    private readonly ProgressionState _state;
    private readonly ModifierIndex _modifiers;
    private readonly MilestoneCounters _counters;
    private readonly ConditionEvaluator _evaluator;
    private readonly ContentDb _content;

    /// <summary>Initializes a new instance of the <see cref="ProgressionQueries"/> class.</summary>
    /// <param name="state">The progression state, already fitted to the content.</param>
    /// <param name="modifiers">The modifier index that answers the stat queries.</param>
    /// <param name="counters">The milestone counters.</param>
    /// <param name="evaluator">The condition evaluator.</param>
    /// <param name="content">The loaded content, which carries the era definitions for the gateway status.</param>
    public ProgressionQueries(ProgressionState state, ModifierIndex modifiers, MilestoneCounters counters, ConditionEvaluator evaluator, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modifiers);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(content);

        _state = state;
        _modifiers = modifiers;
        _counters = counters;
        _evaluator = evaluator;
        _content = content;
    }

    /// <inheritdoc />
    public EraId CurrentEra => EraId.From(checked((ushort)_state.Era));

    /// <summary>Gets a value indicating whether an effect has asked to reveal the coastline (read by exploration in M2-06).</summary>
    public bool CoastlineRevealed => _state.CoastlineRevealed;

    /// <inheritdoc />
    public bool IsUnlocked(BuildingTypeId building) => Bits.Has(_state.Buildings, building.Value);

    /// <inheritdoc />
    public bool IsUnlocked(RecipeId recipe) => Bits.Has(_state.Recipes, recipe.Value);

    /// <inheritdoc />
    public bool IsUnlocked(TierId tier) => Bits.Has(_state.Tiers, tier.Value);

    /// <summary>Tests whether a tradition card is unlocked (read by the culture system in M2-07).</summary>
    /// <param name="tradition">The tradition.</param>
    /// <returns><see langword="true"/> if it is unlocked.</returns>
    public bool IsUnlocked(TraditionId tradition) => Bits.Has(_state.Traditions, tradition.Value);

    /// <summary>Tests whether a festival is unlocked (read by the culture system in M2-08).</summary>
    /// <param name="festival">The festival.</param>
    /// <returns><see langword="true"/> if it is unlocked.</returns>
    public bool IsUnlocked(FestivalId festival) => Bits.Has(_state.Festivals, festival.Value);

    /// <summary>Counts the tradition slots of a type that effects have granted (read by the culture system in M2-07).</summary>
    /// <param name="slot">The slot type.</param>
    /// <returns>The number of slots; zero or more.</returns>
    public int TraditionSlots(TraditionSlotType slot) =>
        (uint)slot < (uint)_state.TraditionSlots.Length ? _state.TraditionSlots[(int)slot] : 0;

    /// <inheritdoc />
    bool IProgressionQueries.IsTraditionUnlocked(TraditionId tradition) => IsUnlocked(tradition);

    /// <inheritdoc />
    bool IProgressionQueries.IsFestivalUnlocked(FestivalId festival) => IsUnlocked(festival);

    /// <inheritdoc />
    int IProgressionQueries.TraditionSlotCount(TraditionSlotType slot) => TraditionSlots(slot);

    /// <inheritdoc />
    GatewayStatus IProgressionQueries.GatewayStatus(EraId era)
    {
        if ((uint)era.Value >= (uint)_content.Eras.Count)
        {
            return new GatewayStatus(false, false);
        }

        EraDef def = _content.Get(era);
        bool techDone = def.RequiresTech is { } tech && IsSourceApplied(EffectSource.ForTech(tech));
        bool civicDone = def.RequiresCivic is { } civic && IsSourceApplied(EffectSource.ForCivic(civic));
        return new GatewayStatus(techDone, civicDone);
    }

    // A gateway's source is recorded exactly once, when research or culture completes the node, and tech and civic
    // sources are never removed, so membership in the sorted applied sources means the gateway is complete.
    private bool IsSourceApplied(EffectSource source)
    {
        EffectSource[] applied = _state.AppliedSources;
        int low = 0;
        int high = applied.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = ProgressionState.Compare(applied[middle], source);
            if (order == 0)
            {
                return true;
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool HasAccess(TerrainAccess access) => ((int)access is >= 0 and < 64) && (_state.TerrainAccessFlags & (1UL << (int)access)) != 0;

    /// <inheritdoc />
    /// <remarks>
    /// For <see cref="FeatureKind.GreatWorks"/> with a type, the answer is whether <c>great_works:&lt;type&gt;</c> is on. Without
    /// a type it is whether any type is on. For the other features the type is ignored.
    /// </remarks>
    public bool HasFeature(FeatureKind feature, GreatWorkTypeId? greatWorkType = null)
    {
        if (feature == FeatureKind.GreatWorks)
        {
            return greatWorkType is { } type ? Bits.Has(_state.GreatWorks, type.Value) : Bits.Count(_state.GreatWorks) > 0;
        }

        return ((int)feature is >= 0 and < 64) && (_state.FeatureFlags & (1UL << (int)feature)) != 0;
    }

    /// <inheritdoc />
    public int BonusPercent(Stat stat, in ModifierContext context) => _modifiers.BonusPercent(stat, in context);

    /// <inheritdoc />
    public void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions) =>
        _modifiers.ListModifiers(stat, in context, contributions);

    /// <inheritdoc />
    public long Counter(CounterKey key) => _counters.Get(key);

    /// <summary>Gets the condition evaluator, so tests can inspect its memoisation.</summary>
    internal ConditionEvaluator Evaluator => _evaluator;

    /// <inheritdoc />
    public bool Evaluate(Condition condition) => _evaluator.Evaluate(condition);
}
