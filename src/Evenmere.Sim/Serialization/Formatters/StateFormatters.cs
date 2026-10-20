using Evenmere.Core.Ids;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;
using MessagePack;
using MessagePack.Formatters;

namespace Evenmere.Sim.Serialization.Formatters;

/// <summary>Writes a <see cref="Pcg32"/> as the array <c>[state, inc]</c> and restores it with <see cref="Pcg32.FromState"/>.</summary>
internal sealed class Pcg32Formatter : IMessagePackFormatter<Pcg32>
{
    private const int FieldCount = 2;

    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, Pcg32 value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(FieldCount);
        writer.Write(value.State);
        writer.Write(value.Inc);
    }

    /// <inheritdoc />
    public Pcg32 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != FieldCount)
        {
            throw new MessagePackSerializationException($"A Pcg32 is an array of {FieldCount} values, not {count}.");
        }

        ulong state = reader.ReadUInt64();
        ulong inc = reader.ReadUInt64();

        // FromState rejects an even increment, which only a corrupt file can carry; the serializer reports it as a read failure.
        return Pcg32.FromState(state, inc);
    }
}

/// <summary>Writes a <see cref="GameClock"/> as its <see cref="long"/> tick count.</summary>
internal sealed class GameClockFormatter : IMessagePackFormatter<GameClock>
{
    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, GameClock value, MessagePackSerializerOptions options) => writer.Write(value.Tick);

    /// <inheritdoc />
    public GameClock Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => new(reader.ReadInt64());
}

/// <summary>Writes an <see cref="EntityIdAllocator"/> as the <see cref="int"/> value of the next ID it will hand out.</summary>
internal sealed class EntityIdAllocatorFormatter : IMessagePackFormatter<EntityIdAllocator?>
{
    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, EntityIdAllocator? value, MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        writer.Write(value.NextValue);
    }

    /// <inheritdoc />
    public EntityIdAllocator? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
        reader.TryReadNil() ? null : new EntityIdAllocator(reader.ReadInt32());
}
