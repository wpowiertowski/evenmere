using System.Collections.Immutable;
using Evenmere.Content.Effects;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Services;

/// <summary>
/// The write side of progression: applying the effects of a researched tech, an adopted civic and the like. Implemented by
/// the progression system (M1-04). It unlocks buildings, recipes and tiers, opens ground, switches on features, adds
/// modifiers to the index, and carries out the one-off grants (goods into the pool, points into the ledger).
/// </summary>
internal interface IProgressionService
{
    /// <summary>
    /// Applies a list of effects on behalf of a source. Applying the same source again changes nothing, so a caller need
    /// not remember whether it already did.
    /// </summary>
    /// <param name="source">What the effects come from, such as the tech that was just researched.</param>
    /// <param name="effects">The effects, as the content defines them.</param>
    void ApplyEffects(EffectSource source, ImmutableArray<Effect> effects);

    /// <summary>
    /// Removes every active modifier of a source and the record that its effects were applied, so the source can be
    /// applied again. Unlocks never go down: tradition, festival and monument sources only ever contributed modifiers
    /// and recorded flags, so removal is exact. (M2-07.)
    /// </summary>
    /// <param name="source">The source whose effects are undone.</param>
    void RemoveSource(EffectSource source);
}
