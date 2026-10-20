using System.Numerics;

namespace Evenmere.Game.Rendering.Camera;

/// <summary>
/// Turns a ray into the tile it first touches. The ground is modelled as a column of height <c>heightAt(x, y)</c> over
/// every tile, and the ray is marched across tile boundaries (a grid traversal, so no tile is skipped and no step size
/// has to be tuned). With flat ground this is exactly the ground-plane intersection. It has no Godot types so it can be
/// unit tested on its own.
/// </summary>
public static class TilePicker
{
    /// <summary>How far below a tile's top, in world units, the ray may be and still count as touching it. It absorbs floating-point error at the exact surface.</summary>
    public const double HitTolerance = 1e-6;

    /// <summary>
    /// Finds the first tile a ray reaches on its way down through the terrain.
    /// </summary>
    /// <param name="origin">The ray origin, in world coordinates.</param>
    /// <param name="direction">The ray direction; it need not be normalised but must point downward to hit anything.</param>
    /// <param name="bounds">The map size; tiles outside it are never hit.</param>
    /// <param name="heightAt">The ground height of tile (x, y) in world units. It is only called for tiles on the map.</param>
    /// <param name="minHeight">The lowest ground height the terrain can have (sea level, normally 0). The ray is not followed below it.</param>
    /// <param name="maxHeight">The highest ground height the terrain can have. It bounds where the march starts, so a larger value costs more steps but never gives a wrong answer.</param>
    /// <param name="tileX">The tile X when the method returns <see langword="true"/>.</param>
    /// <param name="tileY">The tile Y (world Z) when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the ray touches a tile on the map.</returns>
    public static bool TryPick(
        Vector3 origin,
        Vector3 direction,
        MapBounds bounds,
        Func<int, int, float> heightAt,
        float minHeight,
        float maxHeight,
        out int tileX,
        out int tileY)
    {
        tileX = 0;
        tileY = 0;

        // Doubles keep the arithmetic exact enough to land on tile boundaries; the inputs are floats.
        double ox = origin.X, oy = origin.Y, oz = origin.Z;
        double dx = direction.X, dy = direction.Y, dz = direction.Z;
        double width = bounds.WidthTiles * (double)MapBounds.WorldUnitsPerTile;
        double depth = bounds.HeightTiles * (double)MapBounds.WorldUnitsPerTile;
        double floor = Math.Min(minHeight, maxHeight);
        double ceiling = Math.Max(minHeight, maxHeight);

        // Only a ray that descends can reach the ground. The negated comparison also rejects NaN.
        if (!(dy < -1e-12))
        {
            return false;
        }

        // The part of the ray that can touch terrain lies between the ceiling plane and the floor plane.
        double tStart = Math.Max(0.0, (ceiling - oy) / dy);
        double tEnd = (floor - oy) / dy;
        if (tEnd < tStart)
        {
            return false;
        }

        // Clip that part to the map footprint.
        if (!ClipToSlab(ox, dx, 0.0, width, ref tStart, ref tEnd) || !ClipToSlab(oz, dz, 0.0, depth, ref tStart, ref tEnd))
        {
            return false;
        }

        int ix = Math.Clamp((int)Math.Floor(ox + (dx * tStart)), 0, bounds.WidthTiles - 1);
        int iz = Math.Clamp((int)Math.Floor(oz + (dz * tStart)), 0, bounds.HeightTiles - 1);

        int stepX = Math.Sign(dx);
        int stepZ = Math.Sign(dz);
        double tDeltaX = stepX == 0 ? double.PositiveInfinity : 1.0 / Math.Abs(dx);
        double tDeltaZ = stepZ == 0 ? double.PositiveInfinity : 1.0 / Math.Abs(dz);
        double tMaxX = stepX == 0 ? double.PositiveInfinity : ((stepX > 0 ? ix + 1 : ix) - ox) / dx;
        double tMaxZ = stepZ == 0 ? double.PositiveInfinity : ((stepZ > 0 ? iz + 1 : iz) - oz) / dz;

        double tEnter = tStart;

        // A ray crosses at most width + depth tile boundaries; the cap only guards against a bug turning into a hang.
        int guard = bounds.WidthTiles + bounds.HeightTiles + 4;
        while (guard-- > 0)
        {
            double tExit = Math.Max(tEnter, Math.Min(Math.Min(tMaxX, tMaxZ), tEnd));

            // The ray only descends, so its lowest point in this tile is where it leaves. If that is at or below the
            // tile's top, the ray either came in through the side or came down through the top face: a hit either way.
            double yExit = oy + (dy * tExit);
            if (yExit <= heightAt(ix, iz) + HitTolerance)
            {
                tileX = ix;
                tileY = iz;
                return true;
            }

            if (tExit >= tEnd)
            {
                return false;
            }

            // Step into the neighbour the ray crosses into. On an exact corner both axes advance together, so the ray
            // never "visits" a tile it only grazes at a point.
            bool advanceX = tMaxX <= tMaxZ;
            bool advanceZ = tMaxZ <= tMaxX;
            if (advanceX)
            {
                ix += stepX;
                tMaxX += tDeltaX;
            }

            if (advanceZ)
            {
                iz += stepZ;
                tMaxZ += tDeltaZ;
            }

            tEnter = tExit;
            if (!bounds.Contains(ix, iz))
            {
                return false;
            }
        }

        return false;
    }

    private static bool ClipToSlab(double origin, double direction, double low, double high, ref double tStart, ref double tEnd)
    {
        if (Math.Abs(direction) < 1e-12)
        {
            return origin >= low && origin <= high;
        }

        double t1 = (low - origin) / direction;
        double t2 = (high - origin) / direction;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        tStart = Math.Max(tStart, t1);
        tEnd = Math.Min(tEnd, t2);
        return tStart <= tEnd;
    }
}
