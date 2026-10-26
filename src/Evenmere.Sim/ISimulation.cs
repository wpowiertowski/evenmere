using Evenmere.Sim.Queries;

namespace Evenmere.Sim;

/// <summary>
/// The only surface the game and the tools use to drive the simulation (frozen in M0; changed only by ADR).
/// The host owns the loop: it decides how many ticks run per frame (ADR-0006) and calls everything here from
/// one thread.
/// </summary>
public interface ISimulation
{
    /// <summary>Gets the number of ticks that have completed since the world began.</summary>
    long CurrentTick { get; }

    /// <summary>Queues a command. It is validated and applied at the start of the next <see cref="Tick"/>, in enqueue order.</summary>
    /// <param name="command">The command to queue.</param>
    void Enqueue(Command command);

    /// <summary>Advances the simulation by exactly one tick.</summary>
    void Tick();

    /// <summary>Returns the events emitted since the last drain, in emission order, and clears them.</summary>
    /// <returns>The events; empty when nothing happened.</returns>
    IReadOnlyList<SimEvent> DrainEvents();

    /// <summary>Gets the read-only queries. They are valid only between ticks (ADR-0006).</summary>
    ISimQueries Query { get; }

    /// <summary>
    /// Gets or sets whether cheat commands are enabled for this save. It is not simulation state and never reaches
    /// the state hash: the host sets it at new-game time (debug builds, or with the <c>--cheats</c> launch flag)
    /// and save/load carries it in the save header, so replays of cheat-using sessions enable it the same way (ADR-0012).
    /// </summary>
    bool CheatsUsed { get; set; }

    /// <summary>Computes the XxHash64 of the canonical serialisation of the world state (ADR-0003).</summary>
    /// <returns>The state hash.</returns>
    ulong ComputeStateHash();
}
