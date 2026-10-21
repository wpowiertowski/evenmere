using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Sim.Queries;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// Reads the HUD's numbers from the <see cref="SimHost"/> through its read-only queries. This class is the view-model
/// boundary: it never holds simulation state of its own, and every number comes straight from a query.
/// </summary>
public sealed class SimHostHudSource : IHudSource
{
    private readonly SimHost _host;

    /// <summary>Initializes a new instance of the <see cref="SimHostHudSource"/> class.</summary>
    /// <param name="host">The host that owns the simulation.</param>
    public SimHostHudSource(SimHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <inheritdoc />
    public bool IsRunning => _host.IsRunning;

    /// <inheritdoc />
    public GameClock Clock => _host.Clock;

    /// <inheritdoc />
    public SimSpeed Speed => _host.Speed;

    /// <inheritdoc />
    public Amount Stock(GoodId good) => _host.Query.Storage.Stock(good);

    /// <inheritdoc />
    public long? Residents(TierId tier) => _host.Query.Population.Residents(tier);

    /// <inheritdoc />
    public bool IsTierUnlocked(TierId tier) => _host.Query.Progression.IsUnlocked(tier);

    /// <inheritdoc />
    public Amount? KnowledgePerDay() => _host.Query.Research.PointsPerDay;

    /// <inheritdoc />
    public Amount? CulturePerDay() => _host.Query.Culture.PointsPerDay;
}
