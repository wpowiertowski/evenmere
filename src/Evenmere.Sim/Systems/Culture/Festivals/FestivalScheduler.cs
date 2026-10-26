using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Culture.Festivals;

/// <summary>
/// The pure festival rules both the command handler and the daily step share: the discounted cost, the daily culture,
/// the modified duration and the once-per-year bookkeeping. Everything is integer maths; percentages convert through
/// <see cref="Ratio.FromBonusPercent"/> and never by subtracting from 1000.
/// </summary>
internal static class FestivalScheduler
{
    /// <summary>Gets the building tag that marks a building as a ground a festival can be held at.</summary>
    internal const string GroundTag = "festival_ground";

    /// <summary>
    /// Gets what holding a festival costs after the <c>festivalCost</c> modifiers (for example the Harvest Festival
    /// tradition or a happening's festival request). Each good is discounted on its own: the island-wide modifiers
    /// match every good's context and a good-targeted one matches only its good. Amounts round down, as the
    /// construction bills do, and a full discount pays nothing.
    /// </summary>
    /// <param name="festival">The festival's definition.</param>
    /// <param name="progression">The progression queries that answer the <c>festivalCost</c> stat.</param>
    /// <returns>The cost to take from the pool.</returns>
    internal static CostSpec DiscountedCost(FestivalDef festival, IProgressionQueries progression)
    {
        ArgumentNullException.ThrowIfNull(festival);
        ArgumentNullException.ThrowIfNull(progression);

        if (festival.Cost.Goods.IsDefaultOrEmpty)
        {
            return festival.Cost;
        }

        var goods = new GoodAmount[festival.Cost.Goods.Length];
        for (int i = 0; i < goods.Length; i++)
        {
            GoodAmount line = festival.Cost.Goods[i];
            int discount = progression.BonusPercent(Stat.FestivalCost, ModifierContext.ForGood(line.Good));
            goods[i] = new GoodAmount(line.Good, line.Amount.MultiplyBy(Ratio.FromBonusPercent(discount)));
        }

        return new CostSpec([.. goods], festival.Cost.Coin);
    }

    /// <summary>
    /// Gets the culture one daily step credits for a running festival: <c>culturePerDay</c> times the season bonus
    /// (for example the Harvest Fair's autumn ×1.5), or the flat amount outside the bonus season.
    /// </summary>
    /// <param name="festival">The festival's definition.</param>
    /// <param name="season">The season the credit lands in.</param>
    /// <returns>The culture to credit, in milli-units.</returns>
    internal static Amount DailyCulture(FestivalDef festival, Season season)
    {
        ArgumentNullException.ThrowIfNull(festival);

        if (festival.SeasonBonus is { } bonus && bonus.Season == season)
        {
            return festival.CulturePerDay.MultiplyBy(Ratio.FromBonusPercent(bonus.Percent));
        }

        return festival.CulturePerDay;
    }

    /// <summary>
    /// Gets how many daily steps a held festival runs, after the <c>festivalDuration</c> modifiers (for example the
    /// Festival Calendar tradition). Half a day rounds up, and a festival always runs at least one day.
    /// </summary>
    /// <param name="festival">The festival's definition.</param>
    /// <param name="progression">The progression queries that answer the <c>festivalDuration</c> stat.</param>
    /// <returns>The days the festival runs.</returns>
    internal static int DurationDays(FestivalDef festival, IProgressionQueries progression)
    {
        ArgumentNullException.ThrowIfNull(festival);
        ArgumentNullException.ThrowIfNull(progression);

        int bonus = progression.BonusPercent(Stat.FestivalDuration, ModifierContext.Island);
        long scaled = (long)festival.Days * Ratio.FromBonusPercent(bonus).PerMille;
        return (int)Math.Max(1L, (scaled + 500) / 1000);
    }

    /// <summary>
    /// Tests whether a festival type has been held in a year. The state keeps the year plus one (0 means never held),
    /// so a fresh game in year 0 and a remap that adds a festival both read as never held instead of as held in year 0.
    /// </summary>
    /// <param name="lastHeld">The last-held years, by festival ID, as the state keeps them.</param>
    /// <param name="festival">The festival.</param>
    /// <param name="year">The calendar year.</param>
    /// <returns><see langword="true"/> when the type was held that year.</returns>
    internal static bool HeldInYear(int[] lastHeld, FestivalId festival, long year)
    {
        ArgumentNullException.ThrowIfNull(lastHeld);

        return (uint)festival.Value < (uint)lastHeld.Length && lastHeld[festival.Value] == Marker(year);
    }

    /// <summary>
    /// Gets the value the state keeps for a year: the year plus one, so 0 keeps meaning never held.
    /// </summary>
    /// <param name="year">The calendar year.</param>
    /// <returns>The marker to store.</returns>
    internal static int Marker(long year) => checked((int)year) + 1;

    /// <summary>
    /// Runs one daily festival step: credits the day's culture and counts down the run. A run with no days left
    /// ends at the next step instead, so a festival of N days pays N credits and pauses decline timers through N
    /// whole bucket days before its effects are removed and the running flag is cleared.
    /// </summary>
    /// <param name="state">The culture state to tick.</param>
    /// <param name="ctx">The simulation context.</param>
    internal static void Tick(CultureState state, SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);

        if (state.Active is not { } run)
        {
            // Nothing runs, so the flag has nothing to say. It is only ever set alongside a run, but a damaged
            // state read from a save should not pause decline timers forever.
            state.FestivalRunning = false;
            return;
        }

        FestivalDef def = ctx.Content.Get(run.Festival);
        if (run.DaysLeft <= 0)
        {
            ctx.ProgressionService.RemoveSource(EffectSource.ForFestival(run.Festival));
            state.Active = null;
            state.FestivalRunning = false;
            ctx.Events.Emit(new FestivalEnded(ctx.Tick, run.Festival, run.Ground));
            return;
        }

        ctx.PointsLedger.Credit(PointKind.Culture, DailyCulture(def, ctx.Clock.Season));
        state.Active = run with { DaysLeft = run.DaysLeft - 1 };
    }
}
