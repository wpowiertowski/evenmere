using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Evenmere.Sim.Serialization;

/// <summary>
/// The one resolver every state type is serialised with. It lists the formatters for the Core value types, builds
/// <see cref="Evenmere.Core.Collections.IdTable{T}"/> formatters on demand, and falls back to MessagePack's
/// attribute-based resolver for the <c>[MessagePackObject]</c> state classes. Those use explicit integer keys, never
/// string keys, and the state holds no map-typed members, so the bytes depend only on the values (ADR-0003).
/// </summary>
/// <remarks>
/// The attribute-based dynamic resolver is used rather than the source generator: it needs nothing beyond the
/// attributes on the state classes, and every launch platform is a desktop runtime where dynamic code is available
/// (ADR-0008). Its <c>AllowPrivate</c> variant lets a later task keep a nested state type internal.
/// </remarks>
internal static class SimResolver
{
    /// <summary>Gets the composite resolver.</summary>
    public static IFormatterResolver Instance { get; } = CompositeResolver.Create(
        new IMessagePackFormatter[]
        {
            new AmountFormatter(),
            new RatioFormatter(),
            new EntityIdFormatter(),
            new Pcg32Formatter(),
            new GameClockFormatter(),
            new EntityIdAllocatorFormatter(),
            new ContentIdFormatter<GoodId>(),
            new ContentIdFormatter<BuildingTypeId>(),
            new ContentIdFormatter<RecipeId>(),
            new ContentIdFormatter<TierId>(),
            new ContentIdFormatter<TechId>(),
            new ContentIdFormatter<CivicId>(),
            new ContentIdFormatter<TraditionId>(),
            new ContentIdFormatter<FestivalId>(),
            new ContentIdFormatter<GreatWorkTypeId>(),
            new ContentIdFormatter<MonumentId>(),
            new ContentIdFormatter<HappeningId>(),
            new ContentIdFormatter<EraId>(),
            new ContentIdFormatter<TerrainId>(),
        },
        new IFormatterResolver[]
        {
            IdTableResolver.Instance,
            StandardResolverAllowPrivate.Instance,
        });

    /// <summary>
    /// Gets the serializer options: no compression, because the canonical form is uncompressed, and the untrusted-data
    /// security level, because the bytes may come from a file on disk.
    /// </summary>
    public static MessagePackSerializerOptions Options { get; } =
        MessagePackSerializerOptions.Standard.WithResolver(Instance).WithSecurity(MessagePackSecurity.UntrustedData);
}
