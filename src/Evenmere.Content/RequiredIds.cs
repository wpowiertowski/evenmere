using Evenmere.Core.Ids;

namespace Evenmere.Content;

/// <summary>
/// The IDs the engine cannot start without. This is the one place systems may name specific content
/// (ADR-0004); the loader reports an EVL002 diagnostic if any of them is not defined.
/// </summary>
/// <param name="FoundersHall">The pre-placed, unique starting hall.</param>
/// <param name="SettlerHouse">The house that upgrades in place through every tier.</param>
/// <param name="DirtRoad">The road available from the start.</param>
/// <param name="Storehouse">The basic storehouse.</param>
/// <param name="Settlers">The lowest population tier.</param>
public sealed record RequiredIds(
    BuildingTypeId FoundersHall,
    BuildingTypeId SettlerHouse,
    BuildingTypeId DirtRoad,
    BuildingTypeId Storehouse,
    TierId Settlers)
{
    /// <summary>The string ID of the founders' hall building.</summary>
    public const string FoundersHallName = "founders_hall";

    /// <summary>The string ID of the settler house building.</summary>
    public const string SettlerHouseName = "settler_house";

    /// <summary>The string ID of the dirt road building.</summary>
    public const string DirtRoadName = "dirt_road";

    /// <summary>The string ID of the storehouse building.</summary>
    public const string StorehouseName = "storehouse";

    /// <summary>The string ID of the settlers tier.</summary>
    public const string SettlersName = "settlers";
}
