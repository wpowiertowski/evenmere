using Evenmere.Core.Collections;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;
using MessagePack.Formatters;

namespace Evenmere.Sim.Serialization;

/// <summary>
/// Supplies the formatter for every closed <see cref="IdTable{T}"/>. A table can hold any value type, so its formatter
/// cannot be listed up front the way the scalar formatters are; this resolver builds it on first use and caches it.
/// </summary>
internal sealed class IdTableResolver : IFormatterResolver
{
    /// <summary>Gets the shared instance.</summary>
    public static IdTableResolver Instance { get; } = new();

    /// <inheritdoc />
    public IMessagePackFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;

    private static class Cache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter = Create();

        private static IMessagePackFormatter<T>? Create()
        {
            Type type = typeof(T);
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(IdTable<>))
            {
                return null;
            }

            Type formatterType = typeof(IdTableFormatter<>).MakeGenericType(type.GetGenericArguments());
            return (IMessagePackFormatter<T>?)Activator.CreateInstance(formatterType);
        }
    }
}
