namespace Evenmere.Sim.Queries.Types;

/// <summary>
/// Why a building or construction site is not making progress (architecture.md, Production chains). Production sets the
/// reasons in the priority order <see cref="Paused"/>, <see cref="NoStorehouseReach"/>, <see cref="NoWorkforce"/>,
/// <see cref="MissingInput"/>, <see cref="OutputFull"/>; Construction uses <see cref="NoStorehouseReach"/> and
/// <see cref="MissingMaterial"/>. The values are part of the save format; add members at the end.
/// </summary>
public enum StallReason : byte
{
    /// <summary>Nothing is stalling the building: it is working.</summary>
    None = 0,

    /// <summary>The player has paused the building.</summary>
    Paused = 1,

    /// <summary>No storehouse's reach covers the building, so it cannot join the island's goods pool.</summary>
    NoStorehouseReach = 2,

    /// <summary>The building has no workers: its workforce fill is zero.</summary>
    NoWorkforce = 3,

    /// <summary>The input buffer lacks the goods for the next cycle.</summary>
    MissingInput = 4,

    /// <summary>The output buffer is full and no cart has collected the goods.</summary>
    OutputFull = 5,

    /// <summary>The building's upkeep cannot be paid (from Era II, when coin arrives).</summary>
    Underfunded = 6,

    /// <summary>A construction site is waiting for materials its next build step needs.</summary>
    MissingMaterial = 7,
}
