using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using Evenmere.Core.Hashing;

namespace Evenmere.Core.Tests.Hashing;

public sealed class StableHasherTests
{
    [Fact]
    public void ToUInt64_NothingAdded_IsXxHash64OfEmptyInput()
    {
        new StableHasher().ToUInt64().ShouldBe(0xEF46DB3751D8E999UL);
    }

    [Theory]
    [InlineData("a", 0xD24EC4F1A98C6E5BUL)]
    [InlineData("abc", 0x44BC2CF5AD770999UL)]
    public void Add_Bytes_MatchesPublishedXxHash64Vectors(string ascii, ulong expected)
    {
        StableHasher hasher = new();

        hasher.Add(Encoding.ASCII.GetBytes(ascii));

        hasher.ToUInt64().ShouldBe(expected);
    }

    [Fact]
    public void Add_BytesInPieces_EqualsOneCall()
    {
        byte[] data = Enumerable.Range(0, 1_000).Select(i => (byte)(i * 31)).ToArray();
        StableHasher whole = new();
        StableHasher pieces = new();

        whole.Add(data);
        pieces.Add(data.AsSpan(0, 1));
        pieces.Add(data.AsSpan(1, 63));
        pieces.Add(data.AsSpan(64, 500));
        pieces.Add(data.AsSpan(564));

        pieces.ToUInt64().ShouldBe(whole.ToUInt64());
        whole.ToUInt64().ShouldBe(XxHash64.HashToUInt64(data));
    }

    [Fact]
    public void Add_Int_IsFourLittleEndianBytes()
    {
        StableHasher hasher = new();
        hasher.Add(0x01020304);

        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64([0x04, 0x03, 0x02, 0x01]));
    }

    [Fact]
    public void Add_NegativeInt_IsTwosComplementLittleEndian()
    {
        StableHasher hasher = new();
        hasher.Add(-2);

        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64([0xFE, 0xFF, 0xFF, 0xFF]));
    }

    [Fact]
    public void Add_Long_IsEightLittleEndianBytes()
    {
        StableHasher hasher = new();
        hasher.Add(0x0102030405060708L);

        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64([0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01]));
    }

    [Fact]
    public void Add_ULong_IsEightLittleEndianBytes()
    {
        StableHasher hasher = new();
        hasher.Add(0xFF00000000000001UL);

        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64([0x01, 0, 0, 0, 0, 0, 0, 0xFF]));
    }

    [Fact]
    public void Add_LongAndULongWithSameBits_HashTheSame()
    {
        StableHasher signed = new();
        StableHasher unsigned = new();

        signed.Add(-1L);
        unsigned.Add(ulong.MaxValue);

        signed.ToUInt64().ShouldBe(unsigned.ToUInt64());
    }

    [Fact]
    public void Add_String_IsUtf8ByteCountThenUtf8Bytes()
    {
        StableHasher hasher = new();
        hasher.Add("abc");

        byte[] expected = [3, 0, 0, 0, (byte)'a', (byte)'b', (byte)'c'];
        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64(expected));
    }

    [Fact]
    public void Add_NonAsciiString_PrefixesByteCountNotCharCount()
    {
        StableHasher hasher = new();
        hasher.Add("café");

        byte[] expected = [5, 0, 0, 0, (byte)'c', (byte)'a', (byte)'f', 0xC3, 0xA9];
        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64(expected));
    }

    [Fact]
    public void Add_EmptyString_AddsOnlyAZeroLength()
    {
        StableHasher hasher = new();
        hasher.Add(string.Empty);

        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64([0, 0, 0, 0]));
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(5_000)]
    public void Add_StringAroundStackLimit_MatchesManualEncoding(int length)
    {
        string text = new string('q', length - 1) + "€";
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        byte[] prefixed = new byte[4 + utf8.Length];
        BinaryPrimitives.WriteInt32LittleEndian(prefixed, utf8.Length);
        utf8.CopyTo(prefixed, 4);

        StableHasher hasher = new();
        hasher.Add(text);

        hasher.ToUInt64().ShouldBe(XxHash64.HashToUInt64(prefixed));
    }

    [Fact]
    public void Add_AdjacentStrings_DifferWhenSplitDifferently()
    {
        StableHasher abThenC = new();
        abThenC.Add("ab");
        abThenC.Add("c");
        StableHasher aThenBc = new();
        aThenBc.Add("a");
        aThenBc.Add("bc");

        abThenC.ToUInt64().ShouldNotBe(aThenBc.ToUInt64());
    }

    [Fact]
    public void Add_NullString_ThrowsArgumentNullException()
    {
        StableHasher hasher = new();

        Should.Throw<ArgumentNullException>(() => hasher.Add((string)null!));
    }

    [Fact]
    public void Add_MixedValues_IsOrderSensitiveAndRepeatable()
    {
        ulong First()
        {
            StableHasher h = new();
            h.Add(1);
            h.Add(2L);
            h.Add(3UL);
            h.Add("four");
            return h.ToUInt64();
        }

        ulong Swapped()
        {
            StableHasher h = new();
            h.Add(2L);
            h.Add(1);
            h.Add(3UL);
            h.Add("four");
            return h.ToUInt64();
        }

        First().ShouldBe(First());
        First().ShouldNotBe(Swapped());
    }

    [Fact]
    public void ToUInt64_CalledTwice_DoesNotConsumeState()
    {
        StableHasher hasher = new();
        hasher.Add(7);

        ulong once = hasher.ToUInt64();
        ulong twice = hasher.ToUInt64();
        hasher.Add(8);
        ulong extended = hasher.ToUInt64();

        twice.ShouldBe(once);
        extended.ShouldBe(XxHash64.HashToUInt64([7, 0, 0, 0, 8, 0, 0, 0]));
    }

    [Fact]
    public void Reset_AfterAdding_ReturnsToEmptyHash()
    {
        StableHasher hasher = new();
        hasher.Add("something");

        hasher.Reset();

        hasher.ToUInt64().ShouldBe(0xEF46DB3751D8E999UL);
    }
}
