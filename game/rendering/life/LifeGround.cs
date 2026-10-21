using Evenmere.Core.Ids;
using Evenmere.Game.Data.Visuals;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Godot;

namespace Evenmere.Game.Rendering.Life;

/// <summary>
/// The rendered height of the road surface the carts ride and the walkers stroll. Roads sit on the faceted corner
/// heights plus <see cref="Roads.RoadMeshBuilder.Lift"/>, so sampling the same corners with the same lift lands carts
/// on the visible strips. It mirrors <see cref="GroundSampler"/> without depending on the terrain renderer.
/// </summary>
internal static class LifeGround
{
    /// <summary>How far above the road strips carts and walkers float, in world units.</summary>
    public const float RideHeight = 0.02f;

    /// <summary>
    /// Gets the road-surface height at a fractional tile position, in world units.
    /// </summary>
    /// <param name="map">The map queries.</param>
    /// <param name="content">The content, for terrain names.</param>
    /// <param name="tileX">The fractional column (tile X + 0.5 is the tile centre).</param>
    /// <param name="tileZ">The fractional row.</param>
    /// <returns>The bilinear corner height plus the road lift and <see cref="RideHeight"/>.</returns>
    public static float RoadHeight(IMapQueries map, Evenmere.Content.ContentDb content, float tileX, float tileZ)
    {
        int x0 = (int)MathF.Floor(tileX);
        int y0 = (int)MathF.Floor(tileZ);
        float fx = tileX - x0;
        float fy = tileZ - y0;

        float h00 = GroundSampler.CornerHeight(map, content, x0, y0);
        float h10 = GroundSampler.CornerHeight(map, content, x0 + 1, y0);
        float h01 = GroundSampler.CornerHeight(map, content, x0, y0 + 1);
        float h11 = GroundSampler.CornerHeight(map, content, x0 + 1, y0 + 1);

        float top = h00 + ((h10 - h00) * fx);
        float bottom = h01 + ((h11 - h01) * fx);
        return top + ((bottom - top) * fy) + Roads.RoadMeshBuilder.Lift + RideHeight;
    }

    /// <summary>
    /// Gets the centre of a building's footprint in world coordinates.
    /// </summary>
    /// <param name="building">The building.</param>
    /// <returns>The (x, z) centre in world units (one unit per tile).</returns>
    public static (float X, float Z) FootprintCentre(BuildingInfo building) =>
        (building.Origin.X + (building.Width / 2f), building.Origin.Y + (building.Height / 2f));

    /// <summary>
    /// Turns a walk along a road path into a world position. Progress is spread evenly over the path's segments, so a
    /// cart moves at a steady visual speed whatever the path's shape.
    /// </summary>
    /// <param name="path">The road tiles from the walk's start to its end; at least two entries.</param>
    /// <param name="progress">How far along the walk, from 0 (start) to 1 (end); clamped.</param>
    /// <param name="tileX">The fractional column of the walker's feet.</param>
    /// <param name="tileZ">The fractional row.</param>
    /// <param name="yaw">The facing in radians (0 faces -Z, like the camera rig's yaw).</param>
    public static void WalkOnPath(List<TileCoord> path, float progress, out float tileX, out float tileZ, out float yaw)
    {
        float clamped = Math.Clamp(progress, 0f, 1f);
        float distance = clamped * (path.Count - 1);
        int segment = Math.Min((int)distance, path.Count - 2);
        float fraction = distance - segment;

        TileCoord a = path[segment];
        TileCoord b = path[segment + 1];
        tileX = (a.X + 0.5f) + ((b.X - a.X) * fraction);
        tileZ = (a.Y + 0.5f) + ((b.Y - a.Y) * fraction);

        // Godot turns about +Y by yaw face the forward (-Z) toward (-sin yaw, -cos yaw), so facing a step
        // (dx, dz) means yaw = atan2(-dx, -dz). Only axis steps occur on road paths.
        yaw = YawToward(0f, 0f, b.X - a.X, b.Y - a.Y);
    }

    /// <summary>
    /// The yaw that faces from one world point toward another: Godot's +Y turn faces -Z at 0, so facing
    /// (dx, dz) is atan2(-dx, -dz).
    /// </summary>
    /// <param name="fromX">The start X.</param>
    /// <param name="fromZ">The start Z.</param>
    /// <param name="toX">The target X.</param>
    /// <param name="toZ">The target Z.</param>
    /// <returns>The facing in radians; 0 when both points coincide.</returns>
    public static float YawToward(float fromX, float fromZ, float toX, float toZ)
    {
        float dx = toX - fromX;
        float dz = toZ - fromZ;
        return (dx == 0f && dz == 0f) ? 0f : MathF.Atan2(-dx, -dz);
    }
}
