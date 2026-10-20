namespace Evenmere.Sim;

/// <summary>Emitted when a queued command is refused, either because nothing handles it or because its handler's validation failed.</summary>
/// <param name="Tick">The tick at whose start the command was processed.</param>
/// <param name="Command">The refused command.</param>
/// <param name="Code">The reason code.</param>
/// <param name="Detail">A short developer-facing explanation; the view maps <paramref name="Code"/> to player text.</param>
public sealed record CommandRejected(long Tick, Command Command, RejectReason Code, string Detail) : SimEvent(Tick);
