using System.Collections.Immutable;
using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Time;
using Evenmere.Sim.Pipeline;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Services;
using Evenmere.Sim.State;
using Evenmere.Sim.Systems.Modifiers;
using Evenmere.Sim.Systems.Progression;
using Evenmere.Sim.Tests.Fakes;

namespace Evenmere.Sim.Tests.Progression;

/// <summary>
/// The real modifiers and progression systems inside a <see cref="SimContext"/> whose other systems are fakes. The context
/// carries the real progression service, ledger and counters, so effects applied through <see cref="Context"/> land in the
/// real state; everything the progression system reads from elsewhere (buildings, residents, research, the goods pool, the
/// clock) is set through <see cref="Fake"/>.
/// </summary>
internal sealed class ProgressionHarness
{
    private ProgressionHarness(FakeSim fake, ModifiersSystem modifiers, ProgressionSystem progression)
    {
        Fake = fake;
        Modifiers = modifiers;
        Progression = progression;

        FakeServices fakes = fake.Services;
        var services = new SimServices
        {
            BuildingRegistry = fakes.BuildingRegistry,
            BuildingLifecycle = fakes.BuildingLifecycle,
            DirtyChunks = fakes.DirtyChunks,
            Storage = fakes.Storage,
            ProgressionService = progression.Service,
            PointsLedger = progression.Ledger,
            Counters = progression.Counters,
            ProductionBuffers = fakes.ProductionBuffers,
            WorkforceSupply = fakes.WorkforceSupply,
        };
        Context = new SimContext(fake.Content, fake.World, fake.Queries, fake.Events, services);
    }

    /// <summary>Gets the fake world around the systems: the content, the world state, the fake queries and services.</summary>
    public FakeSim Fake { get; }

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Fake.Content;

    /// <summary>Gets the real modifiers service.</summary>
    public ModifiersSystem Modifiers { get; }

    /// <summary>Gets the real progression system.</summary>
    public ProgressionSystem Progression { get; }

    /// <summary>Gets the real progression queries.</summary>
    public ProgressionQueries Queries => Progression.Queries;

    /// <summary>Gets the real progression state, the one in the world state.</summary>
    public ProgressionState State => Fake.World.Progression;

    /// <summary>Gets the context over the real progression services.</summary>
    public SimContext Context { get; }

    /// <summary>Creates a harness over a new, empty world.</summary>
    /// <param name="content">The content; the progression test pack by default.</param>
    /// <param name="tick">The tick the clock starts at.</param>
    /// <returns>The harness.</returns>
    public static ProgressionHarness Create(ContentDb? content = null, long tick = 0)
    {
        var fake = FakeSim.Create(content ?? ProgressionPack.Content, tick: tick);
        return Over(fake);
    }

    /// <summary>
    /// Creates a harness over the state a save holds: the world is written and read back as a save would be, and the systems
    /// are created over the loaded slices, which is when the modifier index is rebuilt.
    /// </summary>
    /// <param name="original">The harness whose world is saved.</param>
    /// <returns>The harness over the loaded state.</returns>
    public static ProgressionHarness Reload(ProgressionHarness original)
    {
        ArgumentNullException.ThrowIfNull(original);

        byte[] bytes = StateSerializer.Write(original.Fake.World);
        WorldState loaded = StateSerializer.Read(bytes, original.Content);

        var fake = FakeSim.Create(original.Content, tick: loaded.Clock.Tick);
        fake.World.Progression = loaded.Progression;
        fake.World.Modifiers = loaded.Modifiers;
        return Over(fake);
    }

    /// <summary>Moves the clock to the first tick of a season of the first year.</summary>
    /// <param name="season">The season.</param>
    public void SetSeason(Season season) => Fake.SetTick((long)season * GameTime.TicksPerSeason);

    /// <summary>Applies effects on behalf of a source through the context's progression service.</summary>
    /// <param name="source">The source.</param>
    /// <param name="effects">The effects.</param>
    public void Apply(EffectSource source, params Effect[] effects) => Context.ProgressionService.ApplyEffects(source, [.. effects]);

    /// <summary>Applies the effects a tech's definition lists.</summary>
    /// <param name="tech">The tech's name.</param>
    public void ApplyTech(string tech)
    {
        TechId id = Content.IdOf<TechId>(tech);
        Context.ProgressionService.ApplyEffects(EffectSource.ForTech(id), Content.Get(id).Effects);
    }

    /// <summary>Unlocks everything marked <c>start</c>, as a new game does.</summary>
    public void ApplyStartUnlocks() => Progression.ApplyStartUnlocks(Context);

    /// <summary>Writes the whole world as a save would.</summary>
    /// <returns>The bytes.</returns>
    public byte[] Save() => StateSerializer.Write(Fake.World);

    /// <summary>Gets the ID of a building type by name.</summary>
    /// <param name="name">The string ID.</param>
    /// <returns>The ID.</returns>
    public BuildingTypeId Building(string name) => Content.IdOf<BuildingTypeId>(name);

    /// <summary>Gets the ID of a good by name.</summary>
    /// <param name="name">The string ID.</param>
    /// <returns>The ID.</returns>
    public GoodId Good(string name) => Content.IdOf<GoodId>(name);

    /// <summary>Gets the ID of a tier by name.</summary>
    /// <param name="name">The string ID.</param>
    /// <returns>The ID.</returns>
    public TierId Tier(string name) => Content.IdOf<TierId>(name);

    private static ProgressionHarness Over(FakeSim fake)
    {
        var modifiers = new ModifiersSystem(fake.World.Modifiers, fake.Content);
        var progression = new ProgressionSystem(fake.World.Progression, fake.Content, modifiers);
        return new ProgressionHarness(fake, modifiers, progression);
    }
}

/// <summary>Shorthand for the content records the tests build by hand.</summary>
internal static class Fx
{
    /// <summary>Builds a modifier effect.</summary>
    /// <param name="stat">The stat.</param>
    /// <param name="percent">The percent.</param>
    /// <param name="target">What it applies to; island-wide by default.</param>
    /// <param name="when">The condition, if any.</param>
    /// <returns>The effect.</returns>
    public static ModifierEffect Modifier(Stat stat, int percent, ModifierTarget? target = null, Condition? when = null) =>
        new(stat, target ?? new IslandTarget(), percent, when);

    /// <summary>Builds an array of effects.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The array.</returns>
    public static ImmutableArray<Effect> Of(params Effect[] effects) => [.. effects];
}
