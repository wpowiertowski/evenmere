using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;
using Evenmere.Sim.Systems.Storage;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Skeleton;

public sealed class StorageTests
{
    private static readonly GoodId _first = new(0);
    private static readonly GoodId _second = new(1);
    private static readonly GoodId _third = new(2);

    private static StoragePool NewPool(int goods = 3) => new(StorageState.ForGoodCount(goods));

    [Fact]
    public void Get_NewPool_IsZeroForEveryGood()
    {
        StoragePool pool = NewPool();

        pool.GoodCount.ShouldBe(3);
        pool.Get(_first).ShouldBe(Amount.Zero);
        pool.Get(_second).ShouldBe(Amount.Zero);
        pool.Get(_third).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Add_Goods_RaisesOnlyThatGood()
    {
        StoragePool pool = NewPool();

        pool.Add(_second, Amount.FromUnits(5));
        pool.Add(_second, Amount.Parse("0.5"));

        pool.Get(_second).ShouldBe(Amount.Parse("5.5"));
        pool.Get(_first).ShouldBe(Amount.Zero);
        pool.Get(_third).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void TryTake_EnoughStock_TakesTheWholeAmount()
    {
        StoragePool pool = NewPool();
        pool.Add(_first, Amount.FromUnits(10));

        bool taken = pool.TryTake(_first, Amount.FromUnits(4));

        taken.ShouldBeTrue();
        pool.Get(_first).ShouldBe(Amount.FromUnits(6));
    }

    [Fact]
    public void TryTake_ExactStock_EmptiesTheGood()
    {
        StoragePool pool = NewPool();
        pool.Add(_first, Amount.FromUnits(3));

        pool.TryTake(_first, Amount.FromUnits(3)).ShouldBeTrue();

        pool.Get(_first).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void TryTake_NotEnoughStock_TakesNothing()
    {
        StoragePool pool = NewPool();
        pool.Add(_first, Amount.Parse("2.999"));

        bool taken = pool.TryTake(_first, Amount.FromUnits(3));

        taken.ShouldBeFalse();
        pool.Get(_first).ShouldBe(Amount.Parse("2.999"));
    }

    [Fact]
    public void TryTake_EmptyPool_FailsAndStaysAtZero()
    {
        StoragePool pool = NewPool();

        pool.TryTake(_third, Amount.FromMilli(1)).ShouldBeFalse();

        pool.Get(_third).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void TryTake_Zero_AlwaysSucceeds()
    {
        StoragePool pool = NewPool();

        pool.TryTake(_first, Amount.Zero).ShouldBeTrue();

        pool.Get(_first).ShouldBe(Amount.Zero);
    }

    [Fact]
    public void Stock_AfterAnySequenceOfCalls_IsNeverNegative()
    {
        StoragePool pool = NewPool();
        var rng = new Pcg32(9, 1);

        for (int i = 0; i < 1000; i++)
        {
            var good = new GoodId((ushort)rng.NextInt(3));
            var amount = Amount.FromMilli(rng.NextInt(0, 5000));
            if (rng.NextInt(2) == 0)
            {
                pool.Add(good, amount);
            }
            else
            {
                _ = pool.TryTake(good, amount);
            }

            pool.Get(good).ShouldBeGreaterThanOrEqualTo(Amount.Zero);
        }
    }

    [Fact]
    public void Add_NegativeAmount_Throws()
    {
        StoragePool pool = NewPool();

        Should.Throw<ArgumentOutOfRangeException>(() => pool.Add(_first, Amount.FromMilli(-1)));
    }

    [Fact]
    public void TryTake_NegativeAmount_Throws()
    {
        StoragePool pool = NewPool();
        pool.Add(_first, Amount.FromUnits(1));

        Should.Throw<ArgumentOutOfRangeException>(() => pool.TryTake(_first, Amount.FromMilli(-1)));
        pool.Get(_first).ShouldBe(Amount.FromUnits(1));
    }

    [Fact]
    public void Add_Overflow_ThrowsAndLeavesTheStockUnchanged()
    {
        StoragePool pool = NewPool();
        pool.Add(_first, Amount.FromMilli(long.MaxValue));

        Should.Throw<OverflowException>(() => pool.Add(_first, Amount.FromMilli(1)));

        pool.Get(_first).ShouldBe(Amount.FromMilli(long.MaxValue));
    }

    [Fact]
    public void Pool_GoodWithoutASlot_Throws()
    {
        StoragePool pool = NewPool();
        var missing = new GoodId(3);

        Should.Throw<ArgumentOutOfRangeException>(() => pool.Get(missing));
        Should.Throw<ArgumentOutOfRangeException>(() => pool.Add(missing, Amount.FromUnits(1)));
        Should.Throw<ArgumentOutOfRangeException>(() => pool.TryTake(missing, Amount.FromUnits(1)));
    }

    [Fact]
    public void NewWorld_Storage_IsSizedFromTheContentsGoods()
    {
        Simulation sim = new WorldBuilder().Build();

        sim.World.Storage.Stock.Length.ShouldBe(WorldBuilder.Content.Goods.Count);
        sim.Context.Storage.GoodCount.ShouldBe(WorldBuilder.Content.Goods.Count);
        WorldBuilder.Content.Goods.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Query_Stock_ReflectsTheWriteService()
    {
        Simulation sim = new WorldBuilder().Build();
        GoodId wood = WorldBuilder.Content.IdOf<GoodId>("wood");

        sim.Query.Storage.Stock(wood).ShouldBe(Amount.Zero);
        sim.Context.Storage.Add(wood, Amount.FromUnits(12));

        sim.Query.Storage.Stock(wood).ShouldBe(Amount.FromUnits(12));
        sim.Context.Storage.TryTake(wood, Amount.FromUnits(2)).ShouldBeTrue();
        sim.Query.Storage.Stock(wood).ShouldBe(Amount.FromUnits(10));
    }

    [Fact]
    public void Query_StockOfUnknownGood_Throws()
    {
        Simulation sim = new WorldBuilder().Build();
        var unknown = new GoodId((ushort)WorldBuilder.Content.Goods.Count);

        Should.Throw<ArgumentOutOfRangeException>(() => sim.Query.Storage.Stock(unknown));
    }

    [Fact]
    public void Stock_ChangesTheStateHash_AndSurvivesARoundTrip()
    {
        Simulation sim = new WorldBuilder().Build();
        GoodId fish = WorldBuilder.Content.IdOf<GoodId>("fish");
        ulong empty = sim.ComputeStateHash();

        sim.Context.Storage.Add(fish, Amount.Parse("7.25"));

        sim.ComputeStateHash().ShouldNotBe(empty);
        var restored = Simulation.Restore(WorldBuilder.Content, sim.SerializeState());
        restored.Query.Storage.Stock(fish).ShouldBe(Amount.Parse("7.25"));
        restored.ComputeStateHash().ShouldBe(sim.ComputeStateHash());
    }

    [Fact]
    public void Restore_Pool_IsIndependentOfTheOriginalSimulation()
    {
        Simulation sim = new WorldBuilder().Build();
        GoodId timber = WorldBuilder.Content.IdOf<GoodId>("timber");
        sim.Context.Storage.Add(timber, Amount.FromUnits(4));

        var restored = Simulation.Restore(WorldBuilder.Content, sim.SerializeState());
        restored.Context.Storage.TryTake(timber, Amount.FromUnits(1)).ShouldBeTrue();

        restored.Query.Storage.Stock(timber).ShouldBe(Amount.FromUnits(3));
        sim.Query.Storage.Stock(timber).ShouldBe(Amount.FromUnits(4));
    }
}
