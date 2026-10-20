using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Time;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Modifiers;

/// <summary>
/// What the modifier index needs from the rest of the simulation: the season and a way to test a <c>when</c> condition.
/// The progression system provides it once the simulation context exists.
/// </summary>
internal interface IModifierEnvironment
{
    /// <summary>Gets the season of the current tick.</summary>
    Season Season { get; }

    /// <summary>Tests a condition against the world as it is now.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns><see langword="true"/> if it holds.</returns>
    bool Evaluate(Condition condition);
}

/// <summary>
/// The effective-stat index: every registered modifier, grouped by stat, with a cache of the answers. The index is derived
/// state, so it is rebuilt from <see cref="ModifiersState"/> whenever a system is created over a state and is never saved.
/// </summary>
/// <remarks>
/// <para>
/// A modifier's <c>when</c> condition decides how much of its answer may be cached. With no condition, or one that only
/// looks at the season, the sum is cached per (stat, building type, tier, good, season), so a change of season simply
/// asks a different key. A condition that reads anything else (stock, population, counters) can change on any tick, so
/// those modifiers are kept out of the cached sum and evaluated on every query.
/// </para>
/// <para>
/// The cache is a lookup table that is never iterated, and it is cleared whenever a modifier is added.
/// </para>
/// </remarks>
internal sealed class ModifierIndex
{
    private static readonly int _statCount = (int)Enum.GetValues<Stat>().Max() + 1;

    private readonly ContentDb _content;
    private readonly List<Entry>[] _byStat;
    private readonly Dictionary<ulong, CachedAnswer> _cache = [];
    private IModifierEnvironment _environment = DetachedEnvironment.Instance;

    /// <summary>Initializes a new instance of the <see cref="ModifierIndex"/> class with no modifiers.</summary>
    /// <param name="content">The content, which supplies each building type's tags and category.</param>
    public ModifierIndex(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _byStat = new List<Entry>[_statCount];
        for (int i = 0; i < _byStat.Length; i++)
        {
            _byStat[i] = [];
        }
    }

    /// <summary>Gets the number of registered modifiers.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (List<Entry> entries in _byStat)
            {
                count += entries.Count;
            }

