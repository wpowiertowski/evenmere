namespace Evenmere.Game.Rendering.Effects;

/// <summary>
/// The chimney-smoke budget (M2-24): a hard cap on emitters and particles that thins with zoom, so smoke stays
/// cheap at every distance. Pure and Godot-free for unit tests; the renderer assigns real emitters from it.
/// </summary>
public static class SmokeBudget
{
    /// <summary>How many chimney emitters may exist at once, at street zoom.</summary>
    public const int MaxEmitters = 40;

    /// <summary>How many live puffs one chimney holds: a wisp, not a column (3-5).</summary>
    public const int PuffsPerEmitter = 4;

    /// <summary>The hard particle ceiling: emitters x puffs, 160.</summary>
    public const int TotalBudget = MaxEmitters * PuffsPerEmitter;

    /// <summary>Past this zoom smoke hides entirely (matches the life layer's far zoom).</summary>
    public const float FarZoom = 120f;

    /// <summary>Past this zoom (but below <see cref="FarZoom"/>) smoke thins to half the emitters.</summary>
    public const float MidZoom = 60f;

    /// <summary>How often the renderer re-reads the active buildings, in seconds.</summary>
    public const float RefreshSeconds = 0.5f;

    /// <summary>How high above a building's pad its chimney smokes, in world units.</summary>
    public const float ChimneyHeight = 1.3f;

    /// <summary>
    /// Caps the emitter count by the budget and the zoom: full strength at street zoom, half past
    /// <see cref="MidZoom"/>, none past <see cref="FarZoom"/>. The caller passes active production buildings in
    /// ascending ID order and takes the first N, so the choice is deterministic.
    /// </summary>
    /// <param name="activeProductionCount">How many active production buildings the island holds.</param>
    /// <param name="zoom">The camera zoom.</param>
    /// <returns>How many emitters to show, 0 to <see cref="MaxEmitters"/>.</returns>
    public static int ActiveEmitterCount(int activeProductionCount, float zoom)
    {
        if (activeProductionCount <= 0 || zoom > FarZoom)
        {
            return 0;
        }

        int capped = Math.Min(activeProductionCount, MaxEmitters);
        return zoom > MidZoom ? capped / 2 : capped;
    }

    /// <summary>Gets the live particle count for an emitter count.</summary>
    /// <param name="emitters">The emitter count from <see cref="ActiveEmitterCount"/>.</param>
    /// <returns>Emitters × puffs, never above <see cref="TotalBudget"/>.</returns>
    public static int ParticleCount(int emitters) => Math.Clamp(emitters, 0, MaxEmitters) * PuffsPerEmitter;
}
