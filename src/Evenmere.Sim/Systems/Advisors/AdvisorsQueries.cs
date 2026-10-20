using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Advisors;

/// <summary>Implements <see cref="IAdvisorsQueries"/>. M1-00 declares the methods and M1-15 implements them; until then each throws <see cref="NotImplementedException"/>.</summary>
internal sealed class AdvisorsQueries : IAdvisorsQueries
{
    /// <inheritdoc />
    public ReadOnlySpan<AdviceView> Active() => throw new NotImplementedException("M1-15");
}
