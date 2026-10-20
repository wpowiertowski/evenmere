using System.Collections.Immutable;
using Evenmere.Core.Ids;

namespace Evenmere.Content;

/// <summary>Name-to-ID and ID-to-name lookups for one kind of content ID. Implemented by <see cref="ContentTable{TId, TDef}"/>.</summary>
/// <typeparam name="TId">The content ID type.</typeparam>
public interface IContentTable<TId>
    where TId : struct, IContentId<TId>
{
    /// <summary>Gets the string IDs in ID order: <c>Names[id.Value]</c> is the name of <c>id</c>. They are in ordinal string order.</summary>
    ImmutableArray<string> Names { get; }

    /// <summary>Finds the ID of a named item.</summary>
    /// <param name="name">The string ID, such as <c>wood</c>.</param>
    /// <param name="id">The ID, when found.</param>
    /// <returns><see langword="true"/> if an item has that name.</returns>
    bool TryGet(string name, out TId id);

    /// <summary>Gets the ID of a named item.</summary>
    /// <param name="name">The string ID.</param>
    /// <returns>The ID.</returns>
    /// <exception cref="KeyNotFoundException">No item has that name.</exception>
    TId IdOf(string name);

    /// <summary>Gets the string ID of an item.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The name.</returns>
    string NameOf(TId id);
}

/// <summary>
/// All the items of one content type: the definitions as an immutable array indexed by ID value, and the
/// string-to-ID mapping. IDs are assigned from 0 in ordinal order of the string IDs, so they are stable for a given
/// set of content. Lookups by name use a dictionary that nothing ever iterates, so its order cannot leak into state.
/// </summary>
/// <typeparam name="TId">The content ID type.</typeparam>
/// <typeparam name="TDef">The definition record.</typeparam>
public sealed class ContentTable<TId, TDef> : IContentTable<TId>
    where TId : struct, IContentId<TId>
{
    private readonly Dictionary<string, ushort> _byName;

    internal ContentTable(ImmutableArray<string> names, ImmutableArray<TDef> items)
    {
        if (names.Length != items.Length)
        {
            throw new ArgumentException("There must be exactly one definition per name.", nameof(items));
        }

        Names = names;
        Items = items;
        _byName = new Dictionary<string, ushort>(names.Length, StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++)
        {
            _byName.Add(names[i], checked((ushort)i));
        }
    }

    /// <summary>Gets the definitions: <c>Items[id.Value]</c> is the definition of <c>id</c>.</summary>
    public ImmutableArray<TDef> Items { get; }

    /// <inheritdoc />
    public ImmutableArray<string> Names { get; }

    /// <summary>Gets the number of items.</summary>
    public int Count => Items.Length;

    /// <summary>Gets the definition of an item.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="IndexOutOfRangeException">The ID does not belong to this content.</exception>
    public TDef this[TId id] => Items[id.Value];

    /// <summary>Gets the definition of an item.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="IndexOutOfRangeException">The ID does not belong to this content.</exception>
    public TDef Get(TId id) => Items[id.Value];

    /// <inheritdoc />
    public bool TryGet(string name, out TId id)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_byName.TryGetValue(name, out ushort value))
        {
            id = TId.From(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <inheritdoc />
    public TId IdOf(string name) =>
        TryGet(name, out TId id) ? id : throw new KeyNotFoundException($"No {typeof(TId).Name} named '{name}'.");

    /// <inheritdoc />
    public string NameOf(TId id) => Names[id.Value];
}
