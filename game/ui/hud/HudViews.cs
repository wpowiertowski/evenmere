using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Hud;

/// <summary>One labelled number in the top bar: a tier's residents, or knowledge or culture per day.</summary>
/// <param name="Label">The name shown beside the number, such as <c>Villagers</c>.</param>
/// <param name="Value">The formatted number, or the dash when no query provides it yet.</param>
/// <param name="Tooltip">The sentence shown when the player points at it.</param>
public readonly record struct HudStat(string Label, string Value, string Tooltip);

/// <summary>One good in the strip under the top bar.</summary>
/// <param name="Name">The good's display name.</param>
/// <param name="Stock">The formatted stock in whole units.</param>
/// <param name="Trend">Whether the stock rose, fell or held over the last game day.</param>
/// <param name="Tooltip">The sentence shown when the player points at it; it spells out the trend, which the arrow shows only by shape and colour.</param>
public readonly record struct HudGood(string Name, string Stock, Trend Trend, string Tooltip);
