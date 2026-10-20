using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Modifiers;

/// <summary>
/// The modifiers service: effective-stat index. A shared service rather than a ticked system, so its cadence is
/// <see cref="Cadence.None"/> and <see cref="Run"/> is never called. It has no commands: modifiers arrive through
/// <c>IProgressionService.ApplyEffects</c>, which the progression system implements, and are read through
/// <c>IProgressionQueries.BonusPercent</c> and <c>ListModifiers</c>.
/// </summary>
internal sealed class ModifiersSystem : ISimSystem
{
    private readonly ModifiersState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModifiersSystem"/> class over a state, and builds the index from the
    /// modifiers the state holds. This is how a loaded save gets its index back.
    /// </summary>
    /// <param name="state">The modifiers slice of the world state.</param>
    /// <param name="content">The loaded content.</param>
    /// <exception cref="InvalidDataException">The state holds a modifier the content cannot explain.</exception>
    public ModifiersSystem(ModifiersState state, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);

        _state = state;

        // MessagePack writes nil for a missing member, so a damaged save can leave the list null.
        _state.Active ??= [];
        Index = new ModifierIndex(content);
        foreach (ActiveModifier modifier in _state.Active)
        {
            Index.Add(modifier);
        }
    }

    /// <inheritdoc />
    public string Name => SystemNames.Modifiers;

    /// <summary>Gets the read-only queries other systems and the view use.</summary>
    public IModifiersQueries Queries { get; } = new ModifiersQueries();

    /// <summary>Gets the effective-stat index.</summary>
    public ModifierIndex Index { get; }

    /// <summary>Gets the number of registered modifiers.</summary>
    public int Count => _state.Active.Length;

    /// <inheritdoc />
    public void Register(CommandRouter router)
    {
        // The modifiers service has no commands; effects reach it through the progression service.
    }

    /// <inheritdoc />
    public void Run(SimContext ctx)
    {
        // A service is never ticked (Cadence.None); it changes only through the calls the progression service makes into it.
    }

    /// <summary>Registers the modifier an effect describes, on behalf of a source, in the state and in the index.</summary>
    /// <param name="source">What the effect comes from.</param>
    /// <param name="effect">The modifier effect.</param>
    public void Add(EffectSource source, ModifierEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        ActiveModifier record = Describe(source, effect);

        // The index is fed the saved form, so a modifier behaves the same whether it was just applied or loaded.
        Index.Add(record);

        var grown = new ActiveModifier[_state.Active.Length + 1];
        Array.Copy(_state.Active, grown, _state.Active.Length);
        grown[^1] = record;
        _state.Active = grown;
    }

    /// <summary>Connects the index to the season and the condition evaluator, which need the simulation context.</summary>
    /// <param name="environment">The environment.</param>
    internal void Attach(IModifierEnvironment environment) => Index.Attach(environment);

    private static ActiveModifier Describe(EffectSource source, ModifierEffect effect)
    {
        var record = new ActiveModifier
        {
            Source = source,
            Stat = effect.Stat,
            Percent = effect.Percent,
            When = effect.When is null ? null : ConditionCodec.Encode(effect.When),
        };

        switch (effect.Target)
        {
            case IslandTarget:
                record.TargetKind = ModifierTargetKind.Island;
                break;
            case BuildingsTarget { Selector: BuildingTypeSelector byType }:
                record.TargetKind = ModifierTargetKind.Building;
                record.TargetValue = byType.Building.Value;
                break;
            case BuildingsTarget { Selector: BuildingTagSelector byTag }:
                record.TargetKind = ModifierTargetKind.Tag;
                record.TargetTag = byTag.Tag;
                break;
            case BuildingsTarget { Selector: BuildingCategorySelector byCategory }:
                record.TargetKind = ModifierTargetKind.Category;
                record.TargetValue = (int)byCategory.Category;
                break;
            case TierTarget tier:
                record.TargetKind = ModifierTargetKind.Tier;
                record.TargetValue = tier.Tier.Value;
                break;
            case GoodTarget good:
                record.TargetKind = ModifierTargetKind.Good;
                record.TargetValue = good.Good.Value;
                break;
            case DiscoveryTarget discovery:
                record.TargetKind = ModifierTargetKind.Discovery;
                record.TargetValue = (int)discovery.Kind;
                break;
            default:
                throw new ArgumentException($"{effect.Target.GetType().Name} is not a known modifier target.", nameof(effect));
        }

        return record;
    }
}
