using System.Diagnostics.CodeAnalysis;
using Evenmere.Core.Ids;

namespace Evenmere.Core.Collections;

/// <summary>
/// Stores one value per <see cref="EntityId"/> in contiguous memory, ordered by ID. Iteration is always in
/// ascending ID order (ADR-0003) and never allocates, and a lookup is a binary search. IDs are allocated
/// monotonically, so adds normally append in O(1); an out-of-order add shifts later entries.
/// </summary>
/// <remarks>
/// <para>
/// Removing an entry leaves a tombstone: the slot keeps its ID but holds no value, and iteration skips it.
/// <see cref="Remove"/> never moves a slot, so removing while enumerating is safe.
/// </para>
/// <para>
/// <see cref="Add"/> reclaims tombstones once they outnumber the live entries (with a floor of 32), shifting
/// live entries down in order, so a table that churns IDs stays proportional to its live size. Adding a removed ID
/// again either reuses its tombstone or, after a compaction, inserts it afresh; the result is the same. Because an
/// <see cref="Add"/> can move entries, it invalidates every reference returned by <see cref="GetRef"/> and every
/// active enumeration: adding while enumerating makes the enumerator's next <c>MoveNext</c> throw
/// <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">The value type. Use a struct and <see cref="GetRef"/> to update in place.</typeparam>
public sealed class IdTable<T>
{
    // Tombstones are tolerated up to this many even in a tiny table, so small tables do not compact on every Add.
    private const int CompactionFloor = 32;

    private Slot[] _slots;
    private int _used;
    private int _count;
    private int _version;

    /// <summary>Initializes a new instance of the <see cref="IdTable{T}"/> class that is empty.</summary>
    public IdTable()
    {
        _slots = [];
    }

