using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Tests.Fakes;

/// <summary>A settable stand-in for <see cref="IWorkforceQueries"/>. A building with no fill set reports <see cref="DefaultFill"/>.</summary>
internal sealed class FakeWorkforceQueries : IWorkforceQueries
{
    private readonly Dictionary<TierId, int> _supply = [];
    private readonly Dictionary<TierId, int> _demand = [];
    private readonly Dictionary<EntityId, Ratio> _fill = [];
    private readonly HashSet<EntityId> _highPriority = [];

    /// <summary>Gets or sets the fill that buildings without their own report; the default is fully staffed.</summary>
    public Ratio DefaultFill { get; set; } = Ratio.One;

    /// <summary>Sets a tier's supply.</summary>
    /// <param name="tier">The tier.</param>
    /// <param name="workers">The number of workers.</param>
    /// <returns>This fake.</returns>
    public FakeWorkforceQueries SetSupply(TierId tier, int workers)
    {
        _supply[tier] = workers;
        return this;
    }

    /// <summary>Sets a tier's demand.</summary>
    /// <param name="tier">The tier.</param>
    /// <param name="workers">The number of workers wanted.</param>
    /// <returns>This fake.</returns>
    public FakeWorkforceQueries SetDemand(TierId tier, int workers)
    {
        _demand[tier] = workers;
        return this;
    }

    /// <summary>Sets one building's fill.</summary>
    /// <param name="building">The building.</param>
    /// <param name="fill">The fill in per-mille.</param>
    /// <returns>This fake.</returns>
    public FakeWorkforceQueries SetFill(EntityId building, Ratio fill)
    {
        _fill[building] = fill;
        return this;
    }

    /// <summary>Marks a workplace as high priority.</summary>
    /// <param name="building">The workplace.</param>
    /// <param name="high"><see langword="true"/> for high priority.</param>
    /// <returns>This fake.</returns>
    public FakeWorkforceQueries SetHighPriority(EntityId building, bool high = true)
    {
        if (high)
        {
            _highPriority.Add(building);
        }
        else
        {
            _highPriority.Remove(building);
        }

        return this;
    }

    /// <inheritdoc />
    public bool IsHighPriority(EntityId building) => _highPriority.Contains(building);

    /// <inheritdoc />
    public int Supply(TierId tier) => _supply.GetValueOrDefault(tier);

    /// <inheritdoc />
    public int Demand(TierId tier) => _demand.GetValueOrDefault(tier);

    /// <inheritdoc />
    public Ratio FillOf(EntityId building) => _fill.TryGetValue(building, out Ratio fill) ? fill : DefaultFill;
}

/// <summary>Records the supply that Population sets and passes it on to a <see cref="FakeWorkforceQueries"/>, so the supply can be read back.</summary>
/// <param name="queries">The queries whose supply is written.</param>
internal sealed class FakeWorkforceSupply(FakeWorkforceQueries queries) : IWorkforceSupply
{
    private readonly List<(TierId Tier, int Workers)> _calls = [];

    /// <summary>Gets every call, in order.</summary>
    public IReadOnlyList<(TierId Tier, int Workers)> Calls => _calls;

    /// <inheritdoc />
    public void SetSupply(TierId tier, int workers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workers);
        _calls.Add((tier, workers));
        queries.SetSupply(tier, workers);
    }
}
