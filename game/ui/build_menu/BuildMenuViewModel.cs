using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Common;
using Evenmere.Game.Ui.Hud;
using Evenmere.Sim.Queries;

namespace Evenmere.Game.Ui.BuildMenu;

/// <summary>
/// The numbers and text of the build menu, as plain properties the menu nodes bind to. It has no Godot types: it
/// reads the content once, re-reads the locked or unlocked state through <see cref="IProgressionQueries"/> and
/// refreshes at most four times a second (<see cref="ViewModel"/>). The owning node rebuilds its rows from
/// <see cref="Tabs"/> whenever <see cref="ViewModel.Refreshed"/> fires.
/// </summary>
public sealed class BuildMenuViewModel : ViewModel
{
    // The island runs 120 ticks a day and a tick is a quarter of a game second (architecture.md, time model), so a
    // day is 30 game seconds and a recipe repeats 30 / cycleSeconds times a day at 100% productivity.
    private const long TicksPerDay = 120;
    private const long TickNumerator = 1;
    private const long TickDenominator = 4;
    private const long SecondsPerDay = TicksPerDay * TickNumerator / TickDenominator;

    private static readonly BuildingVisualRef _fallbackVisual = new("?", "Box", "#8A8A8A");

    private readonly ContentDb _content;
    private readonly UiStrings _strings;
    private readonly IProgressionQueries _progression;
    private readonly IWorkforceQueries _workforce;
    private readonly IReadOnlyDictionary<string, BuildingVisualRef> _visuals;
    private readonly UnlockIndex _unlocks;

