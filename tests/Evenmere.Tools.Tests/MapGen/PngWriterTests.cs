using Evenmere.Tools.MapGen;

namespace Evenmere.Tools.Tests.MapGen;

/// <summary>
/// Unit tests for the in-house PNG writer: the signature and chunk layout, CRCs against the standard test vector,
/// and a full round trip through a decoder written in the test (no new packages allowed, so the test decodes by hand).
/// </summary>
public sealed class PngWriterTests
{
    [Fact]
    public void Crc32_StandardVector_MatchesKnownValue()
    {
        byte[] type = [(byte)'1', (byte)'2', (byte)'3', (byte)'4'];
        byte[] data = [(byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9'];

        // The CRC of the ASCII string "123456789" split across type and data.
        PngWriter.Crc32(type, data).ShouldBe(0xCBF43926u);
    }

    [Fact]
    public void Encode_SingleRedPixel_HasSignatureAndThreeChunks()
    {
        byte[] png = PngWriter.Encode(1, 1, [0xFF, 0x00, 0x00]);

        png[..8].ShouldBe([137, 80, 78, 71, 13, 10, 26, 10]);
        List<(string Type, byte[] Data)> chunks = ReadChunks(png);
        chunks.Count.ShouldBe(3);
        chunks[0].Type.ShouldBe("IHDR");
        chunks[1].Type.ShouldBe("IDAT");
        chunks[2].Type.ShouldBe("IEND");
        chunks[2].Data.ShouldBeEmpty();

        byte[] ihdr = chunks[0].Data;
        ihdr.Length.ShouldBe(13);
        ReadBigEndian(ihdr, 0).ShouldBe(1u);
        ReadBigEndian(ihdr, 4).ShouldBe(1u);
        ihdr[8].ShouldBe((byte)8);
        ihdr[9].ShouldBe((byte)2);
    }

    [Fact]
    public void Encode_BadArguments_Throw()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PngWriter.Encode(0, 1, [0, 0, 0]));
        Should.Throw<ArgumentOutOfRangeException>(() => PngWriter.Encode(1, 0, [0, 0, 0]));
        Should.Throw<ArgumentNullException>(() => PngWriter.Encode(1, 1, null!));
        Should.Throw<ArgumentException>(() => PngWriter.Encode(2, 2, [0, 0, 0]));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(17, 5)]
    [InlineData(300, 80)]
    public void Encode_RoundTripThroughTestDecoder_RestoresPixels(int width, int height)
    {
        // 300x80 carries 72,080 raw bytes, past one 65,535-byte stored block, so the multi-block path is covered.
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)((i * 31) + 7);
        }

        byte[] png = PngWriter.Encode(width, height, rgb);

        Decode(png, width, height).ShouldBe(rgb);
    }

    private static List<(string Type, byte[] Data)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string Type, byte[] Data)>();
        int at = 8;
        while (at < png.Length)
        {
            uint length = ReadBigEndian(png, at);
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            byte[] data = png.Skip(at + 8).Take((int)length).ToArray();
            uint stored = ReadBigEndian(png, (int)(at + 8 + length));
            CrcBitByBit(png.Skip(at + 4).Take((int)(4 + length)).ToArray()).ShouldBe(stored, $"chunk {type} CRC");
            chunks.Add((type, data));
            at = (int)(at + 12 + length);
        }

        return chunks;
    }

    // A bit-by-bit CRC (no shared table with the writer) so the test checks the value, not the implementation.
    private static uint CrcBitByBit(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    // Decodes the writer's output without touching its code: checks the zlib header, stored blocks and Adler-32,
// then unapplies the per-scanline filter (only filter 0 is ever written).
    private static byte[] Decode(byte[] png, int width, int height)
    {
        List<(string Type, byte[] Data)> chunks = ReadChunks(png);
        byte[] idat = chunks.Where(static c => c.Type == "IDAT").SelectMany(static c => c.Data).ToArray();

        idat[0].ShouldBe((byte)0x78);
        idat[1].ShouldBe((byte)0x01);
        var raw = new List<byte>();
        int at = 2;
        bool last = false;
        while (!last)
        {
            last = (idat[at] & 1) != 0;
            (idat[at] & 0xFE).ShouldBe((byte)0, "only stored blocks are written");
            int len = idat[at + 1] | (idat[at + 2] << 8);
            int nlen = idat[at + 3] | (idat[at + 4] << 8);
            (len ^ nlen).ShouldBe(0xFFFF, "LEN/NLEN complement");
            raw.AddRange(idat.Skip(at + 5).Take(len));
            at += 5 + len;
        }

        uint adler = ((uint)idat[at] << 24) | ((uint)idat[at + 1] << 16) | ((uint)idat[at + 2] << 8) | idat[at + 3];
        AdlerBitByBit(raw.ToArray()).ShouldBe(adler);
        (at + 4).ShouldBe(idat.Length, "no trailing zlib bytes");

        int stride = 1 + (width * 3);
        raw.Count.ShouldBe(height * stride);
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            raw[y * stride].ShouldBe((byte)0, $"scanline {y} filter");
            Buffer.BlockCopy(raw.ToArray(), (y * stride) + 1, rgb, y * width * 3, width * 3);
        }

        return rgb;
    }

    private static uint AdlerBitByBit(byte[] data)
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

    private static uint ReadBigEndian(byte[] buffer, int offset) =>
        ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
}
