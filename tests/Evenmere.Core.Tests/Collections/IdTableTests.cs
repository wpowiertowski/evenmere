using System.Runtime.CompilerServices;
using Evenmere.Core.Collections;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Core.Rng;

namespace Evenmere.Core.Tests.Collections;

public sealed class IdTableTests
{
    [Fact]
    public void NewTable_IsEmpty()
    {
        IdTable<int> table = new();

        table.Count.ShouldBe(0);
        table.Contains(new EntityId(1)).ShouldBeFalse();
        CollectIds(table).ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithCapacity_StartsEmptyAndGrowsPastIt()
    {
        IdTable<int> table = new(2);

        for (int i = 1; i <= 100; i++)
        {
            table.Add(new EntityId(i), i * 10);
        }

        table.Count.ShouldBe(100);
        table.TryGet(new EntityId(100), out int value).ShouldBeTrue();
        value.ShouldBe(1_000);
    }

    [Fact]
    public void Constructor_NegativeCapacity_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new IdTable<int>(-1));
    }

    [Fact]
    public void Add_NewId_IsFoundWithItsValue()
    {
        IdTable<string> table = new();

        table.Add(new EntityId(5), "five");

        table.Count.ShouldBe(1);
        table.Contains(new EntityId(5)).ShouldBeTrue();
        table.TryGet(new EntityId(5), out string? value).ShouldBeTrue();
        value.ShouldBe("five");
    }

    [Fact]
    public void Add_DuplicateLiveId_ThrowsAndKeepsOriginal()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(3), 30);

        Should.Throw<ArgumentException>(() => table.Add(new EntityId(3), 99));

        table.Count.ShouldBe(1);
        table.TryGet(new EntityId(3), out int value).ShouldBeTrue();
        value.ShouldBe(30);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Add_NoneOrNegativeId_Throws(int raw)
    {
        IdTable<int> table = new();

        Should.Throw<ArgumentOutOfRangeException>(() => table.Add(new EntityId(raw), 1));
    }

    [Fact]
    public void Remove_LiveId_RemovesAndReportsTrue()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 10);
        table.Add(new EntityId(2), 20);

        table.Remove(new EntityId(1)).ShouldBeTrue();

        table.Count.ShouldBe(1);
        table.Contains(new EntityId(1)).ShouldBeFalse();
        table.Contains(new EntityId(2)).ShouldBeTrue();
    }

    [Fact]
    public void Remove_MissingOrAlreadyRemovedId_ReportsFalse()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 10);
        table.Remove(new EntityId(1));

        table.Remove(new EntityId(1)).ShouldBeFalse();
        table.Remove(new EntityId(77)).ShouldBeFalse();
        table.Remove(EntityId.None).ShouldBeFalse();
        table.Count.ShouldBe(0);
    }

    [Fact]
    public void Add_AfterRemove_RevivesTheIdWithTheNewValue()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 10);
        table.Add(new EntityId(2), 20);
        table.Add(new EntityId(3), 30);
        table.Remove(new EntityId(2));

        table.Add(new EntityId(2), 222);

        table.Count.ShouldBe(3);
        table.TryGet(new EntityId(2), out int value).ShouldBeTrue();
        value.ShouldBe(222);
        CollectIds(table).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void TryGet_MissingOrRemovedId_ReturnsFalseWithDefault()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(4), 40);
        table.Remove(new EntityId(4));

        table.TryGet(new EntityId(4), out int removed).ShouldBeFalse();
        table.TryGet(new EntityId(9), out int missing).ShouldBeFalse();
        table.TryGet(EntityId.None, out int none).ShouldBeFalse();

        removed.ShouldBe(0);
        missing.ShouldBe(0);
        none.ShouldBe(0);
    }

    [Fact]
    public void Remove_ReferenceValue_ReleasesTheReference()
    {
        IdTable<object> table = new();
        WeakReference weak = AddAndRemoveAnObject(table);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        weak.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public void GetRef_LiveId_UpdatesStructInPlace()
    {
        IdTable<Counter> table = new();
        table.Add(new EntityId(1), new Counter { Hits = 1 });
        table.Add(new EntityId(2), new Counter { Hits = 10 });

        table.GetRef(new EntityId(2)).Hits += 5;
        ref Counter second = ref table.GetRef(new EntityId(2));
        second.Total = 99;

        table.TryGet(new EntityId(2), out Counter stored).ShouldBeTrue();
        stored.Hits.ShouldBe(15);
        stored.Total.ShouldBe(99);
        table.GetRef(new EntityId(1)).Hits.ShouldBe(1);
    }

    [Fact]
    public void GetRef_MissingOrRemovedId_ThrowsKeyNotFound()
    {
        IdTable<Counter> table = new();
        table.Add(new EntityId(1), default);
        table.Remove(new EntityId(1));

        Should.Throw<KeyNotFoundException>(() => table.GetRef(new EntityId(1)));
        Should.Throw<KeyNotFoundException>(() => table.GetRef(new EntityId(2)));
        Should.Throw<KeyNotFoundException>(() => table.GetRef(EntityId.None));
    }

    [Fact]
    public void Enumerate_InterleavedAddsAndRemoves_YieldsAscendingLiveIds()
    {
        IdTable<int> table = new();
        for (int i = 1; i <= 20; i++)
        {
            table.Add(new EntityId(i), i);
        }

        for (int i = 1; i <= 20; i += 2)
        {
            table.Remove(new EntityId(i));
        }

        for (int i = 21; i <= 30; i++)
        {
            table.Add(new EntityId(i), i);
            table.Remove(new EntityId(i - 20));
        }

        table.Add(new EntityId(3), 3);
        table.Add(new EntityId(7), 7);

        List<int> ids = CollectIds(table);

        // Odd ids 1-19 go first, then 2-10 as 21-30 arrive; 3 and 7 come back out of order.
        ids.ShouldBe([3, 7, 12, 14, 16, 18, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30]);
        table.Count.ShouldBe(ids.Count);
    }

    [Fact]
    public void Enumerate_OutOfOrderAdds_YieldsAscendingIds()
    {
        IdTable<int> table = new();
        foreach (int id in new[] { 50, 10, 30, 20, 40, 5, 60, 35 })
        {
            table.Add(new EntityId(id), id * 2);
        }

        List<int> ids = CollectIds(table);

        ids.ShouldBe([5, 10, 20, 30, 35, 40, 50, 60]);
        foreach (int id in ids)
        {
            table.TryGet(new EntityId(id), out int value).ShouldBeTrue();
            value.ShouldBe(id * 2);
        }
    }

    [Fact]
    public void Enumerate_Entries_PairEachIdWithItsOwnValue()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(2), 200);
        table.Add(new EntityId(4), 400);
        table.Add(new EntityId(6), 600);
        table.Remove(new EntityId(4));

        int seen = 0;
        foreach (IdTable<int>.Entry entry in table)
        {
            entry.Value.ShouldBe(entry.Id.Value * 100);
            seen++;
        }

        seen.ShouldBe(2);
    }

    [Fact]
    public void Enumerate_ValueRef_UpdatesStoredStructInPlace()
    {
        IdTable<Counter> table = new();
        for (int i = 1; i <= 5; i++)
        {
            table.Add(new EntityId(i), new Counter { Hits = i });
        }

        foreach (IdTable<Counter>.Entry entry in table)
        {
            entry.Value.Hits *= 10;
            entry.Value.Total += entry.Id.Value;
        }

        for (int i = 1; i <= 5; i++)
        {
            Counter stored = table.GetRef(new EntityId(i));
            stored.Hits.ShouldBe(i * 10);
            stored.Total.ShouldBe(i);
        }
    }

    [Fact]
    public void Enumerate_RemovingWhileEnumerating_SkipsRemovedAndVisitsTheRest()
    {
        IdTable<int> table = new();
        for (int i = 1; i <= 10; i++)
        {
            table.Add(new EntityId(i), i);
        }

        List<int> visited = [];
        foreach (IdTable<int>.Entry entry in table)
        {
            visited.Add(entry.Id.Value);
            if (entry.Id.Value % 2 == 0)
            {
                table.Remove(entry.Id);
                table.Remove(new EntityId(entry.Id.Value + 1));
            }
        }

        visited.ShouldBe([1, 2, 4, 6, 8, 10]);
        CollectIds(table).ShouldBe([1]);
        table.Count.ShouldBe(1);
    }

    [Fact]
    public void Enumerate_AllEntriesRemoved_YieldsNothing()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 1);
        table.Add(new EntityId(2), 2);
        table.Remove(new EntityId(1));
        table.Remove(new EntityId(2));

        CollectIds(table).ShouldBeEmpty();
        table.Count.ShouldBe(0);
    }

    [Fact]
    public void Enumerate_LargeTable_DoesNotAllocate()
    {
        IdTable<Counter> table = new();
        for (int i = 1; i <= 2_000; i++)
        {
            table.Add(new EntityId(i), new Counter { Hits = i });
        }

        for (int i = 1; i <= 2_000; i += 3)
        {
            table.Remove(new EntityId(i));
        }

        long warmUp = SumAndTouch(table);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long total = 0;
        for (int round = 0; round < 20; round++)
        {
            total += SumAndTouch(table);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        total.ShouldBeGreaterThan(warmUp);
    }

    [Fact]
    public void RandomOperations_AgainstSortedDictionary_AgreeOnEveryStepAcrossCompactions()
    {
        Pcg32 rng = new(2024, 7);
        IdTable<int> table = new();
        SortedDictionary<int, int> model = [];
        int compactions = 0;

        for (int step = 0; step < 12_000; step++)
        {
            // Alternate add-heavy and remove-heavy phases so tombstones pile up and then get reclaimed.
            Ratio addChance = (step / 400) % 2 == 0 ? Ratio.FromPercent(85) : Ratio.FromPercent(15);
            int id = rng.NextInt(1, 400);
            int value = rng.NextInt(0, 1_000_000);
            int slotsBefore = table.SlotCountForTests;

            if (rng.Chance(addChance))
            {
                if (model.TryAdd(id, value))
                {
                    table.Add(new EntityId(id), value);
                }
                else
                {
                    Should.Throw<ArgumentException>(() => table.Add(new EntityId(id), value));
                }
            }
            else
            {
                table.Remove(new EntityId(id)).ShouldBe(model.Remove(id));
            }

            // Slots are only ever released by a compaction inside Add.
            if (table.SlotCountForTests < slotsBefore)
            {
                compactions++;
            }

            table.Count.ShouldBe(model.Count);
            table.Contains(new EntityId(id)).ShouldBe(model.ContainsKey(id));

            if (step % 50 == 0)
            {
                CollectPairs(table).ShouldBe(model.Select(p => (p.Key, p.Value)).ToList());
            }
        }

        CollectPairs(table).ShouldBe(model.Select(p => (p.Key, p.Value)).ToList());
        foreach ((int key, int expected) in model)
        {
            table.TryGet(new EntityId(key), out int actual).ShouldBeTrue();
            actual.ShouldBe(expected);
        }

        compactions.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void Churn_AddNewIdThenRemoveOldest_KeepsSlotCountBounded()
    {
        const int Live = 10;
        const int Bound = (2 * 32) + 1;
        IdTable<int> table = new();
        int oldest = 1;
        int next = 1;
        int maxSlots = 0;

        for (; next <= Live - 1; next++)
        {
            table.Add(new EntityId(next), next);
        }

        for (int i = 0; i < 100_000; i++)
        {
            table.Add(new EntityId(next), next);
            next++;
            maxSlots = Math.Max(maxSlots, table.SlotCountForTests);

            table.Remove(new EntityId(oldest)).ShouldBeTrue();
            oldest++;
            maxSlots = Math.Max(maxSlots, table.SlotCountForTests);

            table.Count.ShouldBeLessThanOrEqualTo(Live);
        }

        maxSlots.ShouldBeLessThanOrEqualTo(Bound);
        table.Count.ShouldBe(Live - 1);
        CollectIds(table).ShouldBe(Enumerable.Range(oldest, Live - 1).ToList());
    }

    [Fact]
    public void Churn_LargerLiveSet_KeepsSlotCountWithinTwiceTheLiveSize()
    {
        const int Live = 500;
        IdTable<int> table = new();
        int oldest = 1;
        int next = 1;
        int maxSlots = 0;

        for (; next <= Live; next++)
        {
            table.Add(new EntityId(next), next);
        }

        for (int i = 0; i < 50_000; i++)
        {
            table.Remove(new EntityId(oldest)).ShouldBeTrue();
            oldest++;
            table.Add(new EntityId(next), next);
            next++;
            maxSlots = Math.Max(maxSlots, table.SlotCountForTests);
        }

        maxSlots.ShouldBeLessThanOrEqualTo((2 * Live) + 1);
        table.Count.ShouldBe(Live);
    }

    [Fact]
    public void Add_AfterManyRemovals_CompactsAndKeepsOrderAndValues()
    {
        IdTable<int> table = new();
        for (int i = 1; i <= 200; i++)
        {
            table.Add(new EntityId(i), i * 3);
        }

        // Keep every tenth entry: 20 live, 180 tombstones, far above the threshold.
        for (int i = 1; i <= 200; i++)
        {
            if (i % 10 != 0)
            {
                table.Remove(new EntityId(i));
            }
        }

        table.SlotCountForTests.ShouldBe(200);
        table.Add(new EntityId(201), 603);

        table.SlotCountForTests.ShouldBe(21);
        table.Count.ShouldBe(21);
        List<(int Id, int Value)> expected = Enumerable.Range(1, 20).Select(n => (n * 10, n * 30)).ToList();
        expected.Add((201, 603));
        CollectPairs(table).ShouldBe(expected);
        table.Contains(new EntityId(5)).ShouldBeFalse();
        table.GetRef(new EntityId(100)).ShouldBe(300);
    }

    [Fact]
    public void Add_RemovedIdAfterCompaction_ReinsertsInOrder()
    {
        IdTable<int> table = new();
        for (int i = 1; i <= 100; i++)
        {
            table.Add(new EntityId(i), i);
        }

        for (int i = 1; i <= 90; i++)
        {
            table.Remove(new EntityId(i));
        }

        table.Add(new EntityId(50), 5_000);

        table.SlotCountForTests.ShouldBe(11);
        CollectPairs(table).ShouldBe(
        [
            (50, 5_000), (91, 91), (92, 92), (93, 93), (94, 94), (95, 95), (96, 96), (97, 97), (98, 98), (99, 99), (100, 100),
        ]);
    }

    [Fact]
    public void Add_RemovedIdWithoutCompaction_ReusesTheTombstoneSlot()
    {
        IdTable<int> table = new();
        for (int i = 1; i <= 10; i++)
        {
            table.Add(new EntityId(i), i);
        }

        table.Remove(new EntityId(4));
        table.Add(new EntityId(4), 44);

        table.SlotCountForTests.ShouldBe(10);
        table.TryGet(new EntityId(4), out int value).ShouldBeTrue();
        value.ShouldBe(44);
    }

    [Fact]
    public void Remove_ManyEntries_NeverCompacts()
    {
        IdTable<int> table = new();
        for (int i = 1; i <= 100; i++)
        {
            table.Add(new EntityId(i), i);
        }

        for (int i = 1; i <= 100; i++)
        {
            table.Remove(new EntityId(i));
        }

        table.SlotCountForTests.ShouldBe(100);
        table.Count.ShouldBe(0);
    }

    [Fact]
    public void Add_Compaction_ReleasesReferencesHeldByMovedSlots()
    {
        IdTable<object> table = new();
        WeakReference moved = FillThenCompactKeepingOneObject(table);

        // The survivor now lives at slot 0; its old slot must not still point at it.
        table.SlotCountForTests.ShouldBe(2);
        table.Remove(new EntityId(100)).ShouldBeTrue();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        moved.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public void Enumerate_AddingDuringEnumeration_ThrowsInvalidOperationException()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 1);
        table.Add(new EntityId(2), 2);
        table.Add(new EntityId(3), 3);

        Should.Throw<InvalidOperationException>(() =>
        {
            foreach (IdTable<int>.Entry _ in table)
            {
                table.Add(new EntityId(10), 10);
            }
        });
    }

    [Fact]
    public void Enumerate_AddingOnTheLastEntry_StillThrowsOnTheNextMoveNext()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 1);

        Should.Throw<InvalidOperationException>(() =>
        {
            foreach (IdTable<int>.Entry _ in table)
            {
                table.Add(new EntityId(2), 2);
            }
        });
    }

    [Fact]
    public void Enumerate_ReviveDuringEnumeration_ThrowsInvalidOperationException()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 1);
        table.Add(new EntityId(2), 2);
        table.Remove(new EntityId(2));

        Should.Throw<InvalidOperationException>(() =>
        {
            foreach (IdTable<int>.Entry _ in table)
            {
                table.Add(new EntityId(2), 22);
            }
        });
    }

    [Fact]
    public void Enumerate_RemovingEveryEntryOfAChurnedTable_VisitsEachOnceAndNeverThrows()
    {
        IdTable<int> table = new();
        for (int i = 1; i <= 300; i++)
        {
            table.Add(new EntityId(i), i);
            if (i > 20)
            {
                table.Remove(new EntityId(i - 20));
            }
        }

        int visited = 0;
        foreach (IdTable<int>.Entry entry in table)
        {
            visited++;
            table.Remove(entry.Id);
        }

        visited.ShouldBe(20);
        table.Count.ShouldBe(0);
    }

    [Fact]
    public void Enumerate_NewEnumerationAfterAnAdd_Works()
    {
        IdTable<int> table = new();
        table.Add(new EntityId(1), 1);

        table.Add(new EntityId(2), 2);

        CollectIds(table).ShouldBe([1, 2]);
    }

    private static List<int> CollectIds<T>(IdTable<T> table)
    {
        List<int> ids = [];
        foreach (IdTable<T>.Entry entry in table)
        {
            ids.Add(entry.Id.Value);
        }

        return ids;
    }

    private static List<(int Id, int Value)> CollectPairs(IdTable<int> table)
    {
        List<(int Id, int Value)> pairs = [];
        foreach (IdTable<int>.Entry entry in table)
        {
            pairs.Add((entry.Id.Value, entry.Value));
        }

        return pairs;
    }

    private static long SumAndTouch(IdTable<Counter> table)
    {
        long sum = 0;
        foreach (IdTable<Counter>.Entry entry in table)
        {
            entry.Value.Total++;
            sum += entry.Id.Value + entry.Value.Total;
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference FillThenCompactKeepingOneObject(IdTable<object> table)
    {
        object survivor = new();
        for (int i = 1; i < 100; i++)
        {
            table.Add(new EntityId(i), new object());
        }

        table.Add(new EntityId(100), survivor);
        for (int i = 1; i < 100; i++)
        {
            table.Remove(new EntityId(i));
        }

        // 99 tombstones against one live entry: this Add compacts, sliding the survivor from slot 99 to slot 0.
        table.Add(new EntityId(101), new object());
        return new WeakReference(survivor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddAndRemoveAnObject(IdTable<object> table)
    {
        object payload = new();
        table.Add(new EntityId(1), payload);
        table.Remove(new EntityId(1));
        return new WeakReference(payload);
    }

    private struct Counter
    {
        public int Hits;
        public long Total;
    }
}
