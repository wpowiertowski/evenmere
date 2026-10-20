using System.Reflection;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;
using Evenmere.Core.Time;
using Evenmere.Sim.Serialization;
using Evenmere.Sim.Serialization.Formatters;
using MessagePack;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class FormatterTests
{
    private static readonly MessagePackSerializerOptions _options = SimResolver.Options;
    private static readonly int[][] _threeFieldPair = [[1, 2, 3]];

    private static byte[] Write<T>(T value) => MessagePackSerializer.Serialize(value, _options);

    private static T Read<T>(byte[] bytes) => MessagePackSerializer.Deserialize<T>(bytes, _options);

    [Fact]
    public void Amount_IsWrittenAsItsInt64Milli()
    {
        Amount.FromUnits(1).ShouldBe(Read<Amount>(Write(1000L)));
        Read<long>(Write(Amount.FromUnits(1))).ShouldBe(1000);

        // 1000 needs a uint16 on the wire: 0xCD 0x03 0xE8.
        Write(Amount.FromUnits(1)).ShouldBe([0xCD, 0x03, 0xE8]);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(123_456_789L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Amount_RoundTrips(long milli)
    {
        var amount = Amount.FromMilli(milli);

        Read<Amount>(Write(amount)).ShouldBe(amount);
    }

    [Fact]
    public void Ratio_IsWrittenAsItsInt32PerMille()
    {
        Read<int>(Write(Ratio.FromPerMille(1500))).ShouldBe(1500);
        Read<Ratio>(Write(-250)).ShouldBe(Ratio.FromPerMille(-250));
        Write(Ratio.One).ShouldBe([0xCD, 0x03, 0xE8]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Ratio_RoundTrips(int perMille)
    {
        var ratio = Ratio.FromPerMille(perMille);

        Read<Ratio>(Write(ratio)).ShouldBe(ratio);
    }

    [Fact]
    public void EntityId_IsWrittenAsItsInt32()
    {
        Read<int>(Write(new EntityId(77))).ShouldBe(77);
        Read<EntityId>(Write(1234567)).ShouldBe(new EntityId(1234567));
        Write(new EntityId(5)).ShouldBe([0x05]);
        Read<EntityId>(Write(EntityId.None)).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void ContentId_IsWrittenAsItsUInt16()
    {
        Read<ushort>(Write(new GoodId(7))).ShouldBe((ushort)7);
        Read<BuildingTypeId>(Write((ushort)65535)).ShouldBe(new BuildingTypeId(65535));
        Write(new TierId(3)).ShouldBe([0x03]);
    }

    [Fact]
    public void ContentId_EveryIdTypeInCore_HasAFormatter()
    {
        Type[] idTypes =
        [
            .. typeof(EntityId).Assembly.GetTypes().Where(static t =>
                t.IsValueType && t.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContentId<>))),
        ];

        idTypes.Length.ShouldBe(13);
        MethodInfo roundTrip = typeof(FormatterTests).GetMethod(nameof(RoundTripContentId), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (Type idType in idTypes)
        {
            bool ok = (bool)roundTrip.MakeGenericMethod(idType).Invoke(null, null)!;

            ok.ShouldBeTrue($"{idType.Name} must round-trip through the sim resolver");
        }
    }

    private static bool RoundTripContentId<TId>()
        where TId : struct, IContentId<TId>
    {
        foreach (ushort value in new ushort[] { 0, 1, 255, 256, ushort.MaxValue })
        {
            TId id = TId.From(value);
            if (!id.Equals(Read<TId>(Write(id))) || Read<ushort>(Write(id)) != value)
            {
                return false;
            }
        }

        return _options.Resolver.GetFormatterWithVerify<TId>() is ContentIdFormatter<TId>;
    }

    [Fact]
    public void Pcg32_IsWrittenAsStateAndIncAndContinuesTheSequence()
    {
        var rng = new Pcg32(42, 54);
        for (int i = 0; i < 17; i++)
        {
            _ = rng.NextUInt();
        }

        ulong[] pair = Read<ulong[]>(Write(rng));
        pair.ShouldBe([rng.State, rng.Inc]);

        Pcg32 restored = Read<Pcg32>(Write(rng));
        restored.State.ShouldBe(rng.State);
        restored.Inc.ShouldBe(rng.Inc);
        for (int i = 0; i < 100; i++)
        {
            restored.NextUInt().ShouldBe(rng.NextUInt());
        }
    }

    [Fact]
    public void Pcg32_EvenIncrement_IsRejected()
    {
        byte[] corrupt = Write(new ulong[] { 1, 2 });

        Should.Throw<MessagePackSerializationException>(() => Read<Pcg32>(corrupt));
    }

    [Fact]
    public void Pcg32_WrongNumberOfFields_IsRejected()
    {
        byte[] corrupt = Write(new ulong[] { 1, 3, 5 });

        Should.Throw<MessagePackSerializationException>(() => Read<Pcg32>(corrupt));
    }

    [Fact]
    public void GameClock_IsWrittenAsItsTick()
    {
        Read<long>(Write(new GameClock(4321))).ShouldBe(4321);
        Read<GameClock>(Write(99L)).ShouldBe(new GameClock(99));
        Read<GameClock>(Write(new GameClock(0))).ShouldBe(new GameClock(0));
    }

    [Fact]
    public void GameClock_NegativeTick_IsRejected()
    {
        Should.Throw<MessagePackSerializationException>(() => Read<GameClock>(Write(-1L)));
    }

    [Fact]
    public void EntityIdAllocator_IsWrittenAsItsNextValueAndContinues()
    {
        var allocator = new EntityIdAllocator();
        _ = allocator.Allocate();
        _ = allocator.Allocate();

        Read<int>(Write(allocator)).ShouldBe(3);

        EntityIdAllocator restored = Read<EntityIdAllocator>(Write(allocator));
        restored.NextValue.ShouldBe(3);
        restored.Allocate().ShouldBe(new EntityId(3));
        allocator.Allocate().ShouldBe(new EntityId(3));
    }

    [Fact]
    public void EntityIdAllocator_ZeroNextValue_IsRejected()
    {
        Should.Throw<MessagePackSerializationException>(() => Read<EntityIdAllocator>(Write(0)));
    }

    [Fact]
    public void IdTable_IsWrittenAsAscendingIdValuePairs()
    {
        var table = new IdTable<int>();
        table.Add(new EntityId(1), 10);
        table.Add(new EntityId(3), 30);
        table.Add(new EntityId(8), 80);

        Read<int[][]>(Write(table)).ShouldBe([[1, 10], [3, 30], [8, 80]]);
    }

    [Fact]
    public void IdTable_RoundTripsAndKeepsOrder()
    {
        var table = new IdTable<Amount>();
        table.Add(new EntityId(2), Amount.FromUnits(2));
        table.Add(new EntityId(5), Amount.FromUnits(5));
        table.Add(new EntityId(9), Amount.Parse("0.25"));

        IdTable<Amount> restored = Read<IdTable<Amount>>(Write(table));

        restored.Count.ShouldBe(3);
        List<(int Id, Amount Value)> entries = [];
        foreach (IdTable<Amount>.Entry entry in restored)
        {
            entries.Add((entry.Id.Value, entry.Value));
        }

        entries.ShouldBe([(2, Amount.FromUnits(2)), (5, Amount.FromUnits(5)), (9, Amount.Parse("0.25"))]);
    }

    [Fact]
    public void IdTable_RemovedEntriesAndOutOfOrderAdds_GiveTheSameBytesAsACleanTable()
    {
        var churned = new IdTable<int>();
        churned.Add(new EntityId(4), 40);
        churned.Add(new EntityId(1), 10);
        churned.Add(new EntityId(2), 20);
        churned.Add(new EntityId(3), 30);
        churned.Remove(new EntityId(2));
        churned.Remove(new EntityId(4));

        var clean = new IdTable<int>();
        clean.Add(new EntityId(1), 10);
        clean.Add(new EntityId(3), 30);

        Write(churned).ShouldBe(Write(clean));
    }

    [Fact]
    public void IdTable_EmptyAndNull()
    {
        Read<IdTable<int>>(Write(new IdTable<int>())).Count.ShouldBe(0);
        Write(new IdTable<int>()).ShouldBe([0x90]);
        Read<IdTable<int>?>(Write<IdTable<int>?>(null)).ShouldBeNull();
    }

    [Fact]
    public void IdTable_RestoredTable_AcceptsNewEntriesAfterTheLastId()
    {
        var table = new IdTable<int>();
        table.Add(new EntityId(1), 1);
        table.Add(new EntityId(2), 2);

        IdTable<int> restored = Read<IdTable<int>>(Write(table));
        restored.Add(new EntityId(3), 3);

        restored.Count.ShouldBe(3);
        restored.TryGet(new EntityId(3), out int three).ShouldBeTrue();
        three.ShouldBe(3);
    }

    [Fact]
    public void IdTable_NestedTables_RoundTrip()
    {
        var inner = new IdTable<int>();
        inner.Add(new EntityId(1), 11);
        var outer = new IdTable<IdTable<int>>();
        outer.Add(new EntityId(7), inner);

        IdTable<IdTable<int>> restored = Read<IdTable<IdTable<int>>>(Write(outer));

        restored.TryGet(new EntityId(7), out IdTable<int>? found).ShouldBeTrue();
        found!.TryGet(new EntityId(1), out int value).ShouldBeTrue();
        value.ShouldBe(11);
    }

    [Theory]
    [InlineData(new[] { 3, 1 }, "descending")]
    [InlineData(new[] { 2, 2 }, "duplicate")]
    [InlineData(new[] { 0 }, "zero")]
    [InlineData(new[] { -4 }, "negative")]
    public void IdTable_IdsNotStrictlyAscendingAndPositive_AreRejected(int[] ids, string because)
    {
        int[][] pairs = [.. ids.Select(static id => new[] { id, 0 })];

        Should.Throw<MessagePackSerializationException>(() => Read<IdTable<int>>(Write(pairs)), because);
    }

    [Fact]
    public void IdTable_PairWithTheWrongShape_IsRejected()
    {
        byte[] corrupt = Write(_threeFieldPair);

        Should.Throw<MessagePackSerializationException>(() => Read<IdTable<int>>(corrupt));
    }

    [Fact]
    public void Options_HaveNoCompression()
    {
        _options.Compression.ShouldBe(MessagePackCompression.None);
    }
}
