using System.Buffers;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace Evenmere.Core.Hashing;

/// <summary>
/// An incremental XxHash64 (seed 0) that feeds every value in a fixed byte layout: integers are little-endian
/// whatever the platform, and strings are UTF-8. The same sequence of calls therefore gives the same hash on
/// every machine, which the state hash and golden scenario tests rely on (ADR-0003).
/// </summary>
public sealed class StableHasher
{
    private const int StackLimit = 256;

    private readonly XxHash64 _hash = new();

    /// <summary>Appends a 32-bit integer as four little-endian bytes.</summary>
    /// <param name="value">The value to append.</param>
    public void Add(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _hash.Append(bytes);
    }

    /// <summary>Appends a 64-bit integer as eight little-endian bytes.</summary>
    /// <param name="value">The value to append.</param>
    public void Add(long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        _hash.Append(bytes);
    }

    /// <summary>Appends an unsigned 64-bit integer as eight little-endian bytes.</summary>
    /// <param name="value">The value to append.</param>
    public void Add(ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _hash.Append(bytes);
    }

    /// <summary>
    /// Appends raw bytes with no length prefix, so adding a buffer in one call or in several pieces gives the
    /// same hash, and hashing a whole buffer matches <see cref="XxHash64.HashToUInt64(ReadOnlySpan{byte}, long)"/>.
    /// </summary>
    /// <param name="bytes">The bytes to append.</param>
    public void Add(ReadOnlySpan<byte> bytes) => _hash.Append(bytes);

    /// <summary>
    /// Appends a string as its UTF-8 byte count (four little-endian bytes) followed by the UTF-8 bytes, so
    /// <c>"ab" + "c"</c> and <c>"a" + "bc"</c> hash differently.
    /// </summary>
    /// <param name="value">The string to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public void Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Add(byteCount);
        AddUtf8(value, byteCount);
    }

    /// <summary>Gets the hash of everything appended so far. The hasher stays usable, so more values can follow.</summary>
    /// <returns>The 64-bit hash.</returns>
    public ulong ToUInt64() => _hash.GetCurrentHashAsUInt64();

    /// <summary>Discards everything appended so far.</summary>
    public void Reset() => _hash.Reset();

    /// <summary>Appends the UTF-8 bytes of a string with no length prefix. Used where the string alone is the whole input.</summary>
    /// <param name="value">The string to append.</param>
    internal void AddUtf8(string value) => AddUtf8(value, Encoding.UTF8.GetByteCount(value));

    private void AddUtf8(string value, int byteCount)
    {
        if (byteCount == 0)
        {
            return;
        }

        if (byteCount <= StackLimit)
        {
            Span<byte> small = stackalloc byte[StackLimit];
            int written = Encoding.UTF8.GetBytes(value, small);
            _hash.Append(small[..written]);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            int written = Encoding.UTF8.GetBytes(value, rented);
            _hash.Append(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
