using Godot;

namespace Evenmere.Game.Ui.Tools;

/// <summary>
/// One home for every tool tint, so the ghost, the road preview, the demolish highlight and the build menu (M1-23)
/// all speak the same calm language: pale cyan-white means "this will work", amber means "this will work with a
/// caveat", and red means "this will not work". Bodies are translucent so the terrain reads through them; the
/// footprint outline is opaque and bright so it reads on grass, forest, sand and hills alike.
/// </summary>
public static class ToolColours
{
    /// <summary>Pale cyan-white translucent body for a placement with no errors and no warnings.</summary>
    public static readonly Color GhostOk = new(0.82f, 0.96f, 1f, 0.5f);

    /// <summary>Amber translucent body for a placement with warnings but no errors.</summary>
    public static readonly Color GhostWarn = new(1f, 0.76f, 0.32f, 0.55f);

    /// <summary>Red translucent body for a placement with errors.</summary>
    public static readonly Color GhostError = new(1f, 0.32f, 0.3f, 0.55f);

    /// <summary>Bright opaque outline for a clean placement.</summary>
    public static readonly Color OutlineOk = new(0.88f, 1f, 1f);

    /// <summary>Bright opaque outline for a warned placement.</summary>
    public static readonly Color OutlineWarn = new(1f, 0.68f, 0.2f);

    /// <summary>Bright opaque outline for a refused placement.</summary>
    public static readonly Color OutlineError = new(1f, 0.25f, 0.25f);

    /// <summary>Pale cyan-white road preview tile: clearly a preview, never a built road.</summary>
    public static readonly Color RoadOk = new(0.82f, 0.96f, 1f);

    /// <summary>Red road preview tile: this part of the path will not build.</summary>
    public static readonly Color RoadError = new(1f, 0.25f, 0.25f);

    /// <summary>Yellow translucent fill for a demolish target.</summary>
    public static readonly Color DemolishOk = new(1f, 0.9f, 0.4f, 0.35f);

    /// <summary>Red translucent fill for a protected target.</summary>
    public static readonly Color DemolishBad = new(1f, 0.3f, 0.3f, 0.45f);
}
