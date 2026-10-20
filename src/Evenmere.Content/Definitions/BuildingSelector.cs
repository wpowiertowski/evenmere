using Evenmere.Core.Ids;

namespace Evenmere.Content.Definitions;

/// <summary>
/// Picks buildings by ID, tag or category. In JSON it is a single key: <c>{ "building": "bakery" }</c>,
/// <c>{ "tag": "farm" }</c> or <c>{ "category": "knowledge" }</c>. The hierarchy is closed.
/// </summary>
public abstract record BuildingSelector
{
    private protected BuildingSelector()
    {
    }
}

/// <summary>Selects one building type.</summary>
/// <param name="Building">The building type.</param>
public sealed record BuildingTypeSelector(BuildingTypeId Building) : BuildingSelector;

/// <summary>Selects every building that carries a tag.</summary>
/// <param name="Tag">The tag.</param>
public sealed record BuildingTagSelector(string Tag) : BuildingSelector;

/// <summary>Selects every building in a category.</summary>
/// <param name="Category">The category.</param>
public sealed record BuildingCategorySelector(BuildingCategory Category) : BuildingSelector;
