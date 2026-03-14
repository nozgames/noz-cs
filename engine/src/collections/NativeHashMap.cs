//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoZ;

/// <summary>
/// Fixed-capacity unmanaged hash map using open addressing with linear probing.
/// Keys are ulong hashes. A key of 0 is reserved as the empty sentinel.
/// </summary>
public unsafe struct NativeHashMap<TValue> : IDisposable
    where TValue : unmanaged
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public int Capacity;
        public int Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Entry
    {
        public ulong Key;
        public TValue Value;
    }

    private void* _ptr;

    public readonly bool IsCreated => _ptr != null;

    private readonly Header* HeaderPtr => (Header*)_ptr;
    private readonly Entry* Entries => (Entry*)(((Header*)_ptr) + 1);

    public readonly int Capacity
    {
        get
        {
            if (!IsCreated) return 0;
            return HeaderPtr->Capacity;
        }
    }

    public readonly int Count
    {
        get
        {
            if (!IsCreated) return 0;
            return HeaderPtr->Count;
        }
        private set
        {
            Debug.Assert(IsCreated);
            HeaderPtr->Count = value;
        }
    }

    public NativeHashMap(int capacity)
    {
        // capacity must be power of 2 for cheap modulo
        Debug.Assert(capacity > 0);
        Debug.Assert((capacity & (capacity - 1)) == 0, "Capacity must be a power of 2.");

        _ptr = NativeMemory.AllocZeroed((nuint)(sizeof(Header) + sizeof(Entry) * capacity));
        HeaderPtr->Capacity = capacity;
        HeaderPtr->Count = 0;
    }

    /// <summary>
    /// Copies all entries from another map into this one.
    /// </summary>
    public NativeHashMap(NativeHashMap<TValue> other) : this(other.Capacity)
    {
        Debug.Assert(IsCreated);

        if (other.Count == 0)
            return;

        NativeMemory.Copy(
            other.Entries,
            Entries,
            (nuint)(sizeof(Entry) * other.Capacity)
        );
        HeaderPtr->Count = other.Count;
    }

    /// <summary>
    /// Try to get a reference to the value for the given key.
    /// Returns a null ref if not found.
    /// </summary>
    public readonly ref TValue TryGet(ulong key)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(key != 0, "Key 0 is reserved as the empty sentinel.");

        int slot = Slot(key);

        while (true)
        {
            ref Entry entry = ref Entries[slot];

            if (entry.Key == 0)
                return ref System.Runtime.CompilerServices.Unsafe.NullRef<TValue>();

            if (entry.Key == key)
                return ref entry.Value;

            slot = (slot + 1) & (Capacity - 1);
        }
    }

    /// <summary>
    /// Get or add an entry for the given key. Returns ref to value.
    /// isNew is set to true if the entry was just created.
    /// </summary>
    public ref TValue GetOrAdd(ulong key, out bool isNew)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(key != 0, "Key 0 is reserved as the empty sentinel.");
        Debug.Assert(Count < Capacity, "NativeHashMap has reached its capacity.");

        int slot = Slot(key);

        while (true)
        {
            ref Entry entry = ref Entries[slot];

            if (entry.Key == 0)
            {
                entry.Key = key;
                entry.Value = default;
                Count++;
                isNew = true;
                return ref entry.Value;
            }

            if (entry.Key == key)
            {
                isNew = false;
                return ref entry.Value;
            }

            slot = (slot + 1) & (Capacity - 1);
        }
    }

    /// <summary>
    /// Get or add an entry for the given key. Returns ref to value.
    /// </summary>
    public ref TValue GetOrAdd(ulong key)
    {
        return ref GetOrAdd(key, out _);
    }

    /// <summary>
    /// Returns true if the map contains the given key.
    /// </summary>
    public readonly bool ContainsKey(ulong key)
    {
        Debug.Assert(IsCreated);
        Debug.Assert(key != 0, "Key 0 is reserved as the empty sentinel.");

        return !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref TryGet(key));
    }

    /// <summary>
    /// Clears all entries without freeing memory.
    /// </summary>
    public void Clear()
    {
        Debug.Assert(IsCreated);
        NativeMemory.Clear(Entries, (nuint)(sizeof(Entry) * Capacity));
        Count = 0;
    }

    /// <summary>
    /// Swaps the contents of this map with another of the same capacity.
    /// The other map is cleared after the swap.
    /// Useful for double buffering - swap then clear to recycle previous frame.
    /// </summary>
    public void SwapAndClear(ref NativeHashMap<TValue> other)
    {
        Debug.Assert(IsCreated && other.IsCreated);
        Debug.Assert(Capacity == other.Capacity, "Cannot swap maps of different capacities.");

        void* tmp = _ptr;
        _ptr = other._ptr;
        other._ptr = tmp;

        other.Clear();
    }

    public ref TValue this[ulong key]
    {
        get
        {
            Debug.Assert(IsCreated);
            return ref GetOrAdd(key);
        }
    }

    public void Dispose()
    {
        if (!IsCreated) return;
        NativeMemory.Free(_ptr);
        _ptr = null;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private readonly int Slot(ulong key) => (int)(key & (ulong)(Capacity - 1));
}