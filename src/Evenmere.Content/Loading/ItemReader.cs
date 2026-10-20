using System.Text.Json;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Loading;

/// <summary>What range a quantity must lie in.</summary>
internal enum QuantityRange
{
    /// <summary>Zero or more.</summary>
    NonNegative,

    /// <summary>Greater than zero.</summary>
    Positive,
}

/// <summary>
/// Reads values out of one parsed content file and reports problems as diagnostics against that file. Phase 2 of
/// loading walks the <see cref="JsonElement"/> tree through this class, so every problem carries an exact RFC 6901
/// pointer and a single file can report many problems at once. Quantities are read from the raw number text and
/// never pass through <see cref="double"/>.
/// </summary>
internal sealed partial class ItemReader
{
    private readonly List<ContentDiagnostic> _sink;

    public ItemReader(string file, List<ContentDiagnostic> sink, ResolveTables tables)
    {
        File = file;
        _sink = sink;
        Tables = tables;
    }

    /// <summary>Gets the file being read, relative to its content root.</summary>
    public string File { get; }

    /// <summary>Gets the intern tables that references resolve against.</summary>
    public ResolveTables Tables { get; }

    /// <summary>Gets how many errors this reader has reported. An item with any error is dropped.</summary>
    public int ErrorCount { get; private set; }

    /// <summary>Describes a JSON value's kind for messages, for example "a string".</summary>
    public static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "the number " + element.GetRawText(),
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "an unexpected value",
    };

    /// <summary>Reports an EVL001 shape error.</summary>
    public void Error(string pointer, string message)
    {
        ErrorCount++;
        _sink.Add(new ContentDiagnostic(DiagnosticCodes.Evl001, DiagnosticSeverity.Error, File, pointer, message));
    }

    /// <summary>Reports an EVL002 unresolved reference.</summary>
    public void Unresolved(string pointer, string message)
    {
        ErrorCount++;
        _sink.Add(new ContentDiagnostic(DiagnosticCodes.Evl002, DiagnosticSeverity.Error, File, pointer, message));
    }

    /// <summary>Opens a JSON object for reading, or reports that the value is not one. Dispose it to report unknown properties.</summary>
    public ObjectView? OpenObject(JsonElement element, string pointer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return new ObjectView(this, element, pointer);
        }

        Error(pointer, $"expected an object but found {Describe(element)}");
        return null;
    }

    /// <summary>Reads a JSON string.</summary>
    public bool TryString(JsonElement element, string pointer, out string value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return true;
        }

        Error(pointer, $"expected a string but found {Describe(element)}");
        value = string.Empty;
        return false;
    }

    /// <summary>Reads a JSON integer within a range. Decimals and exponent forms are rejected.</summary>
    public bool TryInt(JsonElement element, string pointer, int min, int max, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            Error(pointer, $"expected an integer but found {Describe(element)}");
            return false;
        }

        if (!element.TryGetInt32(out int parsed))
        {
            Error(pointer, $"expected an integer but found {element.GetRawText()}");
            return false;
        }

        if (parsed < min || parsed > max)
        {
            Error(pointer, $"{parsed} is out of range; expected {min} to {max}");
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Reads a JSON boolean.</summary>
    public bool TryBool(JsonElement element, string pointer, out bool value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            default:
                Error(pointer, $"expected true or false but found {Describe(element)}");
                value = false;
                return false;
        }
    }

    /// <summary>
    /// Reads a quantity from the raw text of a JSON number, so it never becomes a double. More than three decimals,
    /// an exponent, or a string are errors.
    /// </summary>
    public bool TryAmount(JsonElement element, string pointer, QuantityRange range, out Amount value)
    {
        value = Amount.Zero;
        if (element.ValueKind != JsonValueKind.Number)
        {
            Error(pointer, $"expected a quantity (a JSON number with at most 3 decimals) but found {Describe(element)}");
            return false;
        }

        string text = element.GetRawText();
        Amount parsed;
        try
        {
            parsed = Amount.Parse(text);
        }
        catch (FormatException)
        {
            // A valid JSON number can fail Amount's grammar only by having an exponent or more than three decimals.
            Error(
                pointer,
                text.Contains('e') || text.Contains('E')
                    ? $"'{text}' uses exponent notation; write quantities as plain decimals with at most 3 decimal places"
                    : $"'{text}' has more than 3 decimal places; quantities hold at most 3 decimals");
            return false;
        }
        catch (OverflowException)
        {
            Error(pointer, $"'{text}' is too large for a quantity");
            return false;
        }

        if (range == QuantityRange.Positive ? parsed <= Amount.Zero : parsed < Amount.Zero)
        {
            Error(pointer, range == QuantityRange.Positive ? $"{text} must be greater than zero" : $"{text} must not be negative");
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Reads a string that names a member of a closed vocabulary (an enum).</summary>
    public bool TryEnum<T>(JsonElement element, string pointer, out T value)
        where T : struct, Enum
    {
        value = default;
        if (!TryString(element, pointer, out string text))
        {
            return false;
        }

        if (ContentNames.TryParse(text, out value))
        {
            return true;
        }

        Error(pointer, $"'{text}' is not a valid {typeof(T).Name}; expected one of: {string.Join(", ", ContentNames.NamesOf<T>())}");
        return false;
    }

    /// <summary>Reads a string that names another item, resolving it to a typed ID (EVL002 if it is not defined).</summary>
    public bool TryRef<TId>(JsonElement element, string pointer, InternTable<TId> table, out TId id)
        where TId : struct, IContentId<TId>
    {
        id = default;
        return TryString(element, pointer, out string name) && TryRefName(name, pointer, table, out id);
    }

    /// <summary>Resolves a string ID, such as a property name used as a key, to a typed ID (EVL002 if it is not defined).</summary>
    public bool TryRefName<TId>(string name, string pointer, InternTable<TId> table, out TId id)
        where TId : struct, IContentId<TId>
    {
        if (table.TryGet(name, out id))
        {
            return true;
        }

        Unresolved(pointer, $"no {table.Singular} named '{name}' is defined");
        return false;
    }
}
