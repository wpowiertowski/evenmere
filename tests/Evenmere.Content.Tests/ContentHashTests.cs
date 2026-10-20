using System.Text;
using Evenmere.Content.Tests.Support;
using Evenmere.Core.Hashing;

namespace Evenmere.Content.Tests;

/// <summary>
/// The content hash is one XxHash64 stream: per root its index, then per file (ordinal path order) the relative path,
/// the byte length of the LF-normalised content and those bytes.
/// </summary>
public sealed class ContentHashTests
{
    [Fact]
    public void ContentHash_LfAndCrlfCopiesOfTheFixture_AreIdentical()
    {
        using var lf = TempContent.FromMinimal();
        using var crlf = TempContent.FromMinimal();
        crlf.ConvertToCrlf();

        File.ReadAllText(Path.Combine(crlf.Root, "goods", "fish.json")).ShouldContain("\r\n");
        File.ReadAllText(Path.Combine(lf.Root, "goods", "fish.json")).ShouldNotContain("\r");

        ContentDb lfDb = lf.Load().ShouldLoadCleanly();
        ContentDb crlfDb = crlf.Load().ShouldLoadCleanly();

        crlfDb.ContentHash.ShouldBe(lfDb.ContentHash);
    }

    [Fact]
    public void ContentHash_FixtureAndItsCopy_AreIdentical()
    {
        using var copy = TempContent.FromMinimal();

        ContentLoader.Load(copy.Root).ShouldLoadCleanly().ContentHash
            .ShouldBe(ContentLoader.Load(TestPaths.MinimalPack).ShouldLoadCleanly().ContentHash);
    }

    [Fact]
    public void ContentHash_DatabaseAndRawContent_Agree()
    {
        ContentLoadResult result = ContentLoader.Load(TestPaths.MinimalPack);

        result.Db!.ContentHash.ShouldBe(result.Raw.ContentHash);
    }

    [Fact]
    public void ContentHash_FollowsTheDocumentedByteLayout()
    {
        using var content = TempContent.Empty();
        content.WriteBytes("b/two.txt", Encoding.UTF8.GetBytes("two"));
        content.WriteBytes("a.txt", Encoding.UTF8.GetBytes("line one\r\nline two\r\n"));
        content.WriteBytes("b/one.bin", [0x00, 0x0D, 0x0A, 0xFF]);

        // Files in ordinal order of the relative path: "a.txt", "b/one.bin", "b/two.txt".
        var expected = new StableHasher();
        expected.Add(0);
        AddFile(expected, "a.txt", Encoding.UTF8.GetBytes("line one\nline two\n"));
        AddFile(expected, "b/one.bin", [0x00, 0x0A, 0xFF]);
        AddFile(expected, "b/two.txt", Encoding.UTF8.GetBytes("two"));

        content.Load().Raw.ContentHash.ShouldBe(expected.ToUInt64());
    }

    [Fact]
    public void ContentHash_SeveralRoots_AreHashedInLoadOrderEachPrefixedWithItsIndex()
    {
        using var first = TempContent.Empty();
        using var second = TempContent.Empty();
        first.WriteBytes("a.txt", [1]);
        second.WriteBytes("a.txt", [2]);
        second.WriteBytes("z.txt", [3]);

        var expected = new StableHasher();
        expected.Add(0);
        AddFile(expected, "a.txt", [1]);
        expected.Add(1);
        AddFile(expected, "a.txt", [2]);
        AddFile(expected, "z.txt", [3]);

        ContentLoader.Load([first.Root, second.Root]).Raw.ContentHash.ShouldBe(expected.ToUInt64());
        ContentLoader.Load([second.Root, first.Root]).Raw.ContentHash.ShouldNotBe(expected.ToUInt64());
    }

    [Fact]
    public void ContentHash_OneChangedByte_ChangesTheHash()
    {
        using var content = TempContent.FromMinimal();
        ulong before = content.Load().ShouldLoadCleanly().ContentHash;

        content.Replace("goods/fish.json", "\"era\": 1", "\"era\": 2");

        // Era 2 exists, so the content still loads; only the hash moves.
        content.Load().ShouldLoadCleanly().ContentHash.ShouldNotBe(before);
    }

    [Fact]
    public void ContentHash_RenamedFile_ChangesTheHashBecausePathsAreHashed()
    {
        using var content = TempContent.FromMinimal();
        ulong before = content.Load().ShouldLoadCleanly().ContentHash;

        File.Move(Path.Combine(content.Root, "locale", "en", "goods.json"), Path.Combine(content.Root, "locale", "en", "items.json"));

        content.Load().ShouldLoadCleanly().ContentHash.ShouldNotBe(before);
    }

    [Fact]
    public void ContentHash_LocaleEdit_ChangesTheHash()
    {
        using var content = TempContent.FromMinimal();
        ulong before = content.Load().ShouldLoadCleanly().ContentHash;

        content.Replace("locale/en/goods.json", "Logs from the forest.", "Logs from the old forest.");

        content.Load().ShouldLoadCleanly().ContentHash.ShouldNotBe(before);
    }

