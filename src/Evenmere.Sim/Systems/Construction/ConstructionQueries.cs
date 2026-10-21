using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Systems.Construction;

/// <summary>Implements <see cref="IConstructionQueries"/> over Construction's own state.</summary>
/// <param name="state">The construction state.</param>
/// <param name="content">The content, whose build times turn work into a progress share.</param>
internal sealed class ConstructionQueries(ConstructionState state, ContentDb content) : IConstructionQueries
{
    /// <inheritdoc />
    /// <remarks>An in-place upgrade also has a site: the building is then <c>Active</c> and its site shows the upgrade's progress.</remarks>
    public bool TryGetSite(EntityId building, out SiteInfo site)
    {
        if (!state.Sites.TryGet(building, out SiteRecord record))
        {
            site = default;
            return false;
        }

        long total = SiteMath.WorkTotal(content.Get(record.Target));
        int progress = total == 0 ? 0 : (int)Math.Min(Ratio.One.PerMille, record.Work * Ratio.One.PerMille / total);
        bool waitingForGood = record.Stall == StallReason.MissingMaterial;
        site = new SiteInfo(
            building,
            record.StepsDone,
            SiteMath.Steps,
            Ratio.FromPerMille(progress),
            record.Stall,
            waitingForGood ? record.WaitingFor : null);
        return true;
    }
}
