namespace Evenmere.Sim;

/// <summary>
/// The base type of everything the player or the host asks the simulation to do, such as placing a building or
/// choosing a research target. Commands are immutable records, are queued with <see cref="ISimulation.Enqueue"/>
/// and are routed to the handler registered for their exact type.
/// </summary>
public abstract record Command;
