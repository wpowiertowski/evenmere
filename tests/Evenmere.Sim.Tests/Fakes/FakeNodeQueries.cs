using System.Collections.Immutable;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Culture.Festivals;
using Evenmere.Sim.Systems.Culture.Traditions;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>A settable stand-in for <see cref="IResearchQueries"/>. Nothing is done, available or inspired until the test says so.</summary>
internal sealed class FakeResearchQueries : IResearchQueries
{
    private readonly Dictionary<TechId, Amount> _progress = [];
    private readonly Dictionary<TechId, Amount> _cost = [];
    private readonly HashSet<TechId> _done = [];
    private readonly HashSet<TechId> _available = [];
    private readonly HashSet<TechId> _inspired = [];

    /// <inheritdoc />
    public TechId? Target { get; set; }

    /// <inheritdoc />
    public ImmutableArray<TechId> Queue { get; set; } = [];

    /// <inheritdoc />
    public Amount PointsPerDay { get; set; }

    /// <summary>Describes a tech.</summary>
    /// <param name="tech">The tech.</param>
    /// <param name="cost">What it still costs, in milli-units.</param>
    /// <param name="progress">What it has received, in milli-units.</param>
    /// <param name="done">Whether it is researched.</param>
    /// <param name="available">Whether it can be chosen as the target.</param>
    /// <param name="inspired">Whether its inspiration has triggered.</param>
    /// <returns>This fake.</returns>
    public FakeResearchQueries SetTech(TechId tech, Amount cost, Amount progress = default, bool done = false, bool available = false, bool inspired = false)
    {
        _cost[tech] = cost;
        _progress[tech] = progress;
        Set(_done, tech, done);
        Set(_available, tech, available);
        Set(_inspired, tech, inspired);
        return this;
    }

    /// <inheritdoc />
    public Amount Progress(TechId tech) => _progress.GetValueOrDefault(tech);

    /// <inheritdoc />
    public Amount Cost(TechId tech) => _cost.GetValueOrDefault(tech);

    /// <inheritdoc />
    public bool IsDone(TechId tech) => _done.Contains(tech);

    /// <inheritdoc />
    public bool IsAvailable(TechId tech) => _available.Contains(tech);

    /// <inheritdoc />
    public bool IsInspired(TechId tech) => _inspired.Contains(tech);

    private static void Set(HashSet<TechId> set, TechId tech, bool member)
    {
        if (member)
        {
            set.Add(tech);
        }
        else
        {
            set.Remove(tech);
        }
    }
}

/// <summary>A settable stand-in for <see cref="ICultureQueries"/>. Nothing is adopted, available or inspired until the test says so.</summary>
internal sealed class FakeCultureQueries : ICultureQueries
{
    private readonly Dictionary<CivicId, Amount> _progress = [];
    private readonly Dictionary<CivicId, Amount> _cost = [];
    private readonly HashSet<CivicId> _done = [];
    private readonly HashSet<CivicId> _available = [];
    private readonly HashSet<CivicId> _inspired = [];

    /// <summary>Gets or sets the granted tradition slots with their slotted cards, as <see cref="ICultureQueries.Slots"/> returns them.</summary>
    public ImmutableArray<TraditionSlotView> SlotViews { get; set; } = [];

    /// <summary>Gets or sets the unlocked tradition cards, in ascending ID order.</summary>
    public ImmutableArray<TraditionId> Unlocked { get; set; } = [];

    /// <summary>Gets or sets the slotted style card, or <see langword="null"/> when none is slotted.</summary>
    public TraditionId? Style { get; set; }

    /// <summary>Gets or sets whether a festival is running, as <see cref="ICultureQueries.IsFestivalRunning"/> reports it.</summary>
    public bool FestivalRunning { get; set; }

    /// <summary>Gets or sets the running festival, as <see cref="ICultureQueries.ActiveFestival"/> reports it.</summary>
    public FestivalRun? RunningFestival { get; set; }

    /// <summary>Gets or sets the last-held years by festival ID, as <see cref="ICultureQueries.FestivalsHeldInYear"/> reads them.</summary>
    public Dictionary<FestivalId, int> LastHeldYear { get; } = [];

    /// <inheritdoc />
    public CivicId? Target { get; set; }

    /// <inheritdoc />
    public ImmutableArray<CivicId> Queue { get; set; } = [];

    /// <inheritdoc />
    public Amount PointsPerDay { get; set; }

    /// <summary>Describes a civic.</summary>
    /// <param name="civic">The civic.</param>
    /// <param name="cost">What it still costs, in milli-units.</param>
    /// <param name="progress">What it has received, in milli-units.</param>
    /// <param name="done">Whether it is adopted.</param>
    /// <param name="available">Whether it can be chosen as the target.</param>
    /// <param name="inspired">Whether its inspiration has triggered.</param>
    /// <returns>This fake.</returns>
    public FakeCultureQueries SetCivic(CivicId civic, Amount cost, Amount progress = default, bool done = false, bool available = false, bool inspired = false)
    {
        _cost[civic] = cost;
        _progress[civic] = progress;
        Set(_done, civic, done);
        Set(_available, civic, available);
        Set(_inspired, civic, inspired);
        return this;
    }

    /// <inheritdoc />
    public Amount Progress(CivicId civic) => _progress.GetValueOrDefault(civic);

    /// <inheritdoc />
    public Amount Cost(CivicId civic) => _cost.GetValueOrDefault(civic);

    /// <inheritdoc />
    public bool IsDone(CivicId civic) => _done.Contains(civic);

    /// <inheritdoc />
    public bool IsAvailable(CivicId civic) => _available.Contains(civic);

    /// <inheritdoc />
    public bool IsInspired(CivicId civic) => _inspired.Contains(civic);

    /// <inheritdoc />
    ImmutableArray<TraditionSlotView> ICultureQueries.Slots() => SlotViews;

    /// <inheritdoc />
    ImmutableArray<TraditionId> ICultureQueries.UnlockedTraditions() => Unlocked;

    /// <inheritdoc />
    TraditionId? ICultureQueries.ActiveStyle() => Style;

    /// <inheritdoc />
    bool ICultureQueries.IsFestivalRunning() => FestivalRunning;

    /// <inheritdoc />
    FestivalRun? ICultureQueries.ActiveFestival() => RunningFestival;

    /// <inheritdoc />
    int ICultureQueries.FestivalsHeldInYear(FestivalId festival, int year) =>
        LastHeldYear.TryGetValue(festival, out int last) && last == year + 1 ? 1 : 0;

    private static void Set(HashSet<CivicId> set, CivicId civic, bool member)
    {
        if (member)
        {
            set.Add(civic);
        }
        else
        {
            set.Remove(civic);
        }
    }
}
