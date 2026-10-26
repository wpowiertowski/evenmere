using System.Collections.Immutable;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Ui.Common;
using Evenmere.Sim;
using Evenmere.Sim.Systems.Culture.Festivals.Commands;

namespace Evenmere.Game.Ui.Events;

/// <summary>One cost line of a festival as the panel shows it: the good's name and what holding needs and has.</summary>
/// <param name="GoodName">The display name of the good.</param>
/// <param name="Need">What holding costs, after discounts, in milli-units.</param>
/// <param name="Have">What the island pool holds, in milli-units.</param>
public sealed record FestivalCostFact(string GoodName, Amount Need, Amount Have);

/// <summary>One festival as the panel shows it, with the facts the block rules read.</summary>
/// <param name="Id">The festival.</param>
/// <param name="Name">The display name.</param>
/// <param name="Seasons">The seasons it can be held in, in season order; empty means any season.</param>
/// <param name="Cost">The discounted cost lines.</param>
/// <param name="Days">How many days it runs.</param>
/// <param name="CulturePerDay">The culture each day pays, in milli-units.</param>
/// <param name="Bonus">The season bonus, if any.</param>
/// <param name="EffectText">The calm sentence naming the effects while it runs.</param>
/// <param name="Unlocked">Whether the festival is unlocked.</param>
/// <param name="HeldThisYear">Whether this type was already held this year.</param>
public sealed record FestivalFacts(
    FestivalId Id,
    string Name,
    ImmutableArray<Season> Seasons,
    ImmutableArray<FestivalCostFact> Cost,
    int Days,
    Amount CulturePerDay,
    SeasonBonus? Bonus,
    string EffectText,
    bool Unlocked,
    bool HeldThisYear);

/// <summary>Where the festival panel reads its numbers. The game feeds it from the sim host; the tests feed it fixture facts.</summary>
public interface IFestivalData
{
    /// <summary>Gets a value indicating whether a festival is running island-wide.</summary>
    bool IsRunning { get; }

    /// <summary>Gets the current season, for the season rule.</summary>
    Season SeasonNow { get; }

    /// <summary>Gets the display name of the running festival, or <see langword="null"/> when none runs.</summary>
    string? RunningName { get; }

    /// <summary>Gets how many daily credits the running festival still pays.</summary>
    int RunningDaysLeft { get; }

    /// <summary>Gets the festivals in display order.</summary>
    IReadOnlyList<FestivalFacts> Festivals { get; }
}

/// <summary>Why a festival cannot be held now, in the sim's <c>HoldFestival</c> check order.</summary>
public enum FestivalBlock
{
    /// <summary>The festival can be held.</summary>
    None = 0,

    /// <summary>The festival is not unlocked.</summary>
    Locked = 1,

    /// <summary>Another festival is already running.</summary>
    AlreadyRunning = 2,

    /// <summary>This type was already held this year.</summary>
    HeldThisYear = 3,

    /// <summary>The season does not match.</summary>
    WrongSeason = 4,

    /// <summary>The pool cannot pay the discounted cost.</summary>
    GoodsShort = 5,
}

/// <summary>One festival row as the panel shows it: its costs and effects plus its status and whether it can be held.</summary>
/// <param name="Id">The festival.</param>
/// <param name="Name">The display name.</param>
/// <param name="CostText">The cost lines, joined.</param>
/// <param name="DaysText">How long it runs, such as <c>3 days</c>.</param>
/// <param name="CultureText">What each day pays, such as <c>+12 culture a day</c>, with the season bonus named.</param>
/// <param name="EffectText">The calm sentence naming the effects while it runs.</param>
/// <param name="CanHold">Whether the Hold button is enabled.</param>
/// <param name="Block">Why it cannot be held.</param>
/// <param name="StatusText">The calm sentence shown under the row, or the running line when it runs.</param>
public sealed record FestivalRow(
    FestivalId Id,
    string Name,
    string CostText,
    string DaysText,
    string CultureText,
    string EffectText,
    bool CanHold,
    FestivalBlock Block,
    string StatusText);

/// <summary>
/// The numbers behind the festival panel, as plain properties the panel binds to. It has no Godot types: it reads an
/// <see cref="IFestivalData"/>, formats through <see cref="UiStrings"/> (the <c>ui.events.*</c> locale keys), and
/// refreshes at most four times a second (<see cref="ViewModel"/>). Block reasons follow the sim's
/// <c>HoldFestival</c> rejection order, so the panel always names the reason the sim would refuse with.
/// </summary>
public sealed class FestivalPanelModel : ViewModel
{
    private readonly IFestivalData _data;
    private readonly UiStrings _strings;
    private readonly List<FestivalRow> _rows = [];

