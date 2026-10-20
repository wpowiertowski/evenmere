using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Evenmere.Content;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Sim.Queries.Types;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>
/// The System.Text.Json settings for reading a command's <c>args</c>. Property names match the record's constructor
/// parameters ignoring case, unknown and missing properties are errors, and the Core value types that System.Text.Json
/// would otherwise read wrongly (and silently, as zero) have converters: content IDs are written as their string id,
/// <see cref="Amount"/> as a decimal, <see cref="EntityId"/> as an integer, <see cref="TileCoord"/> as <c>[x, y]</c> and
/// <see cref="Rotation"/> as 0 to 3. Arrays of these (<c>TechId[]</c>, <c>TileCoord[]</c>) need nothing extra. A task that
/// adds a command parameter of another value type adds its converter here.
/// </summary>
internal static class ScenarioJson
{
    /// <summary>Creates the options for one scenario run. They are bound to the run's content, which resolves string ids.</summary>
    /// <param name="content">The content the scenario runs on.</param>
    /// <returns>The options.</returns>
    public static JsonSerializerOptions CreateOptions(ContentDb content) => new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters =
        {
            new AmountJsonConverter(),
            new EntityIdJsonConverter(),
            new TileCoordJsonConverter(),
            new RotationJsonConverter(),
            new ContentIdJsonConverterFactory(content),
        },
    };

    /// <summary>Reads an <see cref="Amount"/> from a decimal string such as <c>"12.5"</c> or a JSON number, with <see cref="Amount.Parse"/>.</summary>
    private sealed class AmountJsonConverter : JsonConverter<Amount>
    {
        public override Amount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
                _ => throw new JsonException("An amount must be a decimal string such as \"12.5\" or a JSON number."),
            };

            try
            {
                return Amount.Parse(text ?? string.Empty);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new JsonException(ex.Message, ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, Amount value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>Reads an <see cref="EntityId"/> from a JSON integer.</summary>
    private sealed class EntityIdJsonConverter : JsonConverter<EntityId>
    {
        public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value)
                ? new EntityId(value)
                : throw new JsonException("An entity id must be a whole number.");

        public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    /// <summary>
    /// Reads a <see cref="TileCoord"/> from a two-number JSON array <c>[x, y]</c>. An array of tiles (<c>TileCoord[]</c>) needs
    /// no converter of its own: System.Text.Json reads it element by element, as <c>[[x, y], …]</c>.
    /// </summary>
    private sealed class TileCoordJsonConverter : JsonConverter<TileCoord>
    {
        public override TileCoord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            const string Expected = "A tile must be a two-number array [x, y].";
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException(Expected);
            }

            int[] parts = new int[2];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out parts[i]))
                {
                    throw new JsonException(Expected);
                }
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException(Expected);
            }

            return new TileCoord(parts[0], parts[1]);
        }

        public override void Write(Utf8JsonWriter writer, TileCoord value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Reads a <see cref="Rotation"/> from the number of quarter turns, 0 to 3. Without this converter System.Text.Json would
    /// accept any number and produce an undefined enum value.
    /// </summary>
    private sealed class RotationJsonConverter : JsonConverter<Rotation>
    {
        public override Rotation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int quarters) && quarters is >= 0 and <= 3
                ? (Rotation)quarters
                : throw new JsonException("A rotation must be a number of quarter turns, 0 to 3.");

        public override void Write(Utf8JsonWriter writer, Rotation value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((int)value);
    }

    /// <summary>Makes a converter for each content ID type (<c>GoodId</c>, <c>BuildingTypeId</c> and so on) that reads the item's string id.</summary>
    private sealed class ContentIdJsonConverterFactory(ContentDb content) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsValueType && typeToConvert.GetInterfaces().Any(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContentId<>) && i.GetGenericArguments()[0] == typeToConvert);

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter?)Activator.CreateInstance(typeof(ContentIdJsonConverter<>).MakeGenericType(typeToConvert), content);
    }

    private sealed class ContentIdJsonConverter<TId>(ContentDb content) : JsonConverter<TId>
        where TId : struct, IContentId<TId>
    {
        public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"A {typeof(TId).Name} must be the item's string id, such as \"timber\".");
            }

            string name = reader.GetString() ?? string.Empty;
            return content.TryGet(name, out TId id)
                ? id
                : throw new JsonException($"No {typeof(TId).Name} named \"{name}\" in the loaded content.");
        }

        public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(content.NameOf(value));
    }
}
