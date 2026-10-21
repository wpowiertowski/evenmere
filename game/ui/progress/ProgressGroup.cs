namespace Evenmere.Game.Ui.Progress;

/// <summary>
/// Which state group a research or civic node is shown in. The declaration order is the display order:
/// the current focus first, then the queue in order, then what can be picked, what is still locked,
/// and finally what is done (collapsed by default).
/// </summary>
public enum ProgressGroup
{
    /// <summary>The node receiving today's points.</summary>
    Target = 0,

    /// <summary>A queued node, in queue order.</summary>
    Queue = 1,

    /// <summary>A node the player can pick now.</summary>
    Available = 2,

    /// <summary>A node whose prerequisites, era or inspiration are still ahead of it.</summary>
    Locked = 3,

    /// <summary>An already researched tech or adopted civic.</summary>
    Researched = 4,
}
