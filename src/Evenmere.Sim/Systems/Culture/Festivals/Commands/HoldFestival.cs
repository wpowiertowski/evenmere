using Evenmere.Core.Ids;

namespace Evenmere.Sim.Systems.Culture.Festivals.Commands;

/// <summary>
/// Holds a festival at a festival ground. The goods are taken at once, the festival runs for its (modified) days,
/// and each type may be held once per year with only one festival running island-wide. Handled by M2-08.
/// </summary>
/// <remarks>
/// Refused with <see cref="RejectReason.UnknownTarget"/> when the ground is not a building,
/// <see cref="RejectReason.Invalid"/> when the festival is unknown or the ground is unfinished or not a festival
/// ground, <see cref="RejectReason.FestivalNotUnlocked"/> when the festival is locked,
/// <see cref="RejectReason.FestivalAlreadyRunning"/> when a festival is running,
/// <see cref="RejectReason.FestivalHeldThisYear"/> when the type was already held this year,
/// <see cref="RejectReason.WrongSeason"/> when the season does not match, and
/// <see cref="RejectReason.FestivalGoodsShort"/> when the pool cannot pay the discounted cost.
/// </remarks>
/// <param name="Ground">The festival ground the festival runs at.</param>
/// <param name="Festival">The festival to hold.</param>
public sealed record HoldFestival(EntityId Ground, FestivalId Festival) : Command;
