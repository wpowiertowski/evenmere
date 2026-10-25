namespace Evenmere.Sim.Persistence;

/// <summary>
/// The save file layout (ADR-0007): the ASCII magic <c>EVMR</c>, a <c>uint16</c> format version, a length-prefixed
/// MessagePack header, then the LZ4-compressed body, each section length-prefixed with a little-endian
/// <c>int32</c> so a reader never has to guess where a section ends.
/// </summary>
public static class SaveFormat
{
    /// <summary>Gets the current format version, the only version this code writes: 2 since M2-15.</summary>
    public const ushort CurrentVersion = 2;

    /// <summary>Gets the file magic, the ASCII bytes <c>EVMR</c>.</summary>
    public static ReadOnlySpan<byte> Magic => "EVMR"u8;

    /// <summary>Reads a little-endian <c>int32</c> length prefix.</summary>
    /// <param name="data">The bytes, at least four long.</param>
    /// <returns>The length.</returns>
    internal static int ReadLengthPrefix(ReadOnlySpan<byte> data) =>
        data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);

    /// <summary>Writes a little-endian <c>int32</c> length prefix.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="length">The length, zero or more.</param>
    internal static void WriteLengthPrefix(Stream stream, int length)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Span<byte> prefix = stackalloc byte[4];
        prefix[0] = (byte)length;
        prefix[1] = (byte)(length >> 8);
        prefix[2] = (byte)(length >> 16);
        prefix[3] = (byte)(length >> 24);
        stream.Write(prefix);
    }

    /// <summary>Reads exactly the bytes asked for, for streams that may return short reads.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="data">The buffer to fill.</param>
    /// <exception cref="InvalidDataException">The stream ends first.</exception>
    internal static void ReadExactly(Stream stream, Span<byte> data)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int filled = 0;
        while (filled < data.Length)
        {
            int read = stream.Read(data[filled..]);
            if (read == 0)
            {
                throw new InvalidDataException("The save ends in the middle of a section.");
            }

            filled += read;
        }
    }
}
