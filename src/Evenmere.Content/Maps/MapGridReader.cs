using System.Globalization;
using System.Text;

namespace Evenmere.Content.Maps;

/// <summary>
/// The text of one layer file, split into rows. <see cref="Fits"/> is true when there are exactly <c>height</c> rows of exactly
/// <c>width</c> valid characters, which is when <see cref="At"/> may be used.
/// </summary>
internal sealed class MapGrid
{
    private readonly string[] _rows;

    public MapGrid(string[] rows, bool fits)
    {
        _rows = rows;
        Fits = fits;
    }

    /// <summary>Gets a value indicating whether the layer has the right size and only valid characters.</summary>
    public bool Fits { get; }

    /// <summary>Gets the character at a tile.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The character.</returns>
    public char At(int x, int y) => _rows[y][x];
}

/// <summary>Reads the text layers of a map and checks their size and alphabet, reporting line and column.</summary>
internal static class MapGridReader
{
    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads a file as bytes, reporting a problem if it cannot be read.</summary>
    /// <param name="path">The full path.</param>
    /// <param name="file">The path relative to the content root, for diagnostics.</param>
    /// <param name="report">The report.</param>
    /// <returns>The bytes, or <see langword="null"/> if the file cannot be read.</returns>
    public static byte[]? ReadBytes(string path, string file, MapReport report)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Error(DiagnosticCodes.Evl001, file, string.Empty, 0, 0, $"cannot read the file: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads one layer file.</summary>
    /// <param name="path">The full path.</param>
    /// <param name="file">The path relative to the content root, for diagnostics.</param>
    /// <param name="width">The map width.</param>
    /// <param name="height">The map height.</param>
    /// <param name="isValid">Tests a character against the layer's alphabet.</param>
    /// <param name="alphabet">Describes the alphabet for the message, such as <c>0-9 or A-F</c>.</param>
    /// <param name="report">The report.</param>
    /// <returns>The grid, or <see langword="null"/> if the file cannot be read or decoded.</returns>
    public static MapGrid? Read(string path, string file, int width, int height, Func<char, bool> isValid, string alphabet, MapReport report)
    {
        byte[]? bytes = ReadBytes(path, file, report);
        if (bytes is null)
        {
            return null;
        }

        if (bytes.AsSpan().StartsWith("﻿"u8))
        {
            report.Error(DiagnosticCodes.Evl001, file, string.Empty, 1, 1, "the file starts with a UTF-8 byte order mark; save it as UTF-8 without BOM");
            return null;
        }

        string text;
        try
        {
            text = _strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            report.Error(DiagnosticCodes.Evl001, file, string.Empty, 0, 0, "the file is not valid UTF-8");
            return null;
        }

        string[] rows = SplitRows(text);
        bool fits = true;

        if (rows.Length < height)
        {
            report.Error(
                DiagnosticCodes.Evl001, file, string.Empty, rows.Length + 1, 1,
                $"the file has {rows.Length} {Plural(rows.Length, "row")} but the map is {height} tall");
            fits = false;
        }
        else if (rows.Length > height)
        {
            report.Error(
                DiagnosticCodes.Evl001, file, string.Empty, height + 1, 1,
                $"the file has {rows.Length} rows but the map is {height} tall; remove the extra {Plural(rows.Length - height, "row")}");
            fits = false;
        }

        for (int y = 0; y < Math.Min(rows.Length, height); y++)
        {
            string row = rows[y];
            if (row.Length != width)
            {
                report.Error(
                    DiagnosticCodes.Evl001, file, string.Empty, y + 1, Math.Min(row.Length, width) + 1,
                    $"the row is {row.Length} {Plural(row.Length, "character")} long but the map is {width} wide");
                fits = false;
            }

            for (int x = 0; x < row.Length; x++)
            {
                char c = row[x];
                if (isValid(c))
                {
                    continue;
                }

                fits = false;
                report.Error(DiagnosticCodes.Evl001, file, string.Empty, y + 1, x + 1, $"{Describe(c)} is not valid here; expected {alphabet}");
                if (char.IsHighSurrogate(c))
                {
                    x++;
                }
            }
        }

        return new MapGrid(rows, fits);
    }

    /// <summary>Describes a character for a message: a printable ASCII character in quotes, anything else by code point.</summary>
    /// <param name="c">The character.</param>
    /// <returns>The description.</returns>
    public static string Describe(char c) =>
        c is >= '!' and <= '~' ? $"'{c}'" : string.Create(CultureInfo.InvariantCulture, $"U+{(int)c:X4}");

    // LF and CRLF both end a row; one final line break does not start another row. A lone CR stays in the row, where the
    // alphabet check reports it with its column.
    private static string[] SplitRows(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var rows = new List<string>();
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0)
            {
                if (start < text.Length)
                {
                    rows.Add(text[start..]);
                }

                break;
            }

            int contentEnd = end > start && text[end - 1] == '\r' ? end - 1 : end;
            rows.Add(text[start..contentEnd]);
            start = end + 1;
        }

        return [.. rows];
    }

    private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";
}
