using Evenmere.Content.Definitions;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Systems.Production;

/// <summary>
/// The production system's slice of the world state: one <see cref="ProducerRecord"/> per finished building that runs a
/// recipe, and the per-good production <see cref="Totals"/> of today and yesterday. Every member added later gets an explicit integer
/// <c>[Key]</c> that is never reused (ADR-0007).
/// </summary>
[MessagePackObject]
public sealed class ProductionState
{
    /// <summary>Gets or sets the producers, keyed by the building's entity ID. A record exists from the building's activation to its removal.</summary>
    // The formatter is named outright because MessagePack's source generator cannot find one for a generic collection from another
    // assembly and emits code that does not compile; at run time SimResolver would pick the same formatter anyway.
    [Key(0)]
    [MessagePackFormatter(typeof(IdTableFormatter<ProducerRecord>))]
    public IdTable<ProducerRecord> Producers { get; set; } = new();

    /// <summary>Gets or sets what the island has produced, per good, today and yesterday.</summary>
    [Key(1)]
    public ProducedTotals Totals { get; set; } = new();

    /// <summary>Sizes the per-good totals to the loaded content, keeping any values a loaded state already holds.</summary>
    /// <param name="goodCount">The number of goods in the content.</param>
    internal void EnsureGoodSlots(int goodCount)
    {
        Totals ??= new ProducedTotals();
        Totals.EnsureGoodSlots(goodCount);
    }
}

/// <summary>
/// What the island has produced of each good during the current day and the day before, so <c>ProducedYesterday</c> can
/// answer from state. The day buckets roll over on the first tick of a new day.
/// </summary>
[MessagePackObject]
public sealed class ProducedTotals
{
    /// <summary>Gets or sets the whole day (<c>GameClock.Day</c>) that <see cref="Today"/> is counting.</summary>
    [Key(0)]
    public long Day { get; set; }

    /// <summary>Gets or sets what the island has produced so far on <see cref="Day"/>, indexed by <c>GoodId.Value</c>, in milli-units.</summary>
    [Key(1)]
    [ContentIndexed(ContentKind.Good)]
    public Amount[] Today { get; set; } = [];

    /// <summary>Gets or sets what the island produced during the whole day before <see cref="Day"/>, indexed by <c>GoodId.Value</c>, in milli-units.</summary>
    [Key(2)]
    [ContentIndexed(ContentKind.Good)]
    public Amount[] Yesterday { get; set; } = [];

    /// <summary>Sizes the arrays to the loaded content, keeping any values a loaded state already holds.</summary>
    /// <param name="goodCount">The number of goods in the content.</param>
    internal void EnsureGoodSlots(int goodCount)
    {
        Today = Resized(Today, goodCount);
        Yesterday = Resized(Yesterday, goodCount);
    }

    private static Amount[] Resized(Amount[]? values, int length)
    {
        if (values is not null && values.Length == length)
        {
            return values;
        }

        var resized = new Amount[length];
        if (values is not null)
        {
            Array.Copy(values, resized, Math.Min(values.Length, length));
        }

        return resized;
    }
}

/// <summary>
/// Everything the production system remembers about one producer. The buffer arrays run parallel to the recipe's goods:
/// <c>Inputs[i]</c> holds <c>recipe.Inputs[i].Good</c> and <c>Outputs[i]</c> holds <c>recipe.Outputs[i].Good</c> (the recipe
/// lists its goods sorted by good ID). The area yield and the site bonus are caches that the building lifecycle refreshes,
/// so a restored save never depends on replaying lifecycle calls. The last-tick values (productivity, fill, bonus) are kept
/// so that queries can describe the producer without asking the other systems.
/// </summary>
[MessagePackObject]
public sealed class ProducerRecord
{
    /// <summary>The value of a request tick when no request is open.</summary>
    internal const long NoRequest = -1;

    /// <summary>Gets or sets the recipe the building runs.</summary>
    [Key(0)]
    public RecipeId Recipe { get; set; }

    /// <summary>Gets or sets the progress through the current cycle, in productivity units: it adds the per-mille productivity each tick and a cycle completes at <c>cycleSeconds × 4 × 1000</c>.</summary>
    [Key(1)]
    public long Progress { get; set; }

    /// <summary>Gets or sets the input buffer, one amount per recipe input.</summary>
    [Key(2)]
    [ContentIndexed(ContentKind.Good)]
    public Amount[] Inputs { get; set; } = [];

    /// <summary>Gets or sets the output buffer, one amount per recipe output.</summary>
    [Key(3)]
    [ContentIndexed(ContentKind.Good)]
    public Amount[] Outputs { get; set; } = [];

    /// <summary>Gets or sets the tick each open delivery request first arose, one per recipe input; <see cref="NoRequest"/> (-1) when none is open.</summary>
    [Key(4)]
    [NotContentIndexed("parallel to Inputs; the remap permutes them together")]
    public long[] InputRequestTicks { get; set; } = [];

    /// <summary>Gets or sets the tick each open pickup request first arose, one per recipe output; <see cref="NoRequest"/> (-1) when none is open.</summary>
    [Key(5)]
    [NotContentIndexed("parallel to Outputs; the remap permutes them together")]
    public long[] OutputRequestTicks { get; set; } = [];

    /// <summary>Gets or sets the stall reason of the last tick; <see cref="StallReason.None"/> while the building is working.</summary>
    [Key(6)]
    public StallReason Stall { get; set; }

    /// <summary>Gets or sets the cached area yield in per-mille, at most 1000.</summary>
    [Key(7)]
    public Ratio AreaYield { get; set; }

    /// <summary>Gets or sets the cached sum of the building's own productivity site bonuses, in percent.</summary>
    [Key(8)]
    public int SiteBonusPercent { get; set; }

    /// <summary>Gets or sets the productivity applied on the last tick, in per-mille.</summary>
    [Key(9)]
    public Ratio Productivity { get; set; }

    /// <summary>Gets or sets the workforce fill seen on the last tick, in per-mille.</summary>
    [Key(10)]
    public Ratio WorkforceFill { get; set; }

    /// <summary>Gets or sets a value indicating whether the input buffer held the next cycle's inputs on the last tick.</summary>
    [Key(11)]
    public bool InputsReady { get; set; }

    /// <summary>Gets or sets the productivity bonus of the last tick (modifiers plus site bonus), in percent.</summary>
    [Key(12)]
    public int BonusPercent { get; set; }

    /// <summary>Creates the record of a freshly activated producer: empty buffers, no progress, no open requests, full area yield.</summary>
    /// <param name="id">The recipe's ID.</param>
    /// <param name="recipe">The recipe's definition, which sizes the buffers.</param>
    /// <returns>The record.</returns>
    internal static ProducerRecord Create(RecipeId id, RecipeDef recipe)
    {
        var record = new ProducerRecord
        {
            Recipe = id,
            Inputs = new Amount[recipe.Inputs.Length],
            Outputs = new Amount[recipe.Outputs.Length],
            InputRequestTicks = new long[recipe.Inputs.Length],
            OutputRequestTicks = new long[recipe.Outputs.Length],
            AreaYield = Ratio.One,
            InputsReady = recipe.Inputs.IsEmpty,
        };
        Array.Fill(record.InputRequestTicks, NoRequest);
        Array.Fill(record.OutputRequestTicks, NoRequest);
        return record;
    }
}