    /// <summary>Initializes a new instance of the <see cref="FestivalPanelModel"/> class.</summary>
    /// <param name="data">Where the festivals come from.</param>
    /// <param name="strings">The display text.</param>
    public FestivalPanelModel(IFestivalData data, UiStrings strings)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(strings);
        _data = data;
        _strings = strings;
    }

    /// <summary>Gets or sets the festival ground the Hold commands name. The panel sets it when it opens.</summary>
    public EntityId Ground { get; set; } = EntityId.None;

    /// <summary>Gets the festival rows, in display order.</summary>
    public IReadOnlyList<FestivalRow> Rows => _rows;

    /// <summary>Gets the running line, such as <c>Harvest Fair is running — 2 days left.</c>, or <see langword="null"/> when none runs.</summary>
    public string? RunningLine { get; private set; }

    /// <summary>Builds the <c>HoldFestival</c> command for a row, or <see langword="null"/> when the row cannot be held.</summary>
    /// <param name="id">The festival.</param>
    /// <returns>The command, or <see langword="null"/> when the row is blocked or has no ground.</returns>
    public Command? HoldCommand(FestivalId id)
    {
        if (Ground == EntityId.None)
        {
            return null;
        }

        foreach (FestivalRow row in _rows)
        {
            if (row.Id == id)
            {
                return row.CanHold ? new HoldFestival(Ground, id) : null;
            }
        }

        return null;
    }

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        _rows.Clear();
        foreach (FestivalFacts facts in _data.Festivals)
        {
            _rows.Add(BuildRow(facts, _data.IsRunning, _data.SeasonNow));
        }

        RunningLine = _data.IsRunning && _data.RunningName is { } running
            ? _strings.Format(
                "ui.events.festival.running",
                ("name", running),
                ("left", DaysText(_data.RunningDaysLeft)))
            : null;
    }

    private FestivalRow BuildRow(FestivalFacts facts, bool anyRunning, Season seasonNow)
    {
        FestivalBlock block = BlockFor(facts, anyRunning, seasonNow);
        return new FestivalRow(
            facts.Id,
            facts.Name,
            CostText(facts.Cost),
            DaysText(facts.Days),
            CultureText(facts.CulturePerDay, facts.Bonus),
            facts.EffectText,
            block == FestivalBlock.None,
            block,
            StatusText(block, facts, seasonNow));
    }

    private static FestivalBlock BlockFor(FestivalFacts facts, bool anyRunning, Season seasonNow)
    {
        if (!facts.Unlocked)
        {
            return FestivalBlock.Locked;
        }

        if (anyRunning)
        {
            return FestivalBlock.AlreadyRunning;
        }

        if (facts.HeldThisYear)
        {
            return FestivalBlock.HeldThisYear;
        }

        if (facts.Seasons.Length > 0 && !Contains(facts.Seasons, seasonNow))
        {
            return FestivalBlock.WrongSeason;
        }

        foreach (FestivalCostFact line in facts.Cost)
        {
            if (line.Have < line.Need)
            {
                return FestivalBlock.GoodsShort;
            }
        }

        return FestivalBlock.None;
    }

    private string StatusText(FestivalBlock block, FestivalFacts facts, Season seasonNow)
    {
        return block switch
        {
            FestivalBlock.None => string.Empty,
            FestivalBlock.Locked => _strings.Get("ui.events.festival.block.locked"),
            FestivalBlock.AlreadyRunning => _strings.Get("ui.events.festival.block.running"),
            FestivalBlock.HeldThisYear => _strings.Get("ui.events.festival.block.held"),
            FestivalBlock.WrongSeason => _strings.Format(
                "ui.events.festival.block.season",
                ("season", SeasonName(FirstSeason(facts.Seasons, seasonNow)))),
            FestivalBlock.GoodsShort => ShortText(facts.Cost),
            _ => string.Empty,
        };
    }

    private string ShortText(ImmutableArray<FestivalCostFact> cost)
    {
        var parts = new List<string>();
        foreach (FestivalCostFact line in cost)
        {
            if (line.Have < line.Need)
            {
                parts.Add(_strings.Format(
                    "ui.events.festival.block.goods",
                    ("amount", NumberFormat.Units(line.Need - line.Have)),
                    ("good", line.GoodName)));
            }
        }

        return string.Join(" ", parts);
    }

    private string CostText(ImmutableArray<FestivalCostFact> cost)
    {
        var parts = new List<string>(cost.Length);
        foreach (FestivalCostFact line in cost)
        {
            parts.Add(_strings.Format(
                "ui.events.festival.cost.line",
                ("amount", NumberFormat.Units(line.Need)),
                ("good", line.GoodName)));
        }

        return string.Join(", ", parts);
    }

    private string DaysText(int days) =>
        days == 1
            ? _strings.Get("ui.events.festival.days.one")
            : _strings.Format("ui.events.festival.days", ("count", NumberFormat.Count(days)));

    private string CultureText(Amount culturePerDay, SeasonBonus? bonus)
    {
        string text = _strings.Format(
            "ui.events.festival.culture",
            ("amount", NumberFormat.Units(culturePerDay)));
        if (bonus is { } extra)
        {
            text += " · " + _strings.Format(
                "ui.events.festival.bonus",
                ("percent", NumberFormat.Count(extra.Percent)),
                ("season", SeasonName(extra.Season)));
        }

        return text;
    }

    private string SeasonName(Season season) =>
        _strings.Get($"ui.hud.season.{season.ToString().ToLowerInvariant()}");

    private static Season FirstSeason(ImmutableArray<Season> seasons, Season fallback) =>
        seasons.Length > 0 ? seasons[0] : fallback;

    private static bool Contains(ImmutableArray<Season> seasons, Season season)
    {
        foreach (Season candidate in seasons)
        {
            if (candidate == season)
            {
                return true;
            }
        }

        return false;
    }
}
