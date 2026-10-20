using System.Collections.Immutable;
using System.Text.Json;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Loading;

/// <summary>
/// A JSON object being read property by property. Each accessor marks its property as known; disposing the view
/// reports every property nobody asked for as an unknown-property error, so typos are never silently ignored.
/// Accessors report problems through the owning <see cref="ItemReader"/> and return a harmless default, because an
/// item with any error is dropped anyway.
/// </summary>
internal sealed class ObjectView : IDisposable
{
    private readonly ItemReader _reader;
    private readonly JsonElement _element;
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);

    public ObjectView(ItemReader reader, JsonElement element, string pointer)
    {
        _reader = reader;
        _element = element;
        Pointer = pointer;
    }

    public ItemReader Reader => _reader;

    public string Pointer { get; }

    public string Child(string name) => JsonPointer.Append(Pointer, name);

    /// <summary>Reports every property that no accessor asked for.</summary>
    public void Dispose()
    {
        foreach (JsonProperty property in _element.EnumerateObject())
        {
            if (!_known.Contains(property.Name))
            {
                _reader.Error(Child(property.Name), $"unknown property '{property.Name}'");
            }
        }
    }

    /// <summary>Enumerates every property in file order and marks them all as known (for objects keyed by free names).</summary>
    public IEnumerable<JsonProperty> Properties()
    {
        foreach (JsonProperty property in _element.EnumerateObject())
        {
            _known.Add(property.Name);
            yield return property;
        }
    }

    public bool TryGet(string name, out JsonElement value)
    {
        _known.Add(name);
        return _element.TryGetProperty(name, out value);
    }

    public bool Require(string name, out JsonElement value)
    {
        if (TryGet(name, out value))
        {
            return true;
        }

        _reader.Error(Pointer, $"missing required property '{name}'");
        return false;
    }

    public string ReqString(string name) =>
        Require(name, out JsonElement e) && _reader.TryString(e, Child(name), out string s) ? s : string.Empty;

    public string? OptString(string name) =>
        TryGet(name, out JsonElement e) && _reader.TryString(e, Child(name), out string s) ? s : null;

    public int ReqInt(string name, int min, int max) =>
        Require(name, out JsonElement e) && _reader.TryInt(e, Child(name), min, max, out int v) ? v : min;

    public int OptInt(string name, int fallback, int min, int max) =>
        TryGet(name, out JsonElement e) ? (_reader.TryInt(e, Child(name), min, max, out int v) ? v : fallback) : fallback;

    public int? OptNullableInt(string name, int min, int max) =>
        TryGet(name, out JsonElement e) && _reader.TryInt(e, Child(name), min, max, out int v) ? v : null;

    public bool ReqBool(string name) =>
        Require(name, out JsonElement e) && _reader.TryBool(e, Child(name), out bool b) && b;

    public bool OptBool(string name, bool fallback) =>
        TryGet(name, out JsonElement e) ? (_reader.TryBool(e, Child(name), out bool b) ? b : fallback) : fallback;

    public Amount ReqAmount(string name, QuantityRange range) =>
        Require(name, out JsonElement e) && _reader.TryAmount(e, Child(name), range, out Amount a) ? a : Amount.Zero;

    public Amount OptAmount(string name, QuantityRange range) =>
        TryGet(name, out JsonElement e) && _reader.TryAmount(e, Child(name), range, out Amount a) ? a : Amount.Zero;

    public T ReqEnum<T>(string name)
        where T : struct, Enum =>
        Require(name, out JsonElement e) && _reader.TryEnum(e, Child(name), out T v) ? v : default;

    public T? OptEnum<T>(string name)
        where T : struct, Enum =>
        TryGet(name, out JsonElement e) && _reader.TryEnum(e, Child(name), out T v) ? v : null;

    public TId ReqRef<TId>(InternTable<TId> table, string name)
        where TId : struct, IContentId<TId> =>
        Require(name, out JsonElement e) && _reader.TryRef(e, Child(name), table, out TId id) ? id : default;

    public TId? OptRef<TId>(InternTable<TId> table, string name)
        where TId : struct, IContentId<TId> =>
        TryGet(name, out JsonElement e) && _reader.TryRef(e, Child(name), table, out TId id) ? id : null;

    public ObjectView? OptObject(string name) =>
        TryGet(name, out JsonElement e) ? _reader.OpenObject(e, Child(name)) : null;

    /// <summary>Reads an optional array of references. An absent property is an empty list.</summary>
    public ImmutableArray<TId> RefList<TId>(InternTable<TId> table, string name)
        where TId : struct, IContentId<TId>
    {
        if (!TryGet(name, out JsonElement array))
        {
            return [];
        }

        ImmutableArray<TId>.Builder list = ImmutableArray.CreateBuilder<TId>();
        ForEach(array, Child(name), (element, pointer) =>
        {
            if (_reader.TryRef(element, pointer, table, out TId id))
            {
                list.Add(id);
            }
        });
        return list.ToImmutable();
    }

    /// <summary>Reads an optional array of enum names. An absent property is an empty list; duplicates are errors.</summary>
    public ImmutableArray<T> EnumList<T>(string name)
        where T : struct, Enum
    {
        if (!TryGet(name, out JsonElement array))
        {
            return [];
        }

        var list = new List<T>();
        ForEach(array, Child(name), (element, pointer) =>
        {
            if (_reader.TryEnum(element, pointer, out T value))
            {
                if (list.Contains(value))
                {
                    _reader.Error(pointer, $"'{ContentNames.NameOf(value)}' is listed twice");
                }
                else
                {
                    list.Add(value);
                }
            }
        });
        list.Sort();
        return [.. list];
    }

    /// <summary>Reads an optional array of snake_case tags, in file order. An absent property is an empty list.</summary>
    public ImmutableArray<string> Tags(string name = "tags")
    {
        if (!TryGet(name, out JsonElement array))
        {
            return [];
        }

        ImmutableArray<string>.Builder list = ImmutableArray.CreateBuilder<string>();
        ForEach(array, Child(name), (element, pointer) =>
        {
            if (!_reader.TryString(element, pointer, out string tag))
            {
                return;
            }

            if (!Ids.IsValid(tag))
            {
                _reader.Error(pointer, $"'{tag}' is not a valid tag; use snake_case");
            }
            else if (list.Contains(tag))
            {
                _reader.Error(pointer, $"tag '{tag}' is listed twice");
            }
            else
            {
                list.Add(tag);
            }
        });
        return list.ToImmutable();
    }

    /// <summary>Reads a good-to-quantity map such as <c>{ "timber": 2 }</c>, sorted by good ID. Quantities must be above zero.</summary>
    public ImmutableArray<GoodAmount> GoodMap(string name, bool required)
    {
        JsonElement element;
        bool found = required ? Require(name, out element) : TryGet(name, out element);
        if (!found)
        {
            return [];
        }

        using ObjectView? map = _reader.OpenObject(element, Child(name));
        return map is null ? [] : map.ReadGoodAmounts(coinIsReserved: false, out _);
    }

    /// <summary>Reads a bill of materials: a good map where the reserved key <c>coin</c> is coin. An absent property is free.</summary>
    public CostSpec Cost(string name, bool allowFree = true)
    {
        if (!TryGet(name, out JsonElement element))
        {
            if (!allowFree)
            {
                _reader.Error(Pointer, $"missing required property '{name}'");
            }

            return CostSpec.Free;
        }

        return _reader.ReadCostObject(element, Child(name), allowFree);
    }

    /// <summary>
    /// Reads every property of this object as a good quantity. When <paramref name="coinIsReserved"/> is set, the key
    /// <c>coin</c> is coin rather than a good and is returned in <paramref name="coin"/>.
    /// </summary>
    public ImmutableArray<GoodAmount> ReadGoodAmounts(bool coinIsReserved, out Amount coin)
    {
        coin = Amount.Zero;
        var goods = new List<GoodAmount>();
        foreach (JsonProperty property in _element.EnumerateObject())
        {
            _known.Add(property.Name);
            string pointer = Child(property.Name);
            if (!_reader.TryAmount(property.Value, pointer, QuantityRange.Positive, out Amount amount))
            {
                continue;
            }

            if (coinIsReserved && property.Name == CostKeys.Coin)
            {
                coin = amount;
            }
            else if (_reader.TryRefName(property.Name, pointer, _reader.Tables.Goods, out GoodId good))
            {
                goods.Add(new GoodAmount(good, amount));
            }
        }

        goods.Sort(static (a, b) => a.Good.Value.CompareTo(b.Good.Value));
        return [.. goods];
    }

    /// <summary>Reads a required <c>era</c> field: a positive integer that some era defines (EVL002 otherwise).</summary>
    public int ReqEra(string name = "era")
    {
        if (!Require(name, out JsonElement element) || !_reader.TryInt(element, Child(name), 1, 64, out int era))
        {
            return 1;
        }

        if (!_reader.Tables.EraOrders.Contains(era))
        {
            _reader.Unresolved(Child(name), $"no era with order {era} is defined");
        }

        return era;
    }

    /// <summary>Reads an optional array of effects. An absent property is an empty list.</summary>
    public ImmutableArray<Effect> Effects(string name = "effects") =>
        TryGet(name, out JsonElement e) ? _reader.ReadEffects(e, Child(name)) : [];

    /// <summary>Reads an optional condition.</summary>
    public Condition? OptCondition(string name) =>
        TryGet(name, out JsonElement e) ? _reader.ReadCondition(e, Child(name)) : null;

    /// <summary>Reads a required condition.</summary>
    public Condition? ReqCondition(string name) =>
        Require(name, out JsonElement e) ? _reader.ReadCondition(e, Child(name)) : null;

    /// <summary>Calls <paramref name="visit"/> for each element of an array, or reports that the value is not an array.</summary>
    public void ForEach(JsonElement array, string pointer, Action<JsonElement, string> visit)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            _reader.Error(pointer, $"expected an array but found {ItemReader.Describe(array)}");
            return;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            visit(element, JsonPointer.Append(pointer, index));
            index++;
        }
    }
}

/// <summary>Reserved keys in a cost object.</summary>
internal static class CostKeys
{
    /// <summary>In a cost object, this key is coin rather than a good ID.</summary>
    public const string Coin = "coin";
}
