using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Systems.Logistics;

/// <summary>
/// The logistics system's slice of the world state: the jobs under way, the busy carts per storehouse and the days
/// from which failed delivery reservations may be retried. Every member added later gets an explicit integer
/// <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class LogisticsState
{
    /// <summary>Gets or sets the jobs under way, keyed by the job's entity ID from the world allocator.</summary>
    // The formatter is named outright because MessagePack's source generator cannot find one for a generic collection from another
    // assembly and emits code that does not compile; at run time SimResolver would pick the same formatter anyway.
    [Key(0)]
    [MessagePackFormatter(typeof(IdTableFormatter<TransportJobRecord>))]
    public IdTable<TransportJobRecord> Jobs { get; set; } = new();

    /// <summary>
    /// Gets or sets how many of each storehouse's carts are out on jobs, keyed by the storehouse's entity ID. Entries for
    /// storehouses with no job in flight are dropped, so absence means zero.
    /// </summary>
    [Key(1)]
    [MessagePackFormatter(typeof(IdTableFormatter<int>))]
    public IdTable<int> BusyCarts { get; set; } = new();

    /// <summary>
    /// Gets or sets the delivery requests whose pool reservation failed, as packed (building, good, direction) keys in
    /// ascending order: <c>((long)building « 32) | ((long)good « 1) | direction</c>.
    /// </summary>
    [Key(2)]
    [ContentValued(ContentKind.Good)]
    public long[] RetryKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets the day from which each <see cref="RetryKeys"/> entry may be served again, parallel to
    /// <see cref="RetryKeys"/>: the day after the reservation failed, so a shortage does not retry every tick.
    /// </summary>
    [Key(3)]
    [NotContentIndexed("parallel to RetryKeys; the remap permutes them together")]
    public long[] RetryDays { get; set; } = [];
}