            return count;
        }
    }

    /// <summary>Gets the number of answers the cache holds; for tests.</summary>
    public int CachedAnswerCount => _cache.Count;

    /// <summary>Connects the index to the season and the condition evaluator. Until then conditions are false and the season is spring.</summary>
    /// <param name="environment">The environment.</param>
    public void Attach(IModifierEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
        _cache.Clear();
    }

    /// <summary>Registers a saved modifier and drops the cache.</summary>
    /// <param name="modifier">The modifier as it is stored in the state.</param>
    /// <exception cref="InvalidDataException">The modifier names a stat, ID or condition the content does not have.</exception>
    public void Add(ActiveModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);

        if ((int)modifier.Stat < 0 || (int)modifier.Stat >= _statCount || !Enum.IsDefined(modifier.Stat))
        {
            throw new InvalidDataException($"A saved modifier changes stat {(int)modifier.Stat}, which does not exist.");
        }

        CheckTarget(modifier);

        Condition? when = null;
        WhenKind whenKind = WhenKind.None;
        if (modifier.When is { } encoded)
        {
            when = ConditionCodec.Decode(encoded, _content);
            whenKind = IsSeasonOnly(when) ? WhenKind.Seasonal : WhenKind.Dynamic;
        }

        var entry = new Entry(modifier.Source, modifier.TargetKind, modifier.TargetValue, modifier.TargetTag, modifier.Percent, when, whenKind);
        List<Entry> entries = _byStat[(int)modifier.Stat];

        // Ascending by source (kind, then value). A modifier goes after the ones with an equal source, so the order of
        // equal sources is the order they were applied in, and every platform lists the same order.
        int index = entries.Count;
        while (index > 0 && Compare(entries[index - 1].Source, entry.Source) > 0)
        {
            index--;
        }

        entries.Insert(index, entry);
        _cache.Clear();
    }

    /// <summary>Sums the active modifiers of a stat that match a context.</summary>
    /// <param name="stat">The stat.</param>
    /// <param name="context">What the stat is being asked about.</param>
    /// <returns>The total in percent; negative for a net penalty.</returns>
    public int BonusPercent(Stat stat, in ModifierContext context)
    {
        if ((uint)stat >= (uint)_statCount)
        {
            return 0;
        }

        List<Entry> entries = _byStat[(int)stat];
        if (entries.Count == 0)
        {
            return 0;
        }

        Season season = _environment.Season;
        ulong key = KeyOf(stat, context, season);
        if (!_cache.TryGetValue(key, out CachedAnswer answer))
        {
            answer = Build(entries, context);
            _cache[key] = answer;
        }

        int total = answer.Fixed;
        if (answer.Dynamic is { } dynamic)
        {
            foreach (Entry entry in dynamic)
            {
                if (_environment.Evaluate(entry.When!))
                {
                    total += entry.Percent;
                }
            }
        }

        return total;
    }

    /// <summary>Lists the modifiers of a stat that match a context, active or not, in ascending source order.</summary>
    /// <param name="stat">The stat.</param>
    /// <param name="context">What the stat is being asked about.</param>
    /// <param name="contributions">Receives the matches, after the entries it already holds.</param>
    public void ListModifiers(Stat stat, in ModifierContext context, List<ModifierContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        if ((uint)stat >= (uint)_statCount)
        {
            return;
        }

        foreach (Entry entry in _byStat[(int)stat])
        {
            if (Matches(entry, context))
            {
                bool active = entry.When is null || _environment.Evaluate(entry.When);
                contributions.Add(new ModifierContribution(entry.Source, entry.Percent, active));
            }
        }
    }

    private static int Compare(EffectSource left, EffectSource right)
    {
        int byKind = ((int)left.Kind).CompareTo((int)right.Kind);
        return byKind != 0 ? byKind : left.Value.CompareTo(right.Value);
    }

    // Every cache key part is a ushort ID plus one (0 for "none"), so 17 bits each; the stat and season fit in the rest.
    private static ulong KeyOf(Stat stat, in ModifierContext context, Season season) =>
        (ulong)(byte)stat
        | ((ulong)(byte)season << 8)
        | (Part(context.BuildingType?.Value) << 10)
        | (Part(context.Tier?.Value) << 27)
        | (Part(context.Good?.Value) << 44);

    private static ulong Part(ushort? id) => id is { } value ? value + 1UL : 0UL;

    private static bool HasTag(ReadOnlySpan<string> tags, string tag)
    {
        foreach (string candidate in tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // True for conditions that read nothing but the season, so they can be decided once per season and cached.
    private static bool IsSeasonOnly(Condition condition) => condition switch
    {
        SeasonCondition => true,
        AllCondition all => AllSeasonOnly(all.Conditions),
        AnyCondition any => AllSeasonOnly(any.Conditions),
        NotCondition not => IsSeasonOnly(not.Condition),
        _ => false,
    };

    private static bool AllSeasonOnly(System.Collections.Immutable.ImmutableArray<Condition> conditions)
    {
        foreach (Condition condition in conditions)
        {
            if (!IsSeasonOnly(condition))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeTarget(ActiveModifier modifier) => $"{modifier.TargetKind} {modifier.TargetValue}";

    private void CheckTarget(ActiveModifier modifier)
    {
        bool valid = modifier.TargetKind switch
        {
            ModifierTargetKind.Island => true,
            ModifierTargetKind.Building => modifier.TargetValue >= 0 && modifier.TargetValue < _content.Buildings.Count,
            ModifierTargetKind.Tag => !string.IsNullOrEmpty(modifier.TargetTag),
            ModifierTargetKind.Category => Enum.IsDefined((BuildingCategory)modifier.TargetValue),
            ModifierTargetKind.Tier => modifier.TargetValue >= 0 && modifier.TargetValue < _content.Tiers.Count,
            ModifierTargetKind.Good => modifier.TargetValue >= 0 && modifier.TargetValue < _content.Goods.Count,
            ModifierTargetKind.Discovery => Enum.IsDefined((DiscoveryKind)modifier.TargetValue),
            _ => false,
        };

        if (!valid)
        {
            throw new InvalidDataException($"A saved modifier has a target ({DescribeTarget(modifier)}) that the content does not have.");
        }
    }

    private CachedAnswer Build(List<Entry> entries, in ModifierContext context)
    {
        int fixedTotal = 0;
        List<Entry>? dynamic = null;
        foreach (Entry entry in entries)
        {
            if (!Matches(entry, context))
            {
                continue;
            }

            switch (entry.WhenKind)
            {
                case WhenKind.None:
                    fixedTotal += entry.Percent;
                    break;
                case WhenKind.Seasonal:
                    // The cache key holds the season this runs in, so the answer stays right for that season.
                    if (_environment.Evaluate(entry.When!))
                    {
                        fixedTotal += entry.Percent;
                    }

                    break;
                default:
                    (dynamic ??= []).Add(entry);
                    break;
            }
        }

        return new CachedAnswer(fixedTotal, dynamic?.ToArray());
    }

    private bool Matches(Entry entry, in ModifierContext context)
    {
        switch (entry.TargetKind)
        {
            case ModifierTargetKind.Island:
                return true;
            case ModifierTargetKind.Building:
                return context.BuildingType is { } type && type.Value == entry.TargetValue;
            case ModifierTargetKind.Tag:
                return context.BuildingType is { } tagged && HasTag(_content.Buildings.Get(tagged).Tags.AsSpan(), entry.TargetTag!);
            case ModifierTargetKind.Category:
                return context.BuildingType is { } categorised && (int)_content.Buildings.Get(categorised).Category == entry.TargetValue;
            case ModifierTargetKind.Tier:
                return context.Tier is { } tier && tier.Value == entry.TargetValue;
            case ModifierTargetKind.Good:
                return context.Good is { } good && good.Value == entry.TargetValue;
            default:
                // Discovery modifiers (natural wonders and the like) have nothing in ModifierContext to match yet.
                // TODO(M2-06): the exploration system asks about discoveries and adds the context for them.
                return false;
        }
    }

    private enum WhenKind : byte
    {
        None,
        Seasonal,
        Dynamic,
    }

    private sealed record Entry(
        EffectSource Source,
        ModifierTargetKind TargetKind,
        int TargetValue,
        string? TargetTag,
        int Percent,
        Condition? When,
        WhenKind WhenKind);

    private readonly record struct CachedAnswer(int Fixed, Entry[]? Dynamic);

    // The environment before the progression system attaches itself: no condition holds, and it is always spring.
    private sealed class DetachedEnvironment : IModifierEnvironment
    {
        public static DetachedEnvironment Instance { get; } = new();

        public Season Season => Season.Spring;

        public bool Evaluate(Condition condition) => false;
    }
}
