using System.Globalization;

namespace Evenmere.Tools.Reporting;

/// <summary>Formats hashes the one way every command prints them.</summary>
internal static class Hex
{
    /// <summary>Formats a 64-bit hash as 16 lowercase hex digits, the form golden hashes and saves use.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns>The text, such as <c>6114913bc26ea39a</c>.</returns>
    public static string Format(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);
}
