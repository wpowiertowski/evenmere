namespace Evenmere.Sim.Persistence;

/// <summary>
/// The content kinds whose integer IDs a save may hold, either as typed IDs (<c>GoodId</c> and the like) or as raw
/// numbers inside another member. Content IDs are assigned in ordinal string order (ADR-0004), so adding an item shifts
/// every later ID of that kind; the load-time remap permutes or rewrites every member marked with
/// <see cref="ContentIndexedAttribute"/> or <see cref="ContentValuedAttribute"/> using the save's string tables.
/// </summary>
public enum ContentKind
{
    /// <summary>A good (<c>GoodId</c>).</summary>
    Good = 0,

    /// <summary>A building type (<c>BuildingTypeId</c>).</summary>
    Building = 1,

    /// <summary>A production recipe (<c>RecipeId</c>).</summary>
    Recipe = 2,

    /// <summary>A population tier (<c>TierId</c>).</summary>
    Tier = 3,

    /// <summary>A technology (<c>TechId</c>).</summary>
    Tech = 4,

    /// <summary>A civic (<c>CivicId</c>).</summary>
    Civic = 5,

    /// <summary>A tradition (<c>TraditionId</c>).</summary>
    Tradition = 6,

    /// <summary>A festival (<c>FestivalId</c>).</summary>
    Festival = 7,

    /// <summary>A great-work type (<c>GreatWorkTypeId</c>).</summary>
    GreatWork = 8,

    /// <summary>A monument or wonder (<c>MonumentId</c>).</summary>
    Monument = 9,

    /// <summary>A happening (<c>HappeningId</c>).</summary>
    Happening = 10,

    /// <summary>An era (<c>EraId</c>).</summary>
    Era = 11,

    /// <summary>A terrain (<c>TerrainId</c>).</summary>
    Terrain = 12,
}
