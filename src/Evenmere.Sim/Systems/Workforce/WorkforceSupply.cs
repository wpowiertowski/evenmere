using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Workforce;

/// <summary>
/// How Population tells Workforce how many workers each tier supplies. Population calls it once a day at day start;
/// Workforce scales the stored value by the <c>workforce</c> modifiers when it allocates (M1-07).
/// </summary>
internal sealed class WorkforceSupply(WorkforceState state, ContentDb content) : IWorkforceSupply
{
    private readonly WorkforceState _state = state;
    private readonly ContentDb _content = content;

    /// <inheritdoc />
    public void SetSupply(TierId tier, int workers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workers);
        _state.EnsureTierSlots(_content.Tiers.Count);
        if (tier.Value >= _state.RawSupply.Length)
        {
            _state.EnsureTierSlots(tier.Value + 1);
        }

        _state.RawSupply[tier.Value] = workers;
    }
}
