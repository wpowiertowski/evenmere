using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Time;
using Evenmere.Game.Host;
using Evenmere.Sim.Queries;

namespace Evenmere.Game.Ui.Hud;

/// <summary>
/// Reads the HUD's numbers from the <see cref="SimHost"/> through its read-only queries. This class is the view-model
/// boundary: a system that is still a stub throws <see cref="NotImplementedException"/> naming its task, and only here is
/// that turned into "not available yet" (a <see langword="null"/>, shown as a dash), so every other place still fails loudly.
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
    public long? Residents(TierId tier) => Optional(queries => ReadResidents(queries, tier));

    /// <inheritdoc />
    public Amount? KnowledgePerDay() => Optional(ReadKnowledgePerDay);

    /// <inheritdoc />
    public Amount? CulturePerDay() => Optional(ReadCulturePerDay);

    // TODO(M1-10): return queries.Population.Residents(tier).Value (IPopulationQueries.Residents, declared by M1-00).
    private static long? ReadResidents(ISimQueries queries, TierId tier) => null;

    // TODO(M1-11): return queries.Research.PointsPerDay (IResearchQueries.PointsPerDay, declared by M1-00).
    private static Amount? ReadKnowledgePerDay(ISimQueries queries) => null;

    // TODO(M1-11): return queries.Culture.PointsPerDay (ICultureQueries.PointsPerDay, declared by M1-00).
    private static Amount? ReadCulturePerDay(ISimQueries queries) => null;

    private T? Optional<T>(Func<ISimQueries, T?> read)
        where T : struct
    {
        try
        {
            return read(_host.Query);
        }
        catch (NotImplementedException)
        {
            // The query exists but its system is a stub that names the task that will fill it in (M1-00 step 8).
            return null;
        }
    }
}
