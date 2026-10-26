using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Systems.Exploration;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>
/// A settable stand-in for <see cref="IExplorationQueries"/>. Every region counts as revealed until the test says
/// otherwise, no expedition is under way, and no discovery is placed. (M2-06.)
/// </summary>
internal sealed class FakeExplorationQueries : IExplorationQueries
{
    private readonly HashSet<ushort> _hidden = [];
    private readonly Dictionary<EntityId, ExpeditionView> _expeditions = [];
    private readonly List<DiscoveryView> _discoveries = [];

    /// <summary>Hides a region, so <see cref="IsRevealed"/> answers false for it.</summary>
    /// <param name="region">The region.</param>
    /// <returns>This fake.</returns>
    public FakeExplorationQueries Hide(ushort region)
    {
        _hidden.Add(region);
        return this;
    }

    /// <summary>Sets the expedition a lodge is running.</summary>
    /// <param name="lodge">The lodge.</param>
    /// <param name="region">The target region.</param>
    /// <param name="daysLeft">The days left.</param>
    /// <returns>This fake.</returns>
    public FakeExplorationQueries SetExpedition(EntityId lodge, ushort region, int daysLeft)
    {
        _expeditions[lodge] = new ExpeditionView(lodge, region, daysLeft);
        return this;
    }

    /// <summary>Adds a discovery the fake reports.</summary>
    /// <param name="discovery">The discovery.</param>
    /// <returns>This fake.</returns>
    public FakeExplorationQueries AddDiscovery(DiscoveryView discovery)
    {
        _discoveries.Add(discovery);
        return this;
    }

    /// <inheritdoc />
    public bool IsRevealed(ushort region) => !_hidden.Contains(region);

    /// <inheritdoc />
    public ushort[] RevealedRegions()
    {
        // The fake has no map, so "revealed" is every region except the hidden ones up to a small cap.
        // Tests that need exact sets hide what they must and ignore the rest.
        List<ushort> revealed = new();
        for (int region = 0; region < 64; region++)
        {
            ushort number = (ushort)region;
            if (!_hidden.Contains(number))
            {
                revealed.Add(number);
            }
        }

        return [.. revealed];
    }

    /// <inheritdoc />
    public ExpeditionView? ExpeditionForLodge(EntityId lodge) =>
        _expeditions.TryGetValue(lodge, out ExpeditionView expedition) ? expedition : null;

    /// <inheritdoc />
    public ExpeditionView[] Expeditions() => [.. _expeditions.Values.OrderBy(static e => e.Lodge.Value)];

    /// <inheritdoc />
    public DiscoveryView[] Discoveries() => [.. _discoveries];
}
