using Evenmere.Core.Ids;

namespace Evenmere.Core.Tests.Ids;

public sealed class EntityIdAllocatorTests
{
    [Fact]
    public void Allocate_NewAllocator_StartsAtOne()
    {
        EntityIdAllocator allocator = new();

        allocator.NextValue.ShouldBe(1);
        allocator.Allocate().ShouldBe(new EntityId(1));
        allocator.Allocate().ShouldBe(new EntityId(2));
    }

    [Fact]
    public void Allocate_ManyTimes_IsStrictlyIncreasingAndNeverNone()
    {
        EntityIdAllocator allocator = new();
        EntityId previous = EntityId.None;

        for (int i = 0; i < 1_000; i++)
        {
            EntityId id = allocator.Allocate();
            id.IsNone.ShouldBeFalse();
            (id > previous).ShouldBeTrue();
            previous = id;
        }
    }

    [Fact]
    public void NextValue_AfterAllocations_IsOnePastTheLastId()
    {
        EntityIdAllocator allocator = new();

        allocator.Allocate();
        allocator.Allocate();
        EntityId last = allocator.Allocate();

        allocator.NextValue.ShouldBe(last.Value + 1);
    }

    [Fact]
    public void Constructor_SavedNextValue_ContinuesTheSequence()
    {
        EntityIdAllocator original = new();
        for (int i = 0; i < 10; i++)
        {
            original.Allocate();
        }

        EntityIdAllocator restored = new(original.NextValue);

        restored.Allocate().ShouldBe(original.Allocate());
        restored.Allocate().ShouldBe(original.Allocate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_NextValueBelowOne_Throws(int nextValue)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EntityIdAllocator(nextValue));
    }

    [Fact]
    public void Allocate_IdSpaceExhausted_ThrowsAndLeavesStateUnchanged()
    {
        EntityIdAllocator allocator = new(int.MaxValue);

        Should.Throw<OverflowException>(() => allocator.Allocate());

        allocator.NextValue.ShouldBe(int.MaxValue);
    }

    [Fact]
    public void Allocate_SecondToLastValue_Succeeds()
    {
        EntityIdAllocator allocator = new(int.MaxValue - 1);

        allocator.Allocate().ShouldBe(new EntityId(int.MaxValue - 1));
        allocator.NextValue.ShouldBe(int.MaxValue);
    }
}
