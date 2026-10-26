using Evenmere.Game.Rendering.Overlays;
using Godot;

namespace Evenmere.Game.Rendering.Monuments;

/// <summary>
/// The culture-moment site dressing (M2-27): placeholder tiers for a monument or wonder site that grow by stage,
/// and a bunting string over a festival ground while a festival runs. The tiers come from
/// <see cref="MonumentStageLayout"/>: each finished stage is a solid tier stepping inward as it rises, the stage
/// under construction grows with the site's progress, and the stages still ahead draw as faint ghosts. The ghosts
/// are translucent, so they render after the look post pass
/// (<see cref="OverlayRenderer.AfterLookPriority"/>), like the overlay sheet, or the default look would swallow them.
/// </summary>
public sealed partial class MonumentRenderer : Node3D
{
    /// <summary>How tall the finished monument reads, in world units.</summary>
    public const float FinishedHeight = 4f;

    /// <summary>How far above the ground the tiers sit, in world units.</summary>
    public const float Lift = 0.05f;

    /// <summary>How many pennants the bunting string carries.</summary>
    public const int PennantCount = 7;

    private static readonly Color _tierColour = new(0.72f, 0.70f, 0.66f);
    private static readonly Color _ghostColour = new(0.85f, 0.82f, 0.74f, 0.28f);
    private static readonly Color _poleColour = new(0.42f, 0.30f, 0.20f);
    private static readonly Color[] _pennantColours =
    [
        new Color(0.85f, 0.35f, 0.30f),
        new Color(0.95f, 0.75f, 0.35f),
        new Color(0.45f, 0.65f, 0.85f),
    ];

    private string _shownKey = string.Empty;

    /// <summary>
    /// Shows the monument tiers for a site, rebuilding only when the inputs change. A finished monument draws all
    /// tiers solid; a site draws the finished tiers solid, the working tier part-grown and the rest as ghosts.
    /// </summary>
    /// <param name="centre">The footprint centre in world coordinates (X, ground Y, Z).</param>
    /// <param name="footprintTiles">The footprint's longest side, in tiles.</param>
    /// <param name="stageCount">How many stages the monument has in all; at least 1.</param>
    /// <param name="stagesDone">How many stages are finished; 0 to <paramref name="stageCount"/>.</param>
    /// <param name="stageProgressPerMille">How far the working stage has come, in per-mille.</param>
    public void ShowMonument(Vector3 centre, float footprintTiles, int stageCount, int stagesDone, int stageProgressPerMille)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stageCount, 1);

        string key = $"m:{centre.X:F2}:{centre.Y:F2}:{centre.Z:F2}:{footprintTiles:F2}:{stageCount}:{stagesDone}:{stageProgressPerMille}";
        if (key == _shownKey)
        {
            return;
        }

        Clear();
        _shownKey = key;

        MonumentTierBox[] boxes = MonumentStageLayout.BoxesFor(stageCount, stagesDone, stageProgressPerMille);
        float raised = 0f;
        for (int stage = 0; stage < boxes.Length; stage++)
        {
            MonumentTierBox box = boxes[stage];
            float height = (box.HeightShare / 1000f) * FinishedHeight;
            if (height <= 0f)
            {
                continue;
            }

            float side = Math.Max(0.4f, footprintTiles * (box.FootprintShare / 1000f));
            var mesh = new BoxMesh { Size = new Vector3(side, height, side) };
            var instance = new MeshInstance3D
            {
                Mesh = mesh,
                Position = new Vector3(centre.X, centre.Y + Lift + raised + (height / 2f), centre.Z),
                MaterialOverride = box.Built ? SolidMaterial() : GhostMaterial(),
            };
            AddChild(instance);
            raised += height;
        }
    }

    /// <summary>
    /// Shows a bunting string over a festival ground: two poles with a sagging line of pennants between them,
    /// rebuilt only when the inputs change. Opaque, so it needs no look-pass priority.
    /// </summary>
    /// <param name="from">One footprint corner in world coordinates.</param>
    /// <param name="to">The opposite footprint corner in world coordinates.</param>
    public void ShowBunting(Vector3 from, Vector3 to)
    {
        string key = $"b:{from.X:F2}:{from.Y:F2}:{from.Z:F2}:{to.X:F2}:{to.Y:F2}:{to.Z:F2}";
        if (key == _shownKey)
        {
            return;
        }

        Clear();
        _shownKey = key;

        const float PoleHeight = 2.2f;
        var poleMesh = new BoxMesh { Size = new Vector3(0.08f, PoleHeight, 0.08f) };
        foreach (Vector3 foot in new[] { from, to })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = poleMesh,
                Position = new Vector3(foot.X, foot.Y + (PoleHeight / 2f), foot.Z),
                MaterialOverride = PoleMaterial(),
            });
        }

        Vector3 topA = new(from.X, from.Y + PoleHeight, from.Z);
        Vector3 topB = new(to.X, to.Y + PoleHeight, to.Z);
        for (int i = 0; i < PennantCount; i++)
        {
            float t = (i + 1f) / (PennantCount + 1f);
            Vector3 point = topA.Lerp(topB, t);
            point.Y -= MathF.Sin(t * MathF.PI) * 0.35f;
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.30f, 0.02f) },
                Position = point,
                MaterialOverride = PennantMaterial(i % _pennantColours.Length),
            });
        }
    }

    /// <summary>Hides the dressing.</summary>
    public void Clear()
    {
        _shownKey = string.Empty;
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    private static StandardMaterial3D SolidMaterial() => new()
    {
        AlbedoColor = _tierColour,
        Roughness = 0.95f,
    };

    private static StandardMaterial3D GhostMaterial() => new()
    {
        AlbedoColor = _ghostColour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        RenderPriority = OverlayRenderer.AfterLookPriority,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static StandardMaterial3D PoleMaterial() => new()
    {
        AlbedoColor = _poleColour,
        Roughness = 0.9f,
    };

    private static StandardMaterial3D PennantMaterial(int index) => new()
    {
        AlbedoColor = _pennantColours[index % _pennantColours.Length],
        Roughness = 0.8f,
    };
}
