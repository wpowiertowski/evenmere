using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using MessagePack;
using MessagePack.Formatters;

namespace Evenmere.Sim.Serialization.Formatters;

/// <summary>
/// Writes an <see cref="IdTable{T}"/> as an array of <c>[id, value]</c> pairs in ascending ID order, which is the order
/// the table enumerates in, so equal tables always give equal bytes. Removed entries leave no trace: a table with
/// tombstones and a freshly built one with the same live entries serialise identically. Reading adds the pairs back in
/// order and rejects IDs that are not strictly ascending, because the writer never produces them.
/// </summary>
/// <typeparam name="TValue">The table's value type.</typeparam>
internal sealed class IdTableFormatter<TValue> : IMessagePackFormatter<IdTable<TValue>?>
{
    private const int PairFieldCount = 2;

    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, IdTable<TValue>? value, MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        IMessagePackFormatter<TValue> valueFormatter = options.Resolver.GetFormatterWithVerify<TValue>();
        writer.WriteArrayHeader(value.Count);
        foreach (IdTable<TValue>.Entry entry in value)
        {
            writer.WriteArrayHeader(PairFieldCount);
            writer.Write(entry.Id.Value);
            valueFormatter.Serialize(ref writer, entry.Value, options);
        }
    }

    /// <inheritdoc />
    public IdTable<TValue>? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        options.Security.DepthStep(ref reader);
        try
        {
            IMessagePackFormatter<TValue> valueFormatter = options.Resolver.GetFormatterWithVerify<TValue>();
            int count = reader.ReadArrayHeader();

            // The count comes from the file, so it is not used to size the table: a corrupt count then fails when the data runs out.
            var table = new IdTable<TValue>();
            int previous = 0;
            for (int i = 0; i < count; i++)
            {
                int fields = reader.ReadArrayHeader();
                if (fields != PairFieldCount)
                {
                    throw new MessagePackSerializationException($"An IdTable entry is an [id, value] pair, not {fields} values.");
                }

                int id = reader.ReadInt32();
                if (id <= previous)
                {
                    throw new MessagePackSerializationException($"IdTable IDs must be positive and strictly ascending; {id} follows {previous}.");
                }

                previous = id;
                table.Add(new EntityId(id), valueFormatter.Deserialize(ref reader, options));
            }

            return table;
        }
        finally
        {
            reader.Depth--;
        }
    }
}
