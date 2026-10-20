using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;
using MessagePack;

namespace Evenmere.Sim.Systems.Progression;

/// <summary>
/// The progression system's slice of the world state: what has been unlocked, the applied effect sources, the current era,
/// the points ledger and the milestone counters. Everything here is plain arrays and integers, so the serialised bytes
/// (and the state hash) depend only on the values.
/// </summary>
/// <remarks>
/// <para>
/// An unlock set is a bitset over the content table of that kind: bit <c>n</c> of the buildings set is the building whose
/// <c>BuildingTypeId.Value</c> is <c>n</c>. A set holds <c>ceil(count / 64)</c> words, sized when the system is created over
/// the state; a state loaded for different content is rejected rather than reinterpreted (saves from other content are
/// remapped before they get here). The counters are dense arrays indexed by the same content IDs. Unlocks and counters never
/// go down.
/// </para>
/// <para>Every member added later gets an explicit integer <c>[Key]</c> that is never reused (ADR-0007).</para>
/// </remarks>
[MessagePackObject]
public sealed class ProgressionState
{
    /// <summary>Gets or sets the unlocked building types, as a bitset over <c>BuildingTypeId.Value</c>.</summary>
    [Key(0)]
    public ulong[] Buildings { get; set; } = [];

    /// <summary>Gets or sets the unlocked recipes, as a bitset over <c>RecipeId.Value</c>.</summary>
    [Key(1)]
    public ulong[] Recipes { get; set; } = [];

    /// <summary>Gets or sets the unlocked population tiers, as a bitset over <c>TierId.Value</c>.</summary>
    [Key(2)]
    public ulong[] Tiers { get; set; } = [];

    /// <summary>Gets or sets the unlocked traditions, as a bitset over <c>TraditionId.Value</c>. Kept for the culture system (M2-07).</summary>
    [Key(3)]
    public ulong[] Traditions { get; set; } = [];

    /// <summary>Gets or sets the unlocked festivals, as a bitset over <c>FestivalId.Value</c>. Kept for the culture system (M2-08).</summary>
    [Key(4)]
    public ulong[] Festivals { get; set; } = [];

    /// <summary>Gets or sets the great-work types that <c>great_works:&lt;type&gt;</c> has switched on, as a bitset over <c>GreatWorkTypeId.Value</c>.</summary>
    [Key(5)]
    public ulong[] GreatWorks { get; set; } = [];

    /// <summary>Gets or sets the opened kinds of ground: bit <c>n</c> is the <see cref="TerrainAccess"/> whose value is <c>n</c>.</summary>
    [Key(6)]
    public ulong TerrainAccessFlags { get; set; }

    /// <summary>Gets or sets the switched-on features: bit <c>n</c> is the <see cref="FeatureKind"/> whose value is <c>n</c>. Great works are per type, in <see cref="GreatWorks"/>.</summary>
    [Key(7)]
    public ulong FeatureFlags { get; set; }

    /// <summary>Gets or sets the index of the current era in string order, the raw value of its <c>EraId</c>.</summary>
    [Key(8)]
    public int Era { get; set; }

    /// <summary>Gets or sets the tradition slots granted so far, indexed by <see cref="TraditionSlotType"/>. Kept for the culture system (M2-07).</summary>
    [Key(9)]
    public int[] TraditionSlots { get; set; } = [];

    /// <summary>Gets or sets whether an effect asked to reveal the coastline. Kept for the exploration system (M2-06).</summary>
    [Key(10)]
    public bool CoastlineRevealed { get; set; }

    /// <summary>Gets or sets the sources whose effects have been applied, sorted by kind and then value. Applying a source again is a no-op.</summary>
    [Key(11)]
    public EffectSource[] AppliedSources { get; set; } = [];

    /// <summary>Gets or sets the points credited since they were last taken, indexed by <see cref="PointKind"/>. Never negative.</summary>
    [Key(12)]
    public Amount[] Points { get; set; } = [];

    /// <summary>Gets or sets the cumulative production of each good in milli-units, indexed by <c>GoodId.Value</c>.</summary>
    [Key(13)]
    public long[] Produced { get; set; } = [];

    /// <summary>Gets or sets the cumulative consumption of each good in milli-units, indexed by <c>GoodId.Value</c>.</summary>
    [Key(14)]
    public long[] Consumed { get; set; } = [];

    /// <summary>Gets or sets the number of completed buildings of each type, indexed by <c>BuildingTypeId.Value</c>.</summary>
    [Key(15)]
    public long[] Built { get; set; } = [];

    /// <summary>Gets or sets the number of road tiles laid, of any road type.</summary>
    [Key(16)]
    public long RoadTilesLaid { get; set; }

