namespace Evenmere.Sim;

/// <summary>
/// Why a command was refused. Later tasks extend the set: add members at the end and never renumber, because
/// hosts and command logs may store the numeric code.
/// </summary>
public enum RejectReason
{
    /// <summary>No handler is registered for the command's type.</summary>
    UnknownCommand = 0,

    /// <summary>The handler found the command invalid in the current state; the event's detail says why.</summary>
    Invalid = 1,
}
