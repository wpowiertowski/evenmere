namespace Evenmere.Sim;

/// <summary>
/// The base type of everything the simulation reports to the presentation layer, such as a finished construction
/// or an advanced tier. Events are immutable records, are emitted in system order and then in entity order
/// (ADR-0003), and are never part of the state hash.
/// </summary>
/// <param name="Tick">The tick during which the event was emitted.</param>
public abstract record SimEvent(long Tick);
