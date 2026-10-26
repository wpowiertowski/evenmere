using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Exploration.Commands;

/// <summary>
/// Sends an expedition from a Scout's Lodge to an adjacent fogged region. The lodge must be a finished building tagged
/// <c>expedition_lodge</c> (never a content ID), the target must border revealed land, and the lodge must be idle.
/// The region reveals after the duration elapses. Handled by M2-06.
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.UnknownTarget"/> when the lodge names no building, and with
/// <see cref="RejectReason.Invalid"/> when the lodge is unfinished or not a lodge, the region is already revealed or
/// unknown, the target does not border revealed land, or the lodge already has an expedition under way.
/// </remarks>
/// <param name="Lodge">The Scout's Lodge sending the expedition.</param>
/// <param name="Region">The fogged region number to reveal (map data, not a content ID).</param>
public sealed record StartExpedition(EntityId Lodge, int Region) : Command;
