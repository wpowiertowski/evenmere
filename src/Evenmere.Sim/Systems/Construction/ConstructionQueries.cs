using Evenmere.Content;
using Evenmere.Content.Definitions;
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
    /// <remarks>
    /// An in-place upgrade also has a site: the building is then <c>Active</c> and its site shows the upgrade's progress.
    /// A monument site reports which stage is under construction and how many the monument has; every other site reports
    /// 0 for both. A monument's progress is the share of the whole build time that has elapsed, across stages.
    /// </remarks>
    public bool TryGetSite(EntityId building, out SiteInfo site)
    {
        if (!state.Sites.TryGet(building, out SiteRecord record))
        {
            site = default;
            return false;
        }

        BuildingDef target = content.Get(record.Target);
        long total = SiteMath.WorkTotal(target);
        int stageCount = MonumentStages.StageCount(content, record.Kind, record.Target);
        int progress = ProgressOf(total, stageCount, record);
        bool waitingForGood = record.Stall == StallReason.MissingMaterial;
        site = new SiteInfo(
            building,
            record.StepsDone,
            SiteMath.Steps,
            Ratio.FromPerMille(progress),
            record.Stall,
            waitingForGood ? record.WaitingFor : null,
            record.StageIndex,
            stageCount);
        return true;
    }

    /// <inheritdoc />
    public bool TryGetUpgradeTarget(EntityId building, out BuildingTypeId target)
    {
        if (state.Sites.TryGet(building, out SiteRecord record) && record.Kind == SiteKind.Upgrade)
        {
            target = record.Target;
            return true;
        }

        target = default;
        return false;
    }

    // A monument's progress spans its stages: the work of the finished stages plus the work done in this one.
    private static int ProgressOf(long total, int stageCount, SiteRecord record)
    {
        if (total == 0)
        {
            return 0;
        }

        long done = stageCount == 0
            ? record.Work
            : (total * record.StageIndex / stageCount) + record.Work;
        return (int)Math.Min(Ratio.One.PerMille, done * Ratio.One.PerMille / total);
    }
}
