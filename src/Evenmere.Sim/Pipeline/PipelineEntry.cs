namespace Evenmere.Sim.Pipeline;

/// <summary>A system together with its identity and cadence, as the pipeline holds it.</summary>
/// <param name="Id">Which system this is; it indexes the system's random stream.</param>
/// <param name="System">The system.</param>
/// <param name="Cadence">How often the pipeline runs it.</param>
internal sealed record PipelineEntry(SystemId Id, ISimSystem System, Cadence Cadence);