    [Fact]
    public void ContentHash_CoversFilesThatAreNotItemsExceptSchemasAndMarkdown()
    {
        using var content = TempContent.FromMinimal();
        ulong before = content.Load().ShouldLoadCleanly().ContentHash;

        content.Write("maps/starter/map.json", "{}");
        ulong withMap = content.Load().ShouldLoadCleanly().ContentHash;
        content.Write("maps/starter/map.json", "{ \"size\": 128 }");
        ulong editedMap = content.Load().ShouldLoadCleanly().ContentHash;
        content.Write("notes.txt", "scribbles");
        ulong withNotes = content.Load().ShouldLoadCleanly().ContentHash;

        withMap.ShouldNotBe(before);
        editedMap.ShouldNotBe(withMap);
        withNotes.ShouldNotBe(editedMap);
    }

    [Fact]
    public void ContentHash_SchemaEdit_LeavesTheHashUnchanged()
    {
        using var content = TempContent.FromMinimal();
        ulong before = content.Load().ShouldLoadCleanly().ContentHash;

        content.Write("schemas/good.schema.json", "{ \"type\": \"object\" }");
        content.Write("schemas/nested/building.schema.json", "{}");
        ulong withSchemas = content.Load().ShouldLoadCleanly().ContentHash;
        content.Write("schemas/good.schema.json", "{ \"type\": \"object\", \"required\": [\"id\"] }");

        withSchemas.ShouldBe(before);
        content.Load().ShouldLoadCleanly().ContentHash.ShouldBe(before);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("readme.MD")]
    [InlineData("docs/authoring.md")]
    [InlineData("goods/NOTES.md")]
    [InlineData("locale/en/CHANGES.md")]
    public void ContentHash_MarkdownEdit_LeavesTheHashUnchanged(string markdownPath)
    {
        using var content = TempContent.FromMinimal();
        ulong before = content.Load().ShouldLoadCleanly().ContentHash;

        content.Write(markdownPath, "# Authoring");
        ulong added = content.Load().ShouldLoadCleanly().ContentHash;
        content.Write(markdownPath, "# Authoring, revised");

        added.ShouldBe(before);
        content.Load().ShouldLoadCleanly().ContentHash.ShouldBe(before);
    }

    [Fact]
    public void ContentHash_SchemasAndMarkdownAreSkippedAndEverythingElseIsHashed()
    {
        using var content = TempContent.Empty();
        content.WriteBytes("README.md", [1]);
        content.WriteBytes("schemas/a.schema.json", [2]);
        content.WriteBytes("maps/starter/map.json", [3]);
        content.WriteBytes("locale/en/goods.json", [4]);
        content.WriteBytes("schemasx/keep.json", [5]);
        content.WriteBytes("deep/inner/notes.md", [6]);

        var expected = new StableHasher();
        expected.Add(0);
        AddFile(expected, "locale/en/goods.json", [4]);
        AddFile(expected, "maps/starter/map.json", [3]);
        AddFile(expected, "schemasx/keep.json", [5]);

        content.Load().Raw.ContentHash.ShouldBe(expected.ToUInt64());
    }

    [Fact]
    public void ContentHash_IgnoresDotFilesSoEditorAndOsLitterDoesNotChangeIt()
    {
        using var content = TempContent.FromMinimal();
        ulong before = content.Load().ShouldLoadCleanly().ContentHash;

        content.Write(".DS_Store", "litter");
        content.Write("goods/.hidden.json", "{}");
        content.Write(".git/config", "[core]");

        content.Load().ShouldLoadCleanly().ContentHash.ShouldBe(before);
    }

    [Fact]
    public void ContentHash_ByteOrderMark_IsNotStrippedBeforeHashing()
    {
        using var without = TempContent.FromMinimal();
        using var with = TempContent.FromMinimal();
        byte[] original = File.ReadAllBytes(Path.Combine(with.Root, "goods", "fish.json"));
        with.WriteBytes("goods/fish.json", [0xEF, 0xBB, 0xBF, .. original]);

        ContentLoadResult result = with.Load();

        result.Diagnostics.ShouldHaveSingleMatch("EVL001", "goods/fish.json", string.Empty);
        result.Raw.ContentHash.ShouldNotBe(without.Load().Raw.ContentHash);
    }

    [Fact]
    public void ContentHash_LoneCarriageReturn_IsLeftAlone()
    {
        using var lone = TempContent.Empty();
        using var none = TempContent.Empty();
        lone.WriteBytes("a.txt", [0x61, 0x0D, 0x62]);
        none.WriteBytes("a.txt", [0x61, 0x62]);

        lone.Load().Raw.ContentHash.ShouldNotBe(none.Load().Raw.ContentHash);
    }

    [Fact]
    public void ContentHash_IsStableAcrossRepeatedLoads()
    {
        ulong first = ContentLoader.Load(TestPaths.MinimalPack).Raw.ContentHash;
        ulong second = ContentLoader.Load(TestPaths.MinimalPack).Raw.ContentHash;

        second.ShouldBe(first);
    }

    private static void AddFile(StableHasher hasher, string relativePath, byte[] normalisedBytes)
    {
        hasher.Add(relativePath);
        hasher.Add((long)normalisedBytes.Length);
        hasher.Add(normalisedBytes);
    }
}
