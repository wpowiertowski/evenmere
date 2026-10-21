using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Systems.Advisors;
using Evenmere.Sim.Systems.Construction;
using Evenmere.Sim.Systems.Culture;
using Evenmere.Sim.Systems.Economy;
using Evenmere.Sim.Systems.Exploration;
using Evenmere.Sim.Systems.Happenings;
using Evenmere.Sim.Systems.Logistics;
using Evenmere.Sim.Systems.Map;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Needs;
using Evenmere.Sim.Systems.Population;
using Evenmere.Sim.Systems.Production;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Systems.Research;
using Evenmere.Sim.Systems.Roads;
using Evenmere.Sim.Systems.Statistics;
using Evenmere.Sim.Systems.Storage;
using Evenmere.Sim.Systems.Workforce;
using MessagePack;

namespace Evenmere.Sim.State;

/// <summary>
/// Everything that changes as the simulation runs, and nothing else: the clock, the entity ID counter, the world seed,
/// one random stream per system and one state object per system. It holds plain data only, with no engine types and
/// no references to presentation objects. Each system owns its own slice and others read it through that system's
/// query interface. The serialisation of this class is the save body and the input of the state hash (ADR-0003,
/// ADR-0007), so every member has an explicit integer key that is never reused or renumbered.
/// </summary>
[MessagePackObject]
public sealed class WorldState
{
    /// <summary>Gets or sets the world seed from which every system's random stream was derived.</summary>
    [Key(0)]
    public ulong Seed { get; set; }

    /// <summary>Gets or sets the simulation clock. Only the simulation's clock step advances it.</summary>
    [Key(1)]
    public GameClock Clock { get; set; }

    /// <summary>Gets or sets the allocator of entity IDs, which are monotonic and never reused.</summary>
    [Key(2)]
    public EntityIdAllocator EntityIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the random stream of each system, indexed by <see cref="SystemId"/>. Systems reach theirs through
    /// <c>SimContext.Rng</c>, by reference, so a draw advances the stored generator.
    /// </summary>
    [Key(3)]
    [NotContentIndexed("one random stream per system, not content data")]
    public Pcg32[] SystemRngs { get; set; } = [];

    /// <summary>Gets or sets the map service's state. Filled by M1-01.</summary>
    [Key(4)]
    public MapState Map { get; set; } = new();

    /// <summary>Gets or sets the roads service's state. Filled by M1-05.</summary>
    [Key(5)]
    public RoadsState Roads { get; set; } = new();

    /// <summary>Gets or sets the storage service's state: the island goods pool.</summary>
    [Key(6)]
    public StorageState Storage { get; set; } = new();

    /// <summary>Gets or sets the modifiers service's state. Filled by M1-04.</summary>
    [Key(7)]
    public ModifiersState Modifiers { get; set; } = new();

    /// <summary>Gets or sets the construction system's state. Filled by M1-06.</summary>
    [Key(8)]
    public ConstructionState Construction { get; set; } = new();

    /// <summary>Gets or sets the workforce system's state. Filled by M1-07.</summary>
    [Key(9)]
    public WorkforceState Workforce { get; set; } = new();

    /// <summary>Gets or sets the production system's state. Filled by M1-08.</summary>
    [Key(10)]
    public ProductionState Production { get; set; } = new();

    /// <summary>Gets or sets the logistics system's state. Filled by M1-09.</summary>
    [Key(11)]
    public LogisticsState Logistics { get; set; } = new();

    /// <summary>Gets or sets the exploration system's state. Filled by M2-06.</summary>
    [Key(12)]
    public ExplorationState Exploration { get; set; } = new();

    /// <summary>Gets or sets the needs system's state. Filled by M1-10.</summary>
    [Key(13)]
    public NeedsState Needs { get; set; } = new();

    /// <summary>Gets or sets the population system's state. Filled by M1-10.</summary>
    [Key(14)]
    public PopulationState Population { get; set; } = new();

    /// <summary>Gets or sets the economy system's state. Filled by M3-01.</summary>
    [Key(15)]
    public EconomyState Economy { get; set; } = new();

    /// <summary>Gets or sets the research system's state. Filled by M1-11.</summary>
    [Key(16)]
    public ResearchState Research { get; set; } = new();

    /// <summary>Gets or sets the culture system's state. Filled by M1-11.</summary>
    [Key(17)]
    public CultureState Culture { get; set; } = new();

    /// <summary>Gets or sets the progression system's state. Filled by M1-04.</summary>
    [Key(18)]
    public ProgressionState Progression { get; set; } = new();

    /// <summary>Gets or sets the happenings system's state. Filled by M2-12.</summary>
    [Key(19)]
    public HappeningsState Happenings { get; set; } = new();

    /// <summary>Gets or sets the advisors system's state. Filled by M1-15.</summary>
    [Key(20)]
    public AdvisorsState Advisors { get; set; } = new();

    /// <summary>Gets or sets the statistics system's state. Filled by M1-12.</summary>
    [Key(21)]
    public StatisticsState Statistics { get; set; } = new();

    /// <summary>
    /// Creates the state of a new, empty world at tick zero: no map yet (M1-01 adds it), every storage slot at zero and
    /// every system's random stream seeded from the world seed and the system's name.
    /// </summary>
    /// <param name="content">The loaded content, which sizes the storage pool.</param>
    /// <param name="seed">The world seed.</param>
    /// <returns>The new world state.</returns>
    public static WorldState CreateNew(ContentDb content, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rngs = new Pcg32[SystemNames.Count];
        for (int i = 0; i < rngs.Length; i++)
        {
            rngs[i] = Pcg32.ForSystem(seed, SystemNames.Of((SystemId)i));
        }

        return new WorldState
        {
            Seed = seed,
            Clock = new GameClock(0),
            EntityIds = new EntityIdAllocator(),
            SystemRngs = rngs,
            Storage = StorageState.ForGoodCount(content.Goods.Count),
        };
    }

    /// <summary>
    /// Checks that a state read from bytes is complete and fits the loaded content, so a corrupt or foreign save fails
    /// at load instead of far into a run. Content ID remapping for saves from other content is M1-14's job, before this.
    /// </summary>
    /// <param name="content">The loaded content.</param>
    /// <exception cref="InvalidDataException">The state is incomplete or does not match the content.</exception>
    internal void Validate(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // MessagePack writes nil for a null member, so a damaged file can leave any reference member null.
        object?[] members =
        [
            EntityIds, SystemRngs, Map, Roads, Storage, Modifiers, Construction, Workforce, Production, Logistics,
            Exploration, Needs, Population, Economy, Research, Culture, Progression, Happenings, Advisors, Statistics,
        ];
        if (Array.IndexOf(members, null) >= 0)
        {
            throw new InvalidDataException("The world state is missing a required member.");
        }

        if (SystemRngs.Length != SystemNames.Count)
        {
            throw new InvalidDataException($"The world state has {SystemRngs.Length} random streams; expected {SystemNames.Count}.");
        }

        Amount[]? stock = Storage.Stock;
        if (stock is null || stock.Length != content.Goods.Count)
        {
            throw new InvalidDataException(
                $"The storage pool has {stock?.Length ?? 0} goods but the content defines {content.Goods.Count}.");
        }

        foreach (Amount held in stock)
        {
            if (held < Amount.Zero)
            {
                throw new InvalidDataException("The storage pool holds a negative amount.");
            }
        }
    }
}
