using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>Implements <see cref="IProgressionQueries"/>. M1-00 declares the methods and M1-04 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class ProgressionQueries : IProgressionQueries
{
    /// <inheritdoc />
    public EraId CurrentEra => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public bool IsUnlocked(BuildingTypeId building) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public bool IsUnlocked(RecipeId recipe) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public bool IsUnlocked(TierId tier) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public bool HasAccess(TerrainAccess access) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public bool HasFeature(FeatureKind feature, GreatWorkTypeId? greatWorkType = null) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public int BonusPercent(Stat stat, in ModifierContext context) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public long Counter(CounterKey key) => throw new NotImplementedException("M1-04");

    /// <inheritdoc />
    public bool Evaluate(Condition condition) => throw new NotImplementedException("M1-04");
}