    /// <summary>Gets how many members <see cref="PointKind"/> has, which is the length of <see cref="Points"/>.</summary>
    internal static int PointKindCount { get; } = EnumSize<PointKind>();

    /// <summary>Gets how many members <see cref="TraditionSlotType"/> has, which is the length of <see cref="TraditionSlots"/>.</summary>
    internal static int SlotTypeCount { get; } = EnumSize<TraditionSlotType>();

    /// <summary>
    /// Sizes every array to the content, or checks the sizes it already has. A new game's state has empty arrays and gets
    /// zeroed ones; a state read from a save must already match the content.
    /// </summary>
    /// <param name="content">The loaded content.</param>
    /// <exception cref="InvalidDataException">The state does not fit the content, or holds a value no game could have produced.</exception>
    internal void FitTo(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);

        Buildings = Fit(Buildings, Bits.WordCount(content.Buildings.Count), nameof(Buildings));
        Recipes = Fit(Recipes, Bits.WordCount(content.Recipes.Count), nameof(Recipes));
        Tiers = Fit(Tiers, Bits.WordCount(content.Tiers.Count), nameof(Tiers));
        Traditions = Fit(Traditions, Bits.WordCount(content.Traditions.Count), nameof(Traditions));
        Festivals = Fit(Festivals, Bits.WordCount(content.Festivals.Count), nameof(Festivals));
        GreatWorks = Fit(GreatWorks, Bits.WordCount(content.GreatWorks.Count), nameof(GreatWorks));
        TraditionSlots = Fit(TraditionSlots, SlotTypeCount, nameof(TraditionSlots));
        Points = Fit(Points, PointKindCount, nameof(Points));
        Produced = Fit(Produced, content.Goods.Count, nameof(Produced));
        Consumed = Fit(Consumed, content.Goods.Count, nameof(Consumed));
        Built = Fit(Built, content.Buildings.Count, nameof(Built));
        AppliedSources ??= [];

        if (content.Eras.Count > 0 && (Era < 0 || Era >= content.Eras.Count))
        {
            throw new InvalidDataException($"The progression state is in era {Era}, but the content defines {content.Eras.Count} eras.");
        }

        CheckNotNegative(TraditionSlots, nameof(TraditionSlots));
        CheckNotNegative(Produced, nameof(Produced));
        CheckNotNegative(Consumed, nameof(Consumed));
        CheckNotNegative(Built, nameof(Built));
        if (RoadTilesLaid < 0)
        {
            throw new InvalidDataException("The progression state counts a negative number of road tiles.");
        }

        foreach (Amount held in Points)
        {
            if (held < Amount.Zero)
            {
                throw new InvalidDataException("The points ledger holds a negative amount.");
            }
        }

        for (int i = 1; i < AppliedSources.Length; i++)
        {
            if (Compare(AppliedSources[i - 1], AppliedSources[i]) >= 0)
            {
                throw new InvalidDataException("The applied effect sources are not sorted and distinct.");
            }
        }
    }

    /// <summary>Orders effect sources by kind and then value, which is the order <see cref="AppliedSources"/> is kept in.</summary>
    /// <param name="left">The first source.</param>
    /// <param name="right">The second source.</param>
    /// <returns>Negative, zero or positive as <paramref name="left"/> sorts before, with or after <paramref name="right"/>.</returns>
    internal static int Compare(EffectSource left, EffectSource right)
    {
        int byKind = ((int)left.Kind).CompareTo((int)right.Kind);
        return byKind != 0 ? byKind : left.Value.CompareTo(right.Value);
    }

    private static int EnumSize<TEnum>()
        where TEnum : struct, Enum => Enum.GetValues<TEnum>().Max(static v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)) + 1;

    // An empty array is the state of a new game; any other length must already be the right one.
    private static T[] Fit<T>(T[]? current, int length, string name)
    {
        if (current is null || current.Length == 0)
        {
            return length == 0 ? [] : new T[length];
        }

        return current.Length == length
            ? current
            : throw new InvalidDataException($"The progression state's {name} has {current.Length} entries, but the content needs {length}.");
    }

    private static void CheckNotNegative(int[] values, string name)
    {
        foreach (int value in values)
        {
            if (value < 0)
            {
                throw new InvalidDataException($"The progression state's {name} holds a negative count.");
            }
        }
    }

    private static void CheckNotNegative(long[] values, string name)
    {
        foreach (long value in values)
        {
            if (value < 0)
            {
                throw new InvalidDataException($"The progression state's {name} holds a negative count.");
            }
        }
    }
}
