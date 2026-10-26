namespace Evenmere.Game.Rendering.Monuments;

/// <summary>One placeholder tier box as the monument renderer draws it: its footprint share and height share.</summary>
/// <param name="FootprintShare">The side length as a share of the site footprint, in per-mille (1000 is the full footprint).</param>
/// <param name="HeightShare">The box height as a share of the finished monument's height, in per-mille.</param>
/// <param name="Built">Whether the stage is finished (solid) or still ahead (a faint ghost).</param>
public readonly record struct MonumentTierBox(int FootprintShare, int HeightShare, bool Built);

/// <summary>
/// The pure geometry behind the monument stage visuals (M2-27): how placeholder tiers grow by stage. It has no
/// Godot types, so the game test project exercises it on its own. Each finished stage is a solid tier; tiers step
/// inward as they rise, and the stage under construction grows from the last finished tier toward its full height
/// with the site's progress. Stages still ahead draw as a faint ghost of the finished silhouette.
/// </summary>
public static class MonumentStageLayout
{
    /// <summary>How much each higher tier steps inward, in per-mille of the footprint side per stage.</summary>
    public const int StepInPerStagePerMille = 120;

    /// <summary>The least footprint share, so a tall monument still reads at its crown, in per-mille.</summary>
    public const int MinFootprintPerMille = 280;

    /// <summary>
    /// Lays out the tier boxes for a monument site: one box per stage, in build order.
    /// </summary>
    /// <param name="stageCount">How many stages the monument has in all; at least 1.</param>
    /// <param name="stagesDone">How many stages are finished; 0 to <paramref name="stageCount"/>.</param>
    /// <param name="stageProgressPerMille">How far the stage under construction has come, in per-mille; 0 to 1000.</param>
    /// <returns>One box per stage, in build order.</returns>
    public static MonumentTierBox[] BoxesFor(int stageCount, int stagesDone, int stageProgressPerMille)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stageCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stagesDone, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stagesDone, stageCount);

        int progress = Math.Clamp(stageProgressPerMille, 0, 1000);
        int each = 1000 / stageCount;
        int rest = 1000 - (each * stageCount);
        var boxes = new MonumentTierBox[stageCount];
        for (int stage = 0; stage < stageCount; stage++)
        {
            int footprint = Math.Max(MinFootprintPerMille, 1000 - (StepInPerStagePerMille * stage));
            int full = each + (stage == stageCount - 1 ? rest : 0);
            bool built = stage < stagesDone;
            int height = built ? full : stage == stagesDone ? Math.Max(1, (full * progress) / 1000) : 0;
            boxes[stage] = new MonumentTierBox(footprint, height, built);
        }

        return boxes;
    }

    /// <summary>Gets the finished monument's full height in tier shares: always 1000, split across the stages.</summary>
    /// <param name="stageCount">How many stages the monument has in all; at least 1.</param>
    /// <returns>1000.</returns>
    public static int FullHeightPerMille(int stageCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stageCount, 1);

        int each = 1000 / stageCount;
        return (each * stageCount) + (1000 - (each * stageCount));
    }
}