    /// <summary>Initializes a new instance of the <see cref="BuildMenuViewModel"/> class.</summary>
    /// <param name="content">The content database.</param>
    /// <param name="strings">The display text.</param>
    /// <param name="progression">The unlock queries.</param>
    /// <param name="workforce">The workforce supply and demand queries, for each entry's free-workers line.</param>
    /// <param name="visuals">The placeholder icon, shape and colour per content building ID.</param>
    /// <param name="unlocks">The reverse unlock index built from the content.</param>
    public BuildMenuViewModel(
        ContentDb content,
        UiStrings strings,
        IProgressionQueries progression,
        IWorkforceQueries workforce,
        IReadOnlyDictionary<string, BuildingVisualRef> visuals,
        UnlockIndex unlocks)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(workforce);
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(unlocks);
        _content = content;
        _strings = strings;
        _progression = progression;
        _workforce = workforce;
        _visuals = visuals;
        _unlocks = unlocks;
        Tabs = [];
    }

    /// <summary>Gets the tabs in display order, each with its entries in content order plus the demolish entry last.</summary>
    public ImmutableArray<BuildTabData> Tabs { get; private set; }

    /// <summary>Gets how many buildable entries exist across all tabs (roads and demolish included, locked included).</summary>
    public int EntryCount
    {
        get
        {
            int count = 0;
            foreach (BuildTabData tab in Tabs)
            {
                count += tab.Entries.Length;
            }

            return count;
        }
    }

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        var tabs = new List<BuildTabData>((int)BuildTab.Culture + 1);
        var byTab = new List<BuildEntry>[(int)BuildTab.Culture + 1];
        for (int i = 0; i < byTab.Length; i++)
        {
            byTab[i] = [];
        }

        foreach (BuildingDef building in _content.Buildings.Items)
        {
            // Unique buildings (the Founders' Hall) are pre-placed and can never be built twice.
            if (building.Unique)
            {
                continue;
            }

            byTab[(int)BuildCategory.TabFor(building)].Add(MakeEntry(building));
        }

        byTab[(int)BuildTab.Storage].Add(MakeDemolishEntry());

        for (int i = 0; i < byTab.Length; i++)
        {
            var tab = (BuildTab)i;
            tabs.Add(new BuildTabData(tab, _strings.Get(TabKey(tab)), [.. byTab[i]]));
        }

        Tabs = [.. tabs];
    }

    private static string TabKey(BuildTab tab) => tab switch
    {
        BuildTab.Housing => "ui.build.tab.housing",
        BuildTab.Production => "ui.build.tab.production",
        BuildTab.Storage => "ui.build.tab.storage",
        BuildTab.Services => "ui.build.tab.services",
        _ => "ui.build.tab.culture",
    };

    private BuildEntry MakeEntry(BuildingDef building)
    {
        string name = _content.Buildings.NameOf(building.Id);
        bool locked = !_progression.IsUnlocked(building.Id);
        string lockText = locked ? LockTextFor(building.Id) : string.Empty;
        BuildingVisualRef visual = _visuals.TryGetValue(name, out BuildingVisualRef found) ? found : _fallbackVisual;

        string? workforce = null;
        bool workforceShort = false;
        if (building.Workforce is { } staff)
        {
            var summary = WorkforceSummary.From(
                _workforce.Supply(staff.Tier),
                _workforce.Demand(staff.Tier),
                _workforce.WorkingBelowTier(staff.Tier),
                _workforce.FilledFromAbove(staff.Tier));
            string tierName = _strings.TierName(_content.NameOf(staff.Tier));
            string countText = NumberFormat.Count(staff.Count);
            if (summary.Free > 0)
            {
                workforce = _strings.Format("ui.build.workforce", ("count", countText), ("tier", tierName), ("free", NumberFormat.Count(summary.Free)));
                workforceShort = false;
            }
            else if (FindHelperTier(staff.Tier) is { } helper)
            {
                // No workers of its own tier are free, but an unlocked higher tier has idle workers who would
                // step down: name the nearest such tier, with no warning tint.
                string helperName = _strings.TierName(_content.NameOf(helper));
                workforce = _strings.Format("ui.build.workforce.fill", ("count", countText), ("tier", tierName), ("helper", helperName));
                workforceShort = false;
            }
            else
            {
                workforce = _strings.Format("ui.build.workforce.none", ("count", countText), ("tier", tierName), ("free", NumberFormat.Count(0)));
                workforceShort = true;
            }
        }

        return new BuildEntry(
            name,
            building.Id,
            building.RoadCost.HasValue,
            _strings.Get($"building.{name}.name"),
            PickIcon(visual),
            visual.ColourHex,
            CostTextFor(building),
            workforce,
            workforceShort,
            DetailTextFor(building),
            DescriptionFor(name),
            locked,
            lockText);
    }

    // The nearest higher tier (in rank order) that is unlocked and has idle workers of its own, who would step
    // down into this tier's jobs (ADR-0016). Null when no higher tier can fill in.
    private TierId? FindHelperTier(TierId tier)
    {
        ImmutableArray<TierId> order = _content.TiersByOrder;
        int rank = order.IndexOf(tier);
        if (rank < 0)
        {
            return null;
        }

        for (int i = rank + 1; i < order.Length; i++)
        {
            TierId higher = order[i];
            if (!_progression.IsUnlocked(higher))
            {
                continue;
            }

            var summary = WorkforceSummary.From(
                _workforce.Supply(higher),
                _workforce.Demand(higher),
                _workforce.WorkingBelowTier(higher),
                _workforce.FilledFromAbove(higher));
            if (summary.Free > 0)
            {
                return higher;
            }
        }

        return null;
    }

    private BuildEntry MakeDemolishEntry() => new(
        "demolish",
        null,
        false,
        _strings.Get("ui.build.demolish.name"),
        "✕",
        "#8A8A8A",
        _strings.Get("ui.build.free"),
        null,
        false,
        _strings.Get("ui.build.demolish.tip"),
        _strings.Get("ui.build.demolish.desc"),
        false,
        string.Empty);

    private string LockTextFor(BuildingTypeId building)
    {
        if (_unlocks.TryGet(building, out UnlockIndex.UnlockSource source) && !source.FromStart)
        {
            string prefix = source.Kind == UnlockIndex.SourceKind.Tech ? "tech" : "civic";
            return _strings.Format("ui.build.locked_by", ("name", _strings.Get($"{prefix}.{source.NodeName}.name")));
        }

        return _strings.Get("ui.build.locked.unknown");
    }

    private string CostTextFor(BuildingDef building)
    {
        var parts = new List<string>();
        foreach (GoodAmount goods in building.Cost.Goods)
        {
            parts.Add($"{NumberFormat.Units(goods.Amount)} {_strings.GoodName(_content.NameOf(goods.Good))}");
        }

        if (building.BuildDays > 0)
        {
            string dayKey = building.BuildDays == 1 ? "ui.build.days.one" : "ui.build.days.other";
            parts.Add(_strings.Format(dayKey, ("count", NumberFormat.Count(building.BuildDays))));
        }

        return parts.Count == 0
            ? _strings.Get("ui.build.free")
            : _strings.Format("ui.build.cost", ("cost", string.Join(", ", parts)));
    }

    private string DetailTextFor(BuildingDef building)
    {
        string detail;
        if (building.Recipe is { } recipeId)
        {
            detail = RecipeTextFor(recipeId);
        }
        else if (building.Storage is { } storage)
        {
            detail = _strings.Format(
                "ui.build.storage",
                ("reach", NumberFormat.Count(storage.Reach)),
                ("carts", NumberFormat.Count(storage.Carts)));
        }
        else if (building.Service is { } service)
        {
            string key = service.Kind switch
            {
                ServiceKind.Market => "ui.build.service.market",
                ServiceKind.Shrine => "ui.build.service.shrine",
                _ => "ui.build.service.other",
            };
            detail = _strings.Format(key, ("coverage", NumberFormat.Count(service.Coverage)));
        }
        else
        {
            detail = _strings.Get("ui.build.residence");
        }

        string outputs = OutputsTextFor(building);
        return outputs.Length == 0 ? detail : $"{detail} - {outputs}";
    }

    private string RecipeTextFor(RecipeId recipeId)
    {
        RecipeDef recipe = _content.Get(recipeId);
        string body;
        if (recipe.Inputs.IsEmpty)
        {
            body = JoinAmounts(recipe.Outputs);
        }
        else
        {
            body = _strings.Format(
                "ui.build.recipe",
                ("inputs", JoinAmounts(recipe.Inputs)),
                ("outputs", JoinAmounts(recipe.Outputs)));
        }

        if (recipe.Outputs.IsEmpty || recipe.CycleSeconds <= 0)
        {
            return body;
        }

        // Per-day output at 100%: the recipe repeats SecondsPerDay / cycleSeconds times a day. Integer maths keeps
        // the display exact (for example 30 / 40 s gives 750 milli, shown as 0.8/day).
        var rates = new List<string>();
        foreach (GoodAmount output in recipe.Outputs)
        {
            long milliPerDay = checked(output.Amount.Milli * SecondsPerDay / recipe.CycleSeconds);
            rates.Add(NumberFormat.Rate(Amount.FromMilli(milliPerDay)));
        }

        return _strings.Format("ui.build.recipe.rate", ("recipe", body), ("rate", string.Join(", ", rates)));
    }

    private string JoinAmounts(ImmutableArray<GoodAmount> amounts)
    {
        var parts = new List<string>(amounts.Length);
        foreach (GoodAmount amount in amounts)
        {
            parts.Add($"{NumberFormat.Units(amount.Amount)} {_strings.GoodName(_content.NameOf(amount.Good))}");
        }

        return string.Join(" + ", parts);
    }

    private string OutputsTextFor(BuildingDef building)
    {
        if (building.Outputs is not { } outputs)
        {
            return string.Empty;
        }

        var parts = new List<string>(2);
        if (outputs.Knowledge.Milli > 0)
        {
            parts.Add(_strings.Format(
                "ui.build.outputs",
                ("rate", NumberFormat.Rate(outputs.Knowledge)),
                ("name", _strings.Get("ui.hud.knowledge"))));
        }

        if (outputs.Culture.Milli > 0)
        {
            parts.Add(_strings.Format(
                "ui.build.outputs",
                ("rate", NumberFormat.Rate(outputs.Culture)),
                ("name", _strings.Get("ui.hud.culture"))));
        }

        return string.Join(", ", parts);
    }

    private string DescriptionFor(string name)
    {
        string key = $"building.{name}.desc";
        string text = _strings.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? string.Empty : text;
    }

    private static string PickIcon(BuildingVisualRef visual) =>
        visual.Icon.Length > 0 ? visual.Icon : visual.Shape.Length > 0 ? visual.Shape[..1] : "?";

    /// <summary>
    /// The placeholder look of one building for the menu: the mapping's icon glyph (or the mapping's shape name,
    /// whose first letter stands in when the icon is empty) and its sRGB hex colour for the swatch.
    /// </summary>
    /// <param name="Icon">The icon glyph.</param>
    /// <param name="Shape">The placeholder shape name.</param>
    /// <param name="ColourHex">The sRGB hex colour.</param>
    public readonly record struct BuildingVisualRef(string Icon, string Shape, string ColourHex);

    /// <summary>
    /// One menu entry: a building, a road type, or the fixed demolish entry. A road entry carries its building
    /// type too; the menu calls <c>BeginRoad</c> for it instead of <c>BeginBuild</c>.
    /// </summary>
    /// <param name="Key">The content building ID, or <c>demolish</c> for the fixed entry.</param>
    /// <param name="Type">The building type, or <see langword="null"/> for the demolish entry.</param>
    /// <param name="IsRoad">Whether selecting it starts the road tool.</param>
    /// <param name="Name">The display name.</param>
    /// <param name="Icon">The icon glyph.</param>
    /// <param name="ColourHex">The swatch colour.</param>
    /// <param name="CostText">The cost and build time line.</param>
    /// <param name="WorkforceText">The workforce line, or <see langword="null"/> when it needs no workers.</param>
    /// <param name="WorkforceShort">Whether no workers are free for this entry's tier, so the line takes the warning tint.</param>
    /// <param name="DetailText">The recipe, storage, service or residence line.</param>
    /// <param name="Description">The short locale description, or empty when there is none.</param>
    /// <param name="Locked">Whether the entry is still locked.</param>
    /// <param name="LockText">The "Unlocked by …" line, or empty when unlocked.</param>
    public sealed record BuildEntry(
        string Key,
        BuildingTypeId? Type,
        bool IsRoad,
        string Name,
        string Icon,
        string ColourHex,
        string CostText,
        string? WorkforceText,
        bool WorkforceShort,
        string DetailText,
        string Description,
        bool Locked,
        string LockText);

    /// <summary>A tab and its entries in display order.</summary>
    /// <param name="Tab">The tab.</param>
    /// <param name="Title">The display title.</param>
    /// <param name="Entries">The entries in content order, with demolish last on the Storage tab.</param>
    public sealed record BuildTabData(BuildTab Tab, string Title, ImmutableArray<BuildEntry> Entries);
}
