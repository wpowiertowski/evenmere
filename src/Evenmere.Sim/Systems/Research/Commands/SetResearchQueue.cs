using System.Diagnostics.CodeAnalysis;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Research.Commands;

/// <summary>
/// Replaces the research queue, the techs to research after the target, in order. An empty queue clears it. Refused with
/// <see cref="RejectReason.NodeNotAvailable"/> if a tech in it cannot be reached (done, or a prerequisite that is neither
/// done nor earlier in the queue nor the target). Handled by M1-11.
/// </summary>
/// <remarks>The array is part of the command, so the sender must not change it after enqueueing the command. Two commands with equal queues are equal.</remarks>
/// <param name="Queue">The techs in the order to research them.</param>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The command is not a collection; its name is the type name that scenario files and command logs use (M1-00).")]
public sealed record SetResearchQueue(TechId[] Queue) : Command
{
    /// <inheritdoc />
    public bool Equals(SetResearchQueue? other) => other is not null && ValueArrays.Equal(Queue, other.Queue);

    /// <inheritdoc />
    public override int GetHashCode() => ValueArrays.Hash(Queue);
}
