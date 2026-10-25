using Evenmere.Content;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Systems.Culture.Traditions.Commands;

namespace Evenmere.Sim.Tests.Culture.Traditions;

/// <summary>
/// The full simulation over the tradition test content: the real culture, progression and modifier systems, so slotting
/// a card changes <c>BonusPercent</c> exactly as the game sees it. Arrange unlocks and slot grants through
/// <see cref="Arrange"/> (one call: the service ignores a repeated source), queue commands with <see cref="Send"/>,
/// then assert on <see cref="Queries"/> and <see cref="Events"/>.
/// </summary>
internal sealed class TraditionsHarness
{
    private TraditionsHarness(Simulation simulation)
    {
        Sim = simulation;
    }

    /// <summary>Gets the simulation.</summary>
    public Simulation Sim { get; }

    /// <summary>Gets the content.</summary>
    public ContentDb Content => Sim.Context.Content;

    /// <summary>Gets the culture queries: the real ones.</summary>
    public ICultureQueries Culture => Sim.Query.Culture;

    /// <summary>Gets the progression queries: the real ones.</summary>
    public IProgressionQueries Progression => Sim.Query.Progression;

    /// <summary>Creates a harness over a new world.</summary>
    /// <returns>The harness, before any command.</returns>
    public static TraditionsHarness Create() =>
        new(Simulation.CreateNew(TraditionsPack.Content, new NewGameOptions { Seed = 42 }));

    /// <summary>
    /// Creates a harness over the state a save holds: the world is written and read back as a save would be, which is
    /// also when the modifier index is rebuilt from the saved modifiers.
    /// </summary>
    /// <param name="original">The harness whose world is saved.</param>
    /// <returns>The harness over the loaded state.</returns>
    public static TraditionsHarness Reload(TraditionsHarness original)
    {
        ArgumentNullException.ThrowIfNull(original);

        byte[] bytes = StateSerializer.Write(original.Sim.Context.World);
        return new TraditionsHarness(Simulation.Restore(original.Content, bytes));
    }

    /// <summary>Gets a tradition's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public TraditionId Tradition(string name) => Content.IdOf<TraditionId>(name);

    /// <summary>Gets a tier's ID.</summary>
    /// <param name="name">The content ID.</param>
    /// <returns>The ID.</returns>
    public TierId Tier(string name) => Content.IdOf<TierId>(name);

    /// <summary>
    /// Unlocks tradition cards and grants slots, as civics would. Everything is applied at once under one civic source:
    /// applying the same source again is a no-op by contract, so call this once per test.
    /// </summary>
    /// <param name="traditions">The cards to unlock.</param>
    /// <param name="slots">The slot grants, as (type, count) pairs.</param>
    public void Arrange(string[] traditions, params (TraditionSlotType Slot, int Count)[] slots)
    {
        var effects = new List<Effect>(traditions.Length + slots.Length);
        foreach (string name in traditions)
        {
            effects.Add(new UnlockTraditionEffect(Tradition(name)));
        }

        foreach ((TraditionSlotType slot, int count) in slots)
        {
            for (int i = 0; i < count; i++)
            {
                effects.Add(new TraditionSlotEffect(slot));
            }
        }

        CivicId source = Content.IdOf<CivicId>("village_moot");
        Sim.Context.ProgressionService.ApplyEffects(EffectSource.ForCivic(source), [.. effects]);
    }

    /// <summary>Sends a tradition command and runs one tick, as the pipeline would.</summary>
    /// <param name="command">The command.</param>
    public void Send(SetTradition command)
    {
        Sim.Enqueue(command);
        Sim.Tick();
    }

    /// <summary>Returns the events emitted since the last call.</summary>
    /// <returns>The events in emission order.</returns>
    public IReadOnlyList<SimEvent> Events() => Sim.DrainEvents();

    /// <summary>Hashes the whole world state, so determinism tests compare the run's footprint exactly.</summary>
    /// <returns>The state hash.</returns>
    public ulong Hash() => Sim.ComputeStateHash();
}
