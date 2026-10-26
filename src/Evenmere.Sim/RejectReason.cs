namespace Evenmere.Sim;

/// <summary>
/// Why a command was refused. Later tasks extend the set: add members at the end and never renumber, because
/// hosts and command logs may store the numeric code. The view maps each code to player text; the event's detail is
/// for developers and logs.
/// </summary>
public enum RejectReason
{
    /// <summary>No handler is registered for the command's type.</summary>
    UnknownCommand = 0,

    /// <summary>The handler found the command invalid in the current state; the event's detail says why.</summary>
    Invalid = 1,

    /// <summary>The command is declared but its handler has not been written yet (M1-00 registers a placeholder for every M1 command); the event's detail names the task that adds it.</summary>
    NotImplemented = 2,

    /// <summary>The building, house, advice note or other target the command names does not exist.</summary>
    UnknownTarget = 3,

    /// <summary>A placement was refused because the placement check found an error (occupied, wrong terrain, locked, and so on); the event's detail names the first error.</summary>
    PlacementInvalid = 4,

    /// <summary>A research or civic target is not available: already done, a prerequisite is missing, or its era has not begun.</summary>
    NodeNotAvailable = 5,

    /// <summary>The target exists but the command makes no sense for it, such as pausing a house or locking the upgrades of a farm.</summary>
    NotApplicable = 6,

    /// <summary>The target may not be changed this way, such as demolishing the Founders' Hall.</summary>
    Protected = 7,

    /// <summary>A tradition card is not unlocked, or the content does not define it. (M2-07.)</summary>
    TraditionNotUnlocked = 8,

    /// <summary>A tradition card does not fit the slot, or the slot does not exist. (M2-07.)</summary>
    SlotMismatch = 9,

    /// <summary>A tradition card is already slotted in another slot. (M2-07.)</summary>
    AlreadySlotted = 10,

    /// <summary>A style card is already slotted, and only one style may be active at a time. (M2-07.)</summary>
    StyleAlreadySlotted = 11,

    /// <summary>A festival is not unlocked, or the content does not define it. (M2-08.)</summary>
    FestivalNotUnlocked = 12,

    /// <summary>A festival cannot be held in the current season. (M2-08.)</summary>
    WrongSeason = 13,

    /// <summary>A festival is already running, and only one runs island-wide at a time. (M2-08.)</summary>
    FestivalAlreadyRunning = 14,

    /// <summary>A festival type was already held this year. (M2-08.)</summary>
    FestivalHeldThisYear = 15,

    /// <summary>The pool cannot pay a festival's discounted cost. (M2-08.)</summary>
    FestivalGoodsShort = 16,

    /// <summary>A cheat command was sent to a save whose cheats flag is off. (M2-26, ADR-0012.)</summary>
    CheatsDisabled = 17,
}
