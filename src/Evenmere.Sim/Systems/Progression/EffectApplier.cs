using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;
using Evenmere.Sim.Systems.Modifiers;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// Applies the effects of a researched tech, an adopted civic and the like: it unlocks content, opens ground, switches on
/// features, registers modifiers and carries out the one-off grants. Every source is applied once; applying it again does
/// nothing, so a caller never needs to remember whether it already did.
/// </summary>
/// <remarks>
/// The applier is the one progression service that needs the simulation context (for the goods pool), so it is also the one
/// <see cref="SimContext"/> binds, and it passes the context on to the rest of the progression system.
/// </remarks>
internal sealed class EffectApplier : IProgressionService, IBindsContext
{
    private readonly ProgressionState _state;
    private readonly ModifiersSystem _modifiers;
    private readonly PointsLedger _ledger;
    private readonly SimContextHolder _holder;

    /// <summary>Initializes a new instance of the <see cref="EffectApplier"/> class.</summary>
    /// <param name="state">The progression state, already fitted to the content.</param>
    /// <param name="modifiers">The modifiers service, which takes the modifier effects.</param>
    /// <param name="ledger">The points ledger, which takes the point grants.</param>
    /// <param name="holder">Receives the simulation context when the services are bound.</param>
    public EffectApplier(ProgressionState state, ModifiersSystem modifiers, PointsLedger ledger, SimContextHolder holder)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modifiers);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(holder);

        _state = state;
        _modifiers = modifiers;
        _ledger = ledger;
        _holder = holder;
    }

    /// <inheritdoc />
    public void Bind(SimContext context) => _holder.Bind(context);

    /// <inheritdoc />
    public void ApplyEffects(EffectSource source, ImmutableArray<Effect> effects)
    {
        if (effects.IsDefault)
        {
            throw new ArgumentException("The effects must be an array, even if it is empty.", nameof(effects));
        }

        // Recording the source first means a retry after a failure part-way never grants the earlier effects twice.
        if (!TryRecord(source))
        {
            return;
        }

        foreach (Effect effect in effects)
        {
            Apply(source, effect);
        }
    }

    /// <inheritdoc />
    void IProgressionService.RemoveSource(EffectSource source)
    {
        EffectSource[] applied = _state.AppliedSources;
        int at = Array.BinarySearch(applied, source, Comparer<EffectSource>.Create(ProgressionState.Compare));
        if (at >= 0)
        {
            var shrunk = new EffectSource[applied.Length - 1];
            Array.Copy(applied, shrunk, at);
            Array.Copy(applied, at + 1, shrunk, at, shrunk.Length - at);
            _state.AppliedSources = shrunk;
        }

        _modifiers.RemoveSource(source);
    }

    private bool TryRecord(EffectSource source)
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
                return false;
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

        // Insert at the sorted position, so the array (and the bytes it saves as) never depends on the order of application.
        var grown = new EffectSource[applied.Length + 1];
        Array.Copy(applied, grown, low);
        grown[low] = source;
        Array.Copy(applied, low, grown, low + 1, applied.Length - low);
        _state.AppliedSources = grown;
        return true;
    }

    private void Apply(EffectSource source, Effect effect)
    {
        switch (effect)
        {
            case UnlockBuildingEffect e:
                Bits.Set(_state.Buildings, e.Building.Value);
                break;
            case UnlockRecipeEffect e:
                Bits.Set(_state.Recipes, e.Recipe.Value);
                break;
            case UnlockTierEffect e:
                Bits.Set(_state.Tiers, e.Tier.Value);
                break;
            case UnlockTraditionEffect e:
                Bits.Set(_state.Traditions, e.Tradition.Value);
                break;
            case UnlockFestivalEffect e:
                Bits.Set(_state.Festivals, e.Festival.Value);
                break;
            case UnlockTerrainAccessEffect e:
                _state.TerrainAccessFlags |= 1UL << (int)e.Access;
                break;
            case UnlockFeatureEffect e:
                UnlockFeature(e);
                break;
            case TraditionSlotEffect e:
                // Recorded for the culture system; slots have no behaviour until M2-07.
                _state.TraditionSlots[(int)e.Slot]++;
                break;
            case ModifierEffect e:
                _modifiers.Add(source, e);
                break;
            case GrantGoodsEffect e:
                foreach (GoodAmount grant in e.Goods)
                {
                    _holder.Require().Storage.Add(grant.Good, grant.Amount);
                }

                break;
            case GrantPointsEffect e:
                foreach (PointAmount grant in e.Points)
                {
                    _ledger.Credit(grant.Kind, grant.Amount);
                }

                break;
            case RevealCoastlineEffect:
                // Exploration consumes this flag on its next run and reveals the coastal fog (M2-06).
                _state.CoastlineRevealed = true;
                break;
            case RequestFestivalEffect:
                // TODO(M2-12): the happenings system turns a festival request into an offer with the discount.
                break;
            default:
                throw new NotSupportedException($"{effect.GetType().Name} is not a known effect kind.");
        }
    }

    private void UnlockFeature(UnlockFeatureEffect effect)
    {
        if (effect.Feature != FeatureKind.GreatWorks)
        {
            _state.FeatureFlags |= 1UL << (int)effect.Feature;
        }
        else if (effect.GreatWorkType is { } type)
        {
            Bits.Set(_state.GreatWorks, type.Value);
        }
        else
        {
            throw new ArgumentException("The great_works feature needs the type of great work.", nameof(effect));
        }
    }
}
