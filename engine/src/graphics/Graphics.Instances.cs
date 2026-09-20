// NoZ - Copyright(c) 2026 NoZ Games, LLC
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using NoZ.Platform;

namespace NoZ;

public static partial class Graphics
{
    private static readonly Dictionary<Type, InstanceStream> _instanceStreams = [];

    private abstract class InstanceStream : IDisposable
    {
        public abstract void BeginFrame();
        public abstract void Upload();
        public abstract void Dispose();
    }

    private sealed class InstanceStream<T> : InstanceStream where T : unmanaged, IVertex
    {
        private sealed class Page : IDisposable
        {
            public readonly nuint Handle;
            public NativeArray<T> Data;
            public int Uploaded;
            public Page(int capacity)
            {
                Handle = Driver.CreateMesh<T>(capacity, 0, BufferUsage.Stream, $"Instances.{typeof(T).Name}");
                try { Data = new NativeArray<T>(capacity); }
                catch { Driver.DestroyMesh(Handle); throw; }
            }
            public void Dispose() { Data.Dispose(); Driver.DestroyMesh(Handle); }
        }
        private readonly List<Page> _pages = [];
        private readonly int _stride;
        private int _count;

        public InstanceStream()
        {
            var descriptor = T.GetFormatDescriptor();
            _stride = Unsafe.SizeOf<T>();
            if (descriptor.Stride != _stride || (_stride & 3) != 0)
                throw new ArgumentException("Instance format must match the struct size and be four-byte aligned.");
            _ = checked(RenderConfig.MaxInstancesPerFrame * _stride + 8);
        }

        public (nuint Handle, int First) Append(ReadOnlySpan<T> data)
        {
            if (data.Length > RenderConfig.MaxInstancesPerFrame - _count)
                throw new InvalidOperationException($"Graphics instance budget exhausted ({RenderConfig.MaxInstancesPerFrame} per format per frame).");
            Page? page = null;
            foreach (var candidate in _pages)
                if (candidate.Data.CheckCapacity(data.Length)) { page = candidate; break; }
            if (page == null)
            {
                // Each page is immutable in size: a larger draw never destroys a
                // buffer referenced by earlier deferred commands or other views.
                // Geometric growth bounds retained pages; largest-first reuse
                // prevents a long-lived collection of equally sized small pages.
                var minimum = _pages.Count == 0 ? 256u : (uint)_pages[0].Data.Capacity * 2;
                var capacity = (int)Math.Min((uint)RenderConfig.MaxInstancesPerFrame,
                    Math.Max(minimum, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)data.Length)));
                page = new Page(capacity);
                try { _pages.Insert(0, page); }
                catch { page.Dispose(); throw; }
            }
            var first = page.Data.Length;
            page.Data.AddRange(data);
            _count += data.Length;
            return (page.Handle, first);
        }

        public void Rollback(nuint handle, int count)
        {
            foreach (var page in _pages)
                if (page.Handle == handle) { page.Data.RemoveLast(count); _count -= count; return; }
        }

        public override void BeginFrame()
        {
            _count = 0;
            foreach (var page in _pages) { page.Data.Clear(); page.Uploaded = 0; }
        }
        public override void Upload()
        {
            foreach (var page in _pages)
            {
                if (page.Uploaded == page.Data.Length) continue;
                var bytes = MemoryMarshal.AsBytes(page.Data.AsReadonlySpan());
                var offset = page.Uploaded * _stride;
                Driver.UpdateInstanceData(page.Handle, offset, bytes[offset..]);
                page.Uploaded = page.Data.Length;
            }
        }
        public override void Dispose() { foreach (var page in _pages) page.Dispose(); _pages.Clear(); }
    }

    /// <summary>
    /// Copies instance data for a deferred indexed draw of the current mesh.
    /// The shader supplies vs_instanced with slot-1 instance vertex attributes.
    /// Copies remain valid across every view/pass/flush until the frame ends.
    /// Each call is one sortable submission; individual instances are not depth-sorted.
    /// The mesh and shader must remain alive until the commands have executed.
    /// </summary>
    public static void DrawElementsInstanced<T>(int indexCount, ReadOnlySpan<T> instances,
        int indexOffset = 0, ushort order = 0) where T : unmanaged, IVertex
    {
        if (instances.IsEmpty || indexCount == 0) return;
        if (!Driver.SupportsInstancing)
            throw new NotSupportedException("This graphics driver does not support instancing.");
        if (indexCount < 0 || indexOffset < 0 || CurrentState.Mesh.Handle == 0 || CurrentState.Mesh == _mesh)
            throw new ArgumentException("Instancing requires an external indexed mesh and a valid index range.");
        _ = checked(indexOffset + indexCount);
        if (_commands.Length >= _maxDrawCommands)
            throw new InvalidOperationException("Graphics draw command budget exhausted.");
        if (!_instanceStreams.TryGetValue(typeof(T), out var untyped))
        {
            untyped = new InstanceStream<T>();
            _instanceStreams.Add(typeof(T), untyped);
        }
        var stream = (InstanceStream<T>)untyped;
        var range = stream.Append(instances);
        var previousStream = CurrentState.InstanceStream;
        try
        {
            SetInstanceStream(range.Handle);
            if (_batchStateDirty) AddBatchState();
            _commands.Add(new DrawCommand
            {
                SortKey = MakeSortKey(order),
                PassOrder = _currentPass == RenderPass.RenderTexture ? _rtPassIndex : byte.MaxValue,
                IndexOffset = indexOffset, IndexCount = indexCount, BatchState = _currentBatchState,
                FirstInstance = range.First, InstanceCount = instances.Length
            });
        }
        catch { stream.Rollback(range.Handle, instances.Length); throw; }
        finally { SetInstanceStream(previousStream); }
    }

    private static void SetInstanceStream(nuint stream)
    {
        if (CurrentState.InstanceStream == stream) return;
        CurrentState.InstanceStream = stream;
        _batchStateDirty = true;
    }
}
