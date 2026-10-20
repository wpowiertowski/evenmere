using Evenmere.Core.Ids;

namespace Evenmere.Core.Tests.Ids;

public sealed class EntityIdTests
{
    [Fact]
    public void Default_IsNone()
    {
        default(EntityId).IsNone.ShouldBeTrue();
        EntityId.None.Value.ShouldBe(0);
        new EntityId(1).IsNone.ShouldBeFalse();
    }

    [Fact]
    public void Compare_DifferentIds_OrdersByValue()
    {
        EntityId low = new(3);
        EntityId high = new(9);

        (low < high).ShouldBeTrue();
        (high > low).ShouldBeTrue();
        (low <= new EntityId(3)).ShouldBeTrue();
        (high >= new EntityId(9)).ShouldBeTrue();
        (low >= high).ShouldBeFalse();
        (high <= low).ShouldBeFalse();
        (high < low).ShouldBeFalse();
        low.CompareTo(high).ShouldBeLessThan(0);
        high.CompareTo(low).ShouldBeGreaterThan(0);
        low.CompareTo(low).ShouldBe(0);
    }

    [Fact]
    public void Equality_SameValue_IsEqual()
    {
        new EntityId(7).ShouldBe(new EntityId(7));
        new EntityId(7).ShouldNotBe(new EntityId(8));
    }

    [Fact]
    public void ToString_Id_NamesTypeAndValue()
    {
        new EntityId(42).ToString().ShouldBe("EntityId(42)");
    }

    [Fact]
    public void Sort_UnorderedIds_AscendingByValue()
    {
        EntityId[] ids = [new(5), new(1), new(9), new(3)];

        Array.Sort(ids);

        ids.ShouldBe([new EntityId(1), new EntityId(3), new EntityId(5), new EntityId(9)]);
    }
}
