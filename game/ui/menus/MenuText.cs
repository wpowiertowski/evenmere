using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Game.Host.Persistence;
using Evenmere.Game.Ui.Common;

namespace Evenmere.Game.Ui.Menus;

/// <summary>
/// The shared text of the main and pause menus: suggested save names, slot rows and play times. Everything comes
/// from locale keys through <see cref="UiStrings"/>; numbers are already formatted by the callers' helpers.
/// </summary>
public static class MenuText
{
    /// <summary>How many in-game days pass between autosaves.</summary>
    public const int AutosaveDays = 10;

    private static readonly string[] _seasonKeys =
    [
        "ui.hud.season.spring",
        "ui.hud.season.summer",
        "ui.hud.season.autumn",
        "ui.hud.season.winter",
    ];

    /// <summary>Suggests a save name from the island and the date, for the name field's prefill.</summary>
    /// <param name="host">The running simulation host.</param>
    /// <param name="strings">The display text.</param>
    /// <returns>Something like <c>starter — Day 3, Spring, Year 1</c>.</returns>
    public static string SuggestName(SimHost host, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(strings);

        var clock = new GameClock(host.CurrentTick);
        return strings.Format(
            "ui.slot.suggest",
            ("island", host.MapId),
            ("day", NumberFormat.Count(clock.DayOfSeason + 1L)),
            ("season", SeasonName(strings, (int)clock.Season)),
            ("year", NumberFormat.Count(clock.Year + 1)));
    }

    /// <summary>Formats one slot row: the name on the first line, the date, era and play time on the second.</summary>
    /// <param name="strings">The display text.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The two-line row text.</returns>
    public static string SlotDetail(UiStrings strings, SaveSlotInfo slot)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(slot);

        if (slot.Tick < 0)
        {
            return strings.Format("ui.slot.detail.unknown", ("name", slot.Name));
        }

        long dayOfSeason = slot.Day % GameTime.DaysPerSeason;
        string date = strings.Format(
            "ui.hud.date",
            ("day", NumberFormat.Count(dayOfSeason + 1L)),
            ("season", SeasonName(strings, slot.Season)),
            ("year", NumberFormat.Count((slot.Day / GameTime.DaysPerYear) + 1)));
        string era = strings.Get($"era.{slot.Era}.name");
        return strings.Format(
            "ui.slot.detail",
            ("name", slot.Name),
            ("date", date),
            ("era", era),
            ("play", PlayTime(slot.Tick, strings)));
    }

    /// <summary>Formats a tick count as a game-time duration, such as <c>2d 4h 12m</c>.</summary>
    /// <param name="tick">The tick count; negative means unknown.</param>
    /// <param name="strings">The display text.</param>
    /// <returns>The duration text.</returns>
    public static string PlayTime(long tick, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        if (tick < 0)
        {
            return strings.Get("ui.slot.time.none");
        }

        // One tick is a quarter of a game second (FixedStepper.TickMicroseconds), so four ticks make a game second.
        long totalMinutes = tick / 240;
        long days = totalMinutes / 1440;
        long hours = (totalMinutes % 1440) / 60;
        long minutes = totalMinutes % 60;

        var parts = new List<string>(3);
        if (days > 0)
        {
            parts.Add(strings.Format("ui.slot.time.day", ("count", NumberFormat.Count(days))));
        }

        if (hours > 0)
        {
            parts.Add(strings.Format("ui.slot.time.hour", ("count", NumberFormat.Count(hours))));
        }

        if (minutes > 0 || parts.Count == 0)
        {
            parts.Add(parts.Count == 0 && minutes == 0
                ? strings.Get("ui.slot.time.none")
                : strings.Format("ui.slot.time.minute", ("count", NumberFormat.Count(minutes))));
        }

        return string.Join(" ", parts);
    }

    /// <summary>Gets the display name of a season index (0 spring to 3 winter).</summary>
    /// <param name="strings">The display text.</param>
    /// <param name="season">The season index.</param>
    /// <returns>The season name.</returns>
    public static string SeasonName(UiStrings strings, int season)
    {
        ArgumentNullException.ThrowIfNull(strings);
        return season is >= 0 and < 4 ? strings.Get(_seasonKeys[season]) : strings.Get("ui.hud.unavailable");
    }
}
