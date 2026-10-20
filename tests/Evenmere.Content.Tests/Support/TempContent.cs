using System.Text;

namespace Evenmere.Content.Tests.Support;

/// <summary>
/// A content root in a temporary directory, optionally seeded with a copy of the minimal fixture pack, that tests
/// can edit freely and that deletes itself when disposed. Broken fixtures are built this way rather than committed.
/// </summary>
internal sealed class TempContent : IDisposable
{
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private TempContent(string root)
    {
        Root = root;
    }

    /// <summary>Gets the root directory.</summary>
    public string Root { get; }

    /// <summary>Creates an empty content root.</summary>
    public static TempContent Empty()
    {
        string root = Path.Combine(Path.GetTempPath(), "evenmere-content-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TempContent(root);
    }

    /// <summary>Creates a content root holding a copy of the minimal fixture pack.</summary>
    public static TempContent FromMinimal()
    {
        TempContent content = Empty();
        CopyDirectory(TestPaths.MinimalPack, content.Root);
        return content;
    }

    /// <summary>Writes a UTF-8 file without a byte order mark, creating folders as needed.</summary>
    public void Write(string relativePath, string text) => WriteBytes(relativePath, _utf8NoBom.GetBytes(text));

    /// <summary>Writes raw bytes, creating folders as needed.</summary>
    public void WriteBytes(string relativePath, byte[] bytes)
    {
        string full = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    /// <summary>Reads a file as text.</summary>
    public string Read(string relativePath) => File.ReadAllText(FullPath(relativePath));

    /// <summary>Deletes a file.</summary>
    public void Delete(string relativePath) => File.Delete(FullPath(relativePath));

    /// <summary>Replaces text in a file and fails the test if the text is not there, so an edit can never silently miss.</summary>
    public void Replace(string relativePath, string oldText, string newText)
    {
        string text = Read(relativePath);
        if (!text.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{oldText}' was not found in {relativePath}.");
        }

        Write(relativePath, text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    /// <summary>Loads this root.</summary>
    public ContentLoadResult Load() => ContentLoader.Load(Root);

    /// <summary>Rewrites every file with CRLF line endings.</summary>
    public void ConvertToCrlf()
    {
        foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
            File.WriteAllBytes(path, _utf8NoBom.GetBytes(text));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must never fail a test.
        }
    }

    private string FullPath(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
