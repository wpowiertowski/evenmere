using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Queries;

/// <summary>
/// Read-only queries over the unlocks, the effects that modifiers apply, the era and the milestone counters.
/// Implemented by the progression system (M1-04), which also owns the modifier index.
/// </summary>
public interface IProgressionQueries
{
    /// <summary>Tests whether a building type can be built: it is available from the start or an effect has unlocked it.</summary>
    /// <param name="building">The building type.</param>
    /// <returns><see langword="true"/> if it is unlocked.</returns>
    bool IsUnlocked(BuildingTypeId building);

    /// <summary>Tests whether a recipe can be used.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns><see langword="true"/> if it is unlocked.</returns>
    bool IsUnlocked(RecipeId recipe);

    /// <summary>Tests whether a population tier is available to houses.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns><see langword="true"/> if it is unlocked.</returns>
    bool IsUnlocked(TierId tier);

    /// <summary>Tests whether research has opened a kind of ground.</summary>
    /// <param name="access">The access flag.</param>
    /// <returns><see langword="true"/> if it is open.</returns>
    bool HasAccess(TerrainAccess access);

    /// <summary>Tests whether a game feature has been switched on.</summary>
    /// <param name="feature">The feature.</param>
    /// <param name="greatWorkType">For <see cref="FeatureKind.GreatWorks"/>, the type of great work; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the feature is on.</returns>
    bool HasFeature(FeatureKind feature, GreatWorkTypeId? greatWorkType = null);

    /// <summary>
    /// Gets the era the island has reached. <c>EraId.Value</c> is the era's index in string order, not its number, so to
    /// compare with an item's era (<c>BuildingDef.Era</c>, <c>NodeDef.Era</c>) read <c>ContentDb.Get(era).Order</c>.
    /// </summary>
    EraId CurrentEra { get; }

    /// <summary>
    /// Sums the active modifiers of a stat that match a context. Modifiers on the same stat add before they are applied once,
    /// so two +20% modifiers give 40.
    /// </summary>
    /// <param name="stat">The stat.</param>
    /// <param name="context">What the stat is being asked about.</param>
    /// <returns>The total in percent (+25 means ×1.25); negative for a net penalty. Season conditions are evaluated against the current clock.</returns>
    int BonusPercent(Stat stat, in ModifierContext context);

    /// <summary>Lists the modifiers behind <see cref="BonusPercent"/> for the explain queries.</summary>
    /// <param name="stat">The stat.</param>
    /// <param name="context">What the stat is being asked about.</param>
    /// <param name="contributions">Receives, after the entries it already holds, every modifier that matches the context, active or not, in ascending source order (kind, then value).</param>
    void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions);

    /// <summary>Reads a milestone counter.</summary>
    /// <param name="key">The counter.</param>
    /// <returns>The cumulative count; 0 if nothing has been counted. Amounts are in milli-units.</returns>
    long Counter(CounterKey key);

    /// <summary>
    /// Evaluates a content condition against the world as it is now, with the same evaluator the inspirations use.
    /// Condition kinds that the slice does not implement evaluate to <see langword="false"/>.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns><see langword="true"/> if it holds.</returns>
    bool Evaluate(Condition condition);
}
