//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

/// <summary>
/// Fixed native bucket chains for frame snapshots. Hashes only select candidates;
/// callers must compare the complete value, including when hashes collide.
/// Owns its buffers and must not be copied after construction.
/// </summary>
internal struct GraphicsSnapshotIndex : IDisposable
{
    private NativeArray<int> _heads;
    private NativeArray<int> _next;

    public GraphicsSnapshotIndex(int capacity)
    {
        if (capacity is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        var buckets = (int)BitOperations.RoundUpToPowerOf2((uint)capacity * 2);
        _heads = new NativeArray<int>(buckets, buckets);
        _next = new NativeArray<int>(capacity);
        _heads.AsSpan().Clear();
    }

    public readonly int First(ulong hash) => _heads[(int)(hash & (uint)(_heads.Length - 1))] - 1;
    public readonly int Next(int index) => _next[index] - 1;

    // Snapshot indices are sequential and match the corresponding snapshot array.
    public void Add(ulong hash, int index)
    {
        if (index != _next.Length || !_next.CheckCapacity(1))
            throw new InvalidOperationException("Invalid graphics snapshot index or exhausted capacity.");
        ref var head = ref _heads[(int)(hash & (uint)(_heads.Length - 1))];
        _next.Add(head);
        head = index + 1;
    }

    public void Clear()
    {
        if (_next.Length == 0) return;
        _heads.AsSpan().Clear();
        _next.Clear();
    }

    public void Dispose()
    {
        _heads.Dispose();
        _next.Dispose();
    }

    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        // Word-wise FNV mixing followed by an avalanche so bucket selection
        // includes high bits (notably the exponent/sign bits of float values).
        unchecked
        {
            var hash = 14695981039346656037UL ^ (ulong)data.Length;
            while (data.Length >= sizeof(ulong))
            {
                hash = (hash ^ MemoryMarshal.Read<ulong>(data)) * 1099511628211UL;
                data = data[sizeof(ulong)..];
            }
            foreach (var value in data) hash = (hash ^ value) * 1099511628211UL;
            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccdUL;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53UL;
            return hash ^ (hash >> 33);
        }
    }
}
