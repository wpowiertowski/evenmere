using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using MessagePack;
using MessagePack.Formatters;

namespace Evenmere.Sim.Serialization.Formatters;

// The wire form of each Core value type is part of the save format (ADR-0007) and of the state hash. MessagePack
// writes an integer in the smallest encoding that holds it, so the bytes depend only on the value.

/// <summary>Writes an <see cref="Amount"/> as its <see cref="long"/> count of milli-units.</summary>
internal sealed class AmountFormatter : IMessagePackFormatter<Amount>
{
    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, Amount value, MessagePackSerializerOptions options) => writer.Write(value.Milli);

    /// <inheritdoc />
    public Amount Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => Amount.FromMilli(reader.ReadInt64());
}

/// <summary>Writes a <see cref="Ratio"/> as its <see cref="int"/> per-mille.</summary>
internal sealed class RatioFormatter : IMessagePackFormatter<Ratio>
{
    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, Ratio value, MessagePackSerializerOptions options) => writer.Write(value.PerMille);

    /// <inheritdoc />
    public Ratio Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => Ratio.FromPerMille(reader.ReadInt32());
}

/// <summary>Writes an <see cref="EntityId"/> as its <see cref="int"/> value.</summary>
internal sealed class EntityIdFormatter : IMessagePackFormatter<EntityId>
{
    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, EntityId value, MessagePackSerializerOptions options) => writer.Write(value.Value);

    /// <inheritdoc />
    public EntityId Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => new(reader.ReadInt32());
}

/// <summary>Writes any content ID (<c>GoodId</c>, <c>BuildingTypeId</c> and so on) as its <see cref="ushort"/> index.</summary>
/// <typeparam name="TId">The content ID type.</typeparam>
internal sealed class ContentIdFormatter<TId> : IMessagePackFormatter<TId>
    where TId : struct, IContentId<TId>
{
    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, TId value, MessagePackSerializerOptions options) => writer.Write(value.Value);

    /// <inheritdoc />
    public TId Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => TId.From(reader.ReadUInt16());
}
