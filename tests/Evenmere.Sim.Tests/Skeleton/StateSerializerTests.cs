using System.IO.Hashing;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.State;
using Evenmere.Sim.Tests.Fixtures;
using MessagePack;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class StateSerializerTests
{
    private static WorldState NewState(ulong seed = 42) => WorldState.CreateNew(WorldBuilder.Content, seed);

    [Fact]
    public void Write_SameState_GivesIdenticalBytes()
    {
        WorldState state = NewState();

        StateSerializer.Write(state).ShouldBe(StateSerializer.Write(state));
        StateSerializer.Write(NewState()).ShouldBe(StateSerializer.Write(NewState()));
    }

    [Fact]
    public void Read_WrittenState_RoundTripsToIdenticalBytes()
    {
        WorldState state = NewState(99);
        state.Clock = new Evenmere.Core.Time.GameClock(4567);
        _ = state.EntityIds.Allocate();
        _ = state.EntityIds.Allocate();
        state.Storage.Stock[1] = Amount.Parse("3.5");
        _ = state.SystemRngs[(int)SystemId.Needs].NextUInt();

        byte[] bytes = StateSerializer.Write(state);
        WorldState restored = StateSerializer.Read(bytes, WorldBuilder.Content);

        restored.Seed.ShouldBe(99UL);
        restored.Clock.Tick.ShouldBe(4567);
        restored.EntityIds.NextValue.ShouldBe(3);
        restored.Storage.Stock.ShouldBe(state.Storage.Stock);
        restored.SystemRngs[(int)SystemId.Needs].State.ShouldBe(state.SystemRngs[(int)SystemId.Needs].State);
        StateSerializer.Write(restored).ShouldBe(bytes);
    }

    [Fact]
    public void Write_State_IsAnArrayWithOneIntegerKeyPerMember()
    {
        byte[] bytes = StateSerializer.Write(NewState());

        // 22 members (keys 0 to 21), so a MessagePack array16 header: 0xDC then 22 as two bytes. A string-keyed object would be a map.
        bytes[..3].ShouldBe([0xDC, 0x00, 0x16]);
    }

    [Fact]
    public void ComputeHash_State_IsXxHash64OfTheWrittenBytes()
    {
        WorldState state = NewState();

        StateSerializer.ComputeHash(state).ShouldBe(XxHash64.HashToUInt64(StateSerializer.Write(state)));
    }

    [Fact]
    public void ComputeHash_AnyMemberChanging_ChangesTheHash()
    {
        ulong baseline = StateSerializer.ComputeHash(NewState());
        var hashes = new List<ulong>
        {
            StateSerializer.ComputeHash(NewState(43)),
            StateSerializer.ComputeHash(Modified(static s => s.Clock = new Evenmere.Core.Time.GameClock(1))),
            StateSerializer.ComputeHash(Modified(static s => _ = s.EntityIds.Allocate())),
            StateSerializer.ComputeHash(Modified(static s => _ = s.SystemRngs[0].NextUInt())),
            StateSerializer.ComputeHash(Modified(static s => s.Storage.Stock[0] = Amount.FromMilli(1))),
        };

        hashes.ShouldAllBe(hash => hash != baseline);
        hashes.Distinct().Count().ShouldBe(hashes.Count);
    }

    private static WorldState Modified(Action<WorldState> change)
    {
        WorldState state = NewState();
        change(state);
        return state;
    }

    [Fact]
    public void Read_BytesWithTrailingData_Throws()
    {
        byte[] bytes = [.. StateSerializer.Write(NewState()), 0x00];

        Should.Throw<InvalidDataException>(() => StateSerializer.Read(bytes, WorldBuilder.Content));
    }

    [Fact]
    public void Read_TruncatedBytes_Throws()
    {
        byte[] bytes = StateSerializer.Write(NewState());

        Should.Throw<InvalidDataException>(() => StateSerializer.Read(bytes.AsMemory(0, bytes.Length / 2), WorldBuilder.Content));
    }

    [Fact]
    public void Read_GarbageAndEmptyInput_Throw()
    {
        Should.Throw<InvalidDataException>(() => StateSerializer.Read(new byte[] { 0xC1, 0xFF, 0x00 }, WorldBuilder.Content));
        Should.Throw<InvalidDataException>(() => StateSerializer.Read(ReadOnlyMemory<byte>.Empty, WorldBuilder.Content));
        Should.Throw<InvalidDataException>(() => StateSerializer.Read(new byte[] { 0xC0 }, WorldBuilder.Content));
    }

    [Fact]
    public void Read_StorageSizedForOtherContent_Throws()
    {
        WorldState state = NewState();
        state.Storage.Stock = new Amount[WorldBuilder.Content.Goods.Count + 1];

        Should.Throw<InvalidDataException>(() => StateSerializer.Read(StateSerializer.Write(state), WorldBuilder.Content));
    }

    [Fact]
    public void Read_NegativeStock_Throws()
    {
        WorldState state = NewState();
        state.Storage.Stock[0] = Amount.FromMilli(-1);

        Should.Throw<InvalidDataException>(() => StateSerializer.Read(StateSerializer.Write(state), WorldBuilder.Content));
    }

    [Fact]
    public void Read_MissingSystemState_Throws()
    {
        WorldState state = NewState();
        state.Production = null!; // deliberately corrupt: a damaged file can carry nil for any member

        Should.Throw<InvalidDataException>(() => StateSerializer.Read(StateSerializer.Write(state), WorldBuilder.Content));
    }

    [Fact]
    public void Read_WrongNumberOfRandomStreams_Throws()
    {
        WorldState state = NewState();
        state.SystemRngs = [.. state.SystemRngs.Take(SystemNames.Count - 1)];

        Should.Throw<InvalidDataException>(() => StateSerializer.Read(StateSerializer.Write(state), WorldBuilder.Content));
    }

    [Fact]
    public void Read_CorruptRandomStream_Throws()
    {
        WorldState state = NewState();
        byte[] bytes = StateSerializer.Write(state);

        // The Pcg32 formatter rejects an even increment; flip the low bit of the first stream's increment in the bytes.
        Pcg32 first = state.SystemRngs[0];
        byte[] needle = MessagePackSerializer.Serialize(first.Inc, SimResolver.Options, TestContext.Current.CancellationToken);
        int at = bytes.AsSpan().IndexOf(needle);
        at.ShouldBeGreaterThan(0);
        bytes[at + needle.Length - 1] ^= 0x01;

        Should.Throw<InvalidDataException>(() => StateSerializer.Read(bytes, WorldBuilder.Content));
    }

    [Fact]
    public void Read_NullContent_Throws()
    {
        Should.Throw<ArgumentNullException>(() => StateSerializer.Read(StateSerializer.Write(NewState()), null!));
    }

    [Fact]
    public void Restore_ThenTick_BehavesLikeTheOriginal()
    {
        Simulation sim = new WorldBuilder().Build();
        for (int i = 0; i < 300; i++)
        {
            sim.Tick();
        }

        var restored = Simulation.Restore(WorldBuilder.Content, sim.SerializeState());

        for (int i = 0; i < 300; i++)
        {
            sim.Tick();
            restored.Tick();
            restored.ComputeStateHash().ShouldBe(sim.ComputeStateHash());
        }
    }

    [Fact]
    public void CreateNew_NewGame_IsAnEmptyWorldAtTickZero()
    {
        WorldState state = NewState(7);

        state.Seed.ShouldBe(7UL);
        state.Clock.Tick.ShouldBe(0);
        state.EntityIds.NextValue.ShouldBe(1);
        state.SystemRngs.Length.ShouldBe(SystemNames.Count);
        state.Storage.Stock.Length.ShouldBe(WorldBuilder.Content.Goods.Count);
        state.Storage.Stock.ShouldAllBe(held => held == Amount.Zero);
    }

    [Fact]
    public void CreateNew_SameInputs_GiveEqualBytes()
    {
        StateSerializer.Write(NewState(5)).ShouldBe(StateSerializer.Write(NewState(5)));
        StateSerializer.Write(NewState(5)).ShouldNotBe(StateSerializer.Write(NewState(6)));
    }
}
