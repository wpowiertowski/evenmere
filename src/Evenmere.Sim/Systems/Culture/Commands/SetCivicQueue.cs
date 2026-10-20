using System.Diagnostics.CodeAnalysis;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Culture.Commands;

/// <summary>
/// Replaces the civic queue, the civics to adopt after the target, in order. An empty queue clears it. Refused with
/// <see cref="RejectReason.NodeNotAvailable"/> if a civic in it cannot be reached. Handled by M1-11.
/// </summary>
/// <remarks>The array is part of the command, so the sender must not change it after enqueueing the command. Two commands with equal queues are equal.</remarks>
/// <param name="Queue">The civics in the order to adopt them.</param>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The command is not a collection; its name is the type name that scenario files and command logs use (M1-00).")]
public sealed record SetCivicQueue(CivicId[] Queue) : Command
{
    /// <inheritdoc />
    public bool Equals(SetCivicQueue? other) => other is not null && ValueArrays.Equal(Queue, other.Queue);

    /// <inheritdoc />
    public override int GetHashCode() => ValueArrays.Hash(Queue);
}
