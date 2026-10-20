using Evenmere.Content;
using Evenmere.Core.Hashing;
using Evenmere.Sim.State;
using MessagePack;

namespace Evenmere.Sim.Serialization;

/// <summary>
/// Turns a <see cref="WorldState"/> into canonical bytes and back (ADR-0007). The bytes are MessagePack with explicit
/// integer keys and no compression: the same value always gives the same bytes, so they are both the save body (which
/// M1-14 compresses and wraps in a header) and the input of the state hash (ADR-0003).
/// </summary>
public static class StateSerializer
{
    /// <summary>Serialises a world state to its canonical, uncompressed bytes.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    public static byte[] Write(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return MessagePackSerializer.Serialize(state, SimResolver.Options);
    }

    /// <summary>Reads a world state from bytes that <see cref="Write"/> produced, and checks that it fits the content.</summary>
    /// <param name="data">The bytes; there must be nothing after the state.</param>
    /// <param name="content">The loaded content the state is for.</param>
    /// <returns>The state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a valid state, or the state does not match the content.</exception>
    public static WorldState Read(ReadOnlyMemory<byte> data, ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);

        WorldState? state;
        try
        {
            var reader = new MessagePackReader(data);
            state = MessagePackSerializer.Deserialize<WorldState>(ref reader, SimResolver.Options);
            if (!reader.End)
            {
                throw new InvalidDataException("The state is followed by unexpected extra bytes.");
            }
        }
        catch (MessagePackSerializationException exception)
        {
            throw new InvalidDataException("The bytes are not a valid world state.", exception);
        }

        if (state is null)
        {
            throw new InvalidDataException("The bytes hold no world state.");
        }

        state.Validate(content);
        return state;
    }

    /// <summary>Computes the state hash: XxHash64 over the bytes <see cref="Write"/> produces.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    public static ulong ComputeHash(WorldState state)
    {
        var hasher = new StableHasher();
        hasher.Add(Write(state));
        return hasher.ToUInt64();
    }
}
