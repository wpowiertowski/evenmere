using System.Numerics;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Services;

namespace Evenmere.Sim.Systems.Map;

/// <summary>
/// Remembers which 32 × 32 chunks changed since the host last drained them. One bit per chunk, row-major, so draining
/// walks the bits in ascending (Y, X) order and a run is the same on every machine. The marks are bookkeeping for the
/// view: they are not part of <see cref="MapState"/> and never reach a save or the state hash.
/// </summary>
internal sealed class ChunkDirtyTracker : IChunkDirtyTracker
{
    private ulong[] _bits = [];
    private int _width;
    private int _height;
    private int _chunksX;
    private int _chunksY;
    private int _dirtyCount;

    /// <summary>Gets the number of chunk columns.</summary>
    public int ChunksX => _chunksX;

    /// <summary>Gets the number of chunk rows.</summary>
    public int ChunksY => _chunksY;

    /// <summary>Gets the number of chunks currently marked.</summary>
    public int DirtyCount => _dirtyCount;

    /// <summary>Sizes the tracker for a map and clears every mark.</summary>
    /// <param name="width">The map width in tiles; 0 for no map.</param>
    /// <param name="height">The map height in tiles; 0 for no map.</param>
    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _chunksX = (width + ChunkCoord.TilesPerSide - 1) / ChunkCoord.TilesPerSide;
        _chunksY = (height + ChunkCoord.TilesPerSide - 1) / ChunkCoord.TilesPerSide;
        _bits = new ulong[((_chunksX * _chunksY) + 63) / 64];
        _dirtyCount = 0;
    }

    /// <summary>Marks every chunk of the map, so the view builds all of it.</summary>
    public void MarkAll()
    {
        int chunks = _chunksX * _chunksY;
        for (int index = 0; index < chunks; index++)
        {
            Set(index);
        }
    }

    /// <summary>Marks the chunk that holds a tile. A tile off the map is ignored.</summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    public void MarkTile(int x, int y) => MarkRect(x, y, x, y);

    /// <summary>Marks every chunk that a rectangle of tiles touches. The part of the rectangle off the map is ignored.</summary>
    /// <param name="minX">The first column, inclusive.</param>
    /// <param name="minY">The first row, inclusive.</param>
    /// <param name="maxX">The last column, inclusive.</param>
    /// <param name="maxY">The last row, inclusive.</param>
    public void MarkRect(int minX, int minY, int maxX, int maxY)
    {
        minX = Math.Max(minX, 0);
        minY = Math.Max(minY, 0);
        maxX = Math.Min(maxX, _width - 1);
        maxY = Math.Min(maxY, _height - 1);
        if (maxX < minX || maxY < minY)
        {
            return;
        }

        int firstX = minX / ChunkCoord.TilesPerSide;
        int firstY = minY / ChunkCoord.TilesPerSide;
        int lastX = maxX / ChunkCoord.TilesPerSide;
        int lastY = maxY / ChunkCoord.TilesPerSide;
        for (int cy = firstY; cy <= lastY; cy++)
        {
            for (int cx = firstX; cx <= lastX; cx++)
            {
                Set((cy * _chunksX) + cx);
            }
        }
    }

    /// <summary>Hands out marked chunks in ascending (Y, X) order and forgets exactly those.</summary>
    /// <param name="destination">Receives up to <c>destination.Length</c> chunks.</param>
    /// <returns>The number of chunks written.</returns>
    public int Drain(Span<ChunkCoord> destination)
    {
        int written = 0;
        for (int word = 0; word < _bits.Length && written < destination.Length && _dirtyCount > 0; word++)
        {
            ulong bits = _bits[word];
            while (bits != 0 && written < destination.Length)
            {
                int bit = BitOperations.TrailingZeroCount(bits);
                int index = (word * 64) + bit;
                destination[written++] = new ChunkCoord(index % _chunksX, index / _chunksX);
                bits &= bits - 1;
                _bits[word] &= ~(1UL << bit);
                _dirtyCount--;
            }
        }

        return written;
    }

    private void Set(int index)
    {
        ulong mask = 1UL << (index & 63);
        if ((_bits[index >> 6] & mask) == 0)
        {
            _bits[index >> 6] |= mask;
            _dirtyCount++;
        }
    }
}