    /// <summary>Initializes a new instance of the <see cref="IdTable{T}"/> class with room for entries before it must grow.</summary>
    /// <param name="capacity">The number of entries to reserve room for.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public IdTable(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _slots = capacity == 0 ? [] : new Slot[capacity];
    }

    /// <summary>Gets the number of entries, not counting removed ones.</summary>
    public int Count => _count;

    /// <summary>Gets the number of slots in use, tombstones included. Lets tests check that churn stays bounded.</summary>
    internal int SlotCountForTests => _used;

    /// <summary>
    /// Adds a value under an ID that has no live entry. This may reclaim tombstones, so it invalidates any
    /// reference from <see cref="GetRef"/> and any active enumeration.
    /// </summary>
    /// <param name="id">The ID to add; it must not be <see cref="EntityId.None"/> or negative.</param>
    /// <param name="value">The value to store.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not positive.</exception>
    /// <exception cref="ArgumentException">The ID already has a live entry.</exception>
    public void Add(EntityId id, T value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id.Value, nameof(id));
        int key = id.Value;

        if (_used - _count > Math.Max(CompactionFloor, _count))
        {
            Compact();
        }

        if (_used == 0 || key > _slots[_used - 1].Key)
        {
            Insert(_used, key, value);
            return;
        }

        int index = Find(key);
        if (index >= 0)
        {
            ref Slot slot = ref _slots[index];
            if (slot.Live)
            {
                throw new ArgumentException($"{id} is already in the table.", nameof(id));
            }

            slot.Value = value;
            slot.Live = true;
            _count++;
            _version++;
            return;
        }

        Insert(~index, key, value);
    }

    /// <summary>Removes the entry for an ID, leaving a tombstone.</summary>
    /// <param name="id">The ID to remove.</param>
    /// <returns><see langword="true"/> if there was an entry to remove.</returns>
    public bool Remove(EntityId id)
    {
        int index = Find(id.Value);
        if (index < 0 || !_slots[index].Live)
        {
            return false;
        }

        // Replacing the whole slot also releases any reference the value held.
        _slots[index] = new Slot { Key = id.Value };
        _count--;
        return true;
    }

    /// <summary>Tests whether an ID has a live entry.</summary>
    /// <param name="id">The ID to look for.</param>
    /// <returns><see langword="true"/> if the ID has an entry.</returns>
    public bool Contains(EntityId id)
    {
        int index = Find(id.Value);
        return index >= 0 && _slots[index].Live;
    }

    /// <summary>Looks up the value for an ID.</summary>
    /// <param name="id">The ID to look for.</param>
    /// <param name="value">The value, or the default when there is no entry.</param>
    /// <returns><see langword="true"/> if the ID has an entry.</returns>
    public bool TryGet(EntityId id, [MaybeNullWhen(false)] out T value)
    {
        int index = Find(id.Value);
        if (index >= 0 && _slots[index].Live)
        {
            value = _slots[index].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets a reference to the stored value, so a struct can be updated in place. The reference is invalid after
    /// the next <see cref="Add"/>, which may grow the storage or compact it; <see cref="Remove"/> does not move
    /// anything, but the reference then points at a tombstone.
    /// </summary>
    /// <param name="id">The ID to look up.</param>
    /// <returns>A reference to the value.</returns>
    /// <exception cref="KeyNotFoundException">The ID has no live entry.</exception>
    public ref T GetRef(EntityId id)
    {
        int index = Find(id.Value);
        if (index < 0 || !_slots[index].Live)
        {
            throw new KeyNotFoundException($"{id} is not in the table.");
        }

        return ref _slots[index].Value;
    }

    /// <summary>Starts an allocation-free enumeration in ascending ID order, skipping removed entries.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    private void Insert(int index, int key, T value)
    {
        if (_used == _slots.Length)
        {
            Array.Resize(ref _slots, Math.Max(4, _slots.Length * 2));
        }

        if (index < _used)
        {
            Array.Copy(_slots, index, _slots, index + 1, _used - index);
        }

        _slots[index] = new Slot { Key = key, Live = true, Value = value };
        _used++;
        _count++;
        _version++;
    }

    // Slides live slots down over the tombstones, keeping their order, then clears the freed tail so the
    // stale copies left behind do not keep their values reachable.
    private void Compact()
    {
        int write = 0;
        for (int read = 0; read < _used; read++)
        {
            if (_slots[read].Live)
            {
                if (write != read)
                {
                    _slots[write] = _slots[read];
                }

                write++;
            }
        }

        Array.Clear(_slots, write, _used - write);
        _used = write;
        _version++;
    }

    // Binary search over every slot, tombstones included (they keep their key). Returns the index, or the
    // bitwise complement of the insertion point when the key is absent.
    private int Find(int key)
    {
        int low = 0;
        int high = _used - 1;
        while (low <= high)
        {
            int mid = (int)((uint)(low + high) >> 1);
            int midKey = _slots[mid].Key;
            if (midKey == key)
            {
                return mid;
            }

            if (midKey < key)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return ~low;
    }

    private struct Slot
    {
        public int Key;
        public bool Live;
        public T Value;
    }

    /// <summary>One entry seen during enumeration: its ID and a reference to its value.</summary>
    public readonly ref struct Entry
    {
        private readonly ref T _value;

        internal Entry(EntityId id, ref T value)
        {
            Id = id;
            _value = ref value;
        }

        /// <summary>Gets the entry's ID.</summary>
        public EntityId Id { get; }

        /// <summary>Gets a reference to the entry's value, which can be read or updated in place.</summary>
        public ref T Value => ref _value;
    }

    /// <summary>
    /// Enumerates a table in ascending ID order without allocating. Removing entries during the enumeration is
    /// safe; an <see cref="Add"/> makes the next <see cref="MoveNext"/> throw.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly IdTable<T> _table;
        private readonly int _version;
        private int _index;

        internal Enumerator(IdTable<T> table)
        {
            _table = table;
            _version = table._version;
            _index = -1;
        }

        /// <summary>Gets the current entry.</summary>
        public readonly Entry Current
        {
            get
            {
                ref Slot slot = ref _table._slots[_index];
                return new Entry(new EntityId(slot.Key), ref slot.Value);
            }
        }

        /// <summary>Moves to the next live entry.</summary>
        /// <returns><see langword="true"/> if there is one.</returns>
        /// <exception cref="InvalidOperationException">An entry was added since the enumeration began.</exception>
        public bool MoveNext()
        {
            if (_table._version != _version)
            {
                throw new InvalidOperationException("The table was modified during enumeration.");
            }

            Slot[] slots = _table._slots;
            int used = _table._used;
            int next = _index + 1;
            while (next < used && !slots[next].Live)
            {
                next++;
            }

            _index = next;
            return next < used;
        }
    }
}
