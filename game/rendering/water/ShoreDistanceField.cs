namespace Evenmere.Game.Rendering.Water;

/// <summary>
/// Bakes the shoreline distance field the sea shader samples for foam and depth tinting: a breadth-first march from
/// every land tile over water with 4-neighbourhood, capped at <see cref="MaxDistance"/>. It runs once at load, so it
/// allocates its output once and reuses a single queue. Tile order is ascending (y, x), which keeps the bake
/// deterministic.
/// </summary>
public static class ShoreDistanceField
{
    /// <summary>The distance cap in tiles; anything farther reads as open sea.</summary>
    public const int MaxDistance = 12;

    /// <summary>
    /// Computes the distance to the nearest land tile for every tile: 0 on land, 1 on the shoreline water, up to
    /// <see cref="MaxDistance"/> far out.
    /// </summary>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    /// <param name="isLand">Whether tile (x, y) is land (anything that is not open water or a river).</param>
    /// <returns>One distance byte per tile, row-major.</returns>
    public static byte[] Compute(int width, int height, Func<int, int, bool> isLand)
    {
        byte[] distance = new byte[width * height];
        int[] queue = new int[width * height];
        int head = 0;
        int tail = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (isLand(x, y))
                {
                    distance[(y * width) + x] = 0;
                    queue[tail++] = (y * width) + x;
                }
                else
                {
                    distance[(y * width) + x] = byte.MaxValue;
                }
            }
        }

        while (head < tail)
        {
            int current = queue[head++];
            int cx = current % width;
            int cy = current / width;
            byte next = (byte)Math.Min(distance[current] + 1, MaxDistance);
            if (next >= MaxDistance && distance[current] >= MaxDistance)
            {
                continue;
            }

            Visit(distance, queue, ref tail, width, height, cx - 1, cy, next);
            Visit(distance, queue, ref tail, width, height, cx + 1, cy, next);
            Visit(distance, queue, ref tail, width, height, cx, cy - 1, next);
            Visit(distance, queue, ref tail, width, height, cx, cy + 1, next);
        }

        for (int i = 0; i < distance.Length; i++)
        {
            if (distance[i] == byte.MaxValue)
            {
                distance[i] = MaxDistance;
            }
        }

        return distance;
    }

    private static void Visit(byte[] distance, int[] queue, ref int tail, int width, int height, int x, int y, byte next)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        int i = (y * width) + x;
        if (next < distance[i])
        {
            distance[i] = next;
            queue[tail++] = i;
        }
    }
}
