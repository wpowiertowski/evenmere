using System.Collections.Immutable;
using System.Text.Json;

namespace Evenmere.Content.Loading;

/// <summary>
/// One content file after phase 1: located, hashed and parsed as JSON, but with its references not yet resolved.
/// </summary>
/// <param name="Type">The kind of content.</param>
/// <param name="Id">The item's string ID, which is its file name without <c>.json</c>.</param>
/// <param name="RootIndex">The index of the content root it came from, in load order.</param>
/// <param name="File">The path relative to that root, with forward slashes.</param>
/// <param name="Json">The parsed document root, which is always an object; <see langword="null"/> if the file could not be parsed (a diagnostic says why).</param>
public sealed record RawItem(ContentType Type, string Id, int RootIndex, string File, JsonElement? Json);

/// <summary>
/// The result of phase 1 of loading: every item file that survived override resolution, as parsed JSON, plus the
/// content hash. The linter (M0-04) validates these against the JSON Schemas.
/// </summary>
public sealed class RawContent
{
    internal RawContent(ImmutableArray<string> roots, ImmutableArray<RawItem> items, ulong contentHash)
    {
        Roots = roots;
        Items = items;
        ContentHash = contentHash;
    }

    /// <summary>Gets the content roots, in load order; later roots override earlier ones.</summary>
    public ImmutableArray<string> Roots { get; }

    /// <summary>
    /// Gets the items, sorted by <see cref="ContentType"/> and then by ordinal ID. When several roots define the same
    /// type and ID, only the one from the last root is present. The tunables file appears as type
    /// <see cref="ContentType.Rules"/> with ID <see cref="ContentTypes.RulesId"/>.
    /// </summary>
    public ImmutableArray<RawItem> Items { get; }

    /// <summary>Gets the content hash over the files under every root, except schemas and Markdown (see <see cref="ContentLoader"/>).</summary>
    public ulong ContentHash { get; }

    /// <summary>Gets the items of one type, in ordinal ID order.</summary>
    /// <param name="type">The content type.</param>
    /// <returns>The items.</returns>
    public IEnumerable<RawItem> OfType(ContentType type) => Items.Where(item => item.Type == type);
}
