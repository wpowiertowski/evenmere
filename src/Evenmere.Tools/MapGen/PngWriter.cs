namespace Evenmere.Tools.MapGen;

/// <summary>
/// A minimal PNG writer: 8-bit truecolor (color type 2), one filter byte of zero per scanline, and the image data
/// stored (uncompressed) in a zlib stream. Compression would need a DEFLATE compressor; stored blocks keep the writer
/// dependency-free (AGENTS.md forbids unnamed packages) while staying valid PNG. Galleries are a few megabytes, which
/// is fine for a developer preview tool.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] _signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] _crcTable = BuildCrcTable();

    /// <summary>Encodes an RGB image as a PNG.</summary>
    /// <param name="width">The width in pixels; at least 1.</param>
    /// <param name="height">The height in pixels; at least 1.</param>
    /// <param name="rgb">The pixels top-row first, three bytes (R, G, B) per pixel.</param>
    /// <returns>The PNG file bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is below 1.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rgb"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="rgb"/> is not exactly <c>width × height × 3</c> bytes.</exception>
    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(rgb);
        if (rgb.Length != width * height * 3)
        {
            throw new ArgumentException($"RGB data is {rgb.Length} bytes but {width}x{height} needs {width * height * 3}.", nameof(rgb));
        }

        byte[] raw = new byte[height * (1 + (width * 3))];
        for (int y = 0; y < height; y++)
        {
            raw[y * (1 + (width * 3))] = 0;
            Buffer.BlockCopy(rgb, y * width * 3, raw, (y * (1 + (width * 3))) + 1, width * 3);
        }

        byte[] zlib = ZlibStored(raw);

        using var png = new MemoryStream();
        png.Write(_signature);
        WriteChunk(png, "IHDR", IhdrData(width, height));
        WriteChunk(png, "IDAT", zlib);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>Computes the PNG CRC (IEEE CRC-32) of a chunk's type and data.</summary>
    /// <param name="type">The four ASCII chunk-type bytes.</param>
    /// <param name="data">The chunk data.</param>
    /// <returns>The CRC value.</returns>
    public static uint Crc32(byte[] type, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(data);

        uint crc = 0xFFFFFFFFu;
        foreach (byte b in type)
        {
            crc = (crc >> 8) ^ _crcTable[(crc ^ b) & 0xFF];
        }

        foreach (byte b in data)
        {
            crc = (crc >> 8) ^ _crcTable[(crc ^ b) & 0xFF];
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] IhdrData(int width, int height)
    {
        byte[] data = new byte[13];
        WriteBigEndian(data, 0, (uint)width);
        WriteBigEndian(data, 4, (uint)height);
        data[8] = 8;
        data[9] = 2;
        return data;
    }

    // A zlib stream holding only stored (uncompressed) DEFLATE blocks: header 0x78 0x01, then blocks of at most
    // 65535 bytes with LEN/NLEN pairs, then the Adler-32 of the raw data. Deterministic by construction.
    private static byte[] ZlibStored(byte[] raw)
    {
        using var zlib = new MemoryStream();
        zlib.WriteByte(0x78);
        zlib.WriteByte(0x01);

        int at = 0;
        while (at < raw.Length)
        {
            int take = Math.Min(65535, raw.Length - at);
            bool last = at + take == raw.Length;
            zlib.WriteByte((byte)(last ? 1 : 0));
            zlib.WriteByte((byte)(take & 0xFF));
            zlib.WriteByte((byte)((take >> 8) & 0xFF));
            zlib.WriteByte((byte)(~take & 0xFF));
            zlib.WriteByte((byte)((~take >> 8) & 0xFF));
            zlib.Write(raw, at, take);
            at += take;
        }

        uint adler = Adler32(raw);
        zlib.WriteByte((byte)((adler >> 24) & 0xFF));
        zlib.WriteByte((byte)((adler >> 16) & 0xFF));
        zlib.WriteByte((byte)((adler >> 8) & 0xFF));
        zlib.WriteByte((byte)(adler & 0xFF));
        return zlib.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint Mod = 65521;
        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % Mod;
            b = (b + a) % Mod;
        }

        return (b << 16) | a;
    }

    private static void WriteChunk(MemoryStream png, string type, byte[] data)
    {
        byte[] typeBytes = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        png.Write(length);
        png.Write(typeBytes);
        png.Write(data);
        byte[] crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typeBytes, data));
        png.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
