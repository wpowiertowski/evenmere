namespace Evenmere.Sim;

/// <summary>
/// Identifies one of the simulation's systems and services. The value is the system's index in
/// <c>WorldState.SystemRngs</c>, so the numbering is part of the save format: add members at the end and never
/// renumber. It is not the tick order, which <c>SystemRoster</c> defines.
/// </summary>
public enum SystemId
{
    /// <summary>The map service: tiles, terrain and the building registry.</summary>
    Map = 0,

    /// <summary>The roads service: road layer, connectivity and distance fields.</summary>
    Roads = 1,

    /// <summary>The storage service: the island-wide goods pool.</summary>
    Storage = 2,

    /// <summary>The modifiers service: the effective-stat index.</summary>
    Modifiers = 3,

    /// <summary>The construction system.</summary>
    Construction = 4,

    /// <summary>The workforce system.</summary>
    Workforce = 5,

    /// <summary>The production system.</summary>
    Production = 6,

    /// <summary>The logistics system.</summary>
    Logistics = 7,

    /// <summary>The exploration system.</summary>
    Exploration = 8,

    /// <summary>The needs system.</summary>
    Needs = 9,

    /// <summary>The population system.</summary>
    Population = 10,

    /// <summary>The coin economy system.</summary>
    Economy = 11,

    /// <summary>The research system.</summary>
    Research = 12,

    /// <summary>The culture system.</summary>
    Culture = 13,

    /// <summary>The progression system.</summary>
    Progression = 14,

    /// <summary>The happenings system.</summary>
    Happenings = 15,

    /// <summary>The advisors system.</summary>
    Advisors = 16,

    /// <summary>The statistics system.</summary>
    Statistics = 17,
}
