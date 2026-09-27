using NoZ.Platform;
using System.Runtime.InteropServices;

namespace NoZ;

/// <summary>A fixed-capacity GPU instance buffer. Update before its first draw
/// of a frame; draws borrow immutable ranges until that frame has completed.</summary>
public sealed class PersistentInstanceBuffer<T> : IDisposable where T : unmanaged, IVertex
{
    internal nuint Handle { get; private set; }
    internal long LastDrawFrame = -1;
    public int Capacity { get; }
    public long ByteCapacity => (long)Capacity * Marshal.SizeOf<T>();
    public bool CanUpdate => !Graphics.FrameOpen || LastDrawFrame != Graphics.FrameRevision;
    public PersistentInstanceBuffer(int capacity, string name = "Persistent instances")
    {
        if (capacity < 1 || capacity > int.MaxValue / Marshal.SizeOf<T>() || T.GetFormatDescriptor().Stride != Marshal.SizeOf<T>()) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        Handle = Graphics.Driver.CreateMesh<T>(capacity, 0, BufferUsage.Dynamic, name);
    }
    public void Update(int first, ReadOnlySpan<T> data)
    {
        ObjectDisposedException.ThrowIf(Handle == 0, this);
        if (first < 0 || data.Length > Capacity - first) throw new ArgumentOutOfRangeException(nameof(first));
        if (!CanUpdate) throw new InvalidOperationException("Update instance ranges before submitting this buffer in the frame.");
        Graphics.Driver.UpdateInstanceData(Handle, checked(first * Marshal.SizeOf<T>()), MemoryMarshal.AsBytes(data));
    }
    public void Dispose()
    {
        if (Handle == 0) return;
        var handle = Handle; Handle = 0;
        if (!CanUpdate) Graphics.AfterEndFrame += () => Graphics.Driver.DestroyMesh(handle);
        else Graphics.Driver.DestroyMesh(handle);
    }
}

public static partial class Graphics
{
    internal static long FrameRevision { get; private set; }
    internal static bool FrameOpen { get; private set; }
    private static int _persistentInstancesThisFrame;

    /// <summary>Submit resident instances without per-frame copying/uploading.
    /// Returns false at the frame-wide quality limit, shared by all views/passes.</summary>
    public static bool DrawElementsPersistent<T>(int indexCount, PersistentInstanceBuffer<T> buffer,
        int firstInstance, int instanceCount, int indexOffset = 0, ushort order = 0) where T : unmanaged, IVertex
    {
        ObjectDisposedException.ThrowIf(buffer.Handle == 0, buffer);
        if (!Driver.SupportsInstancing) throw new NotSupportedException("This graphics driver does not support instancing.");
        if (firstInstance < 0 || instanceCount < 0 || instanceCount > buffer.Capacity - firstInstance || indexCount < 0 || indexOffset < 0 || CurrentState.Mesh.Handle == 0 || CurrentState.Mesh == _mesh)
            throw new ArgumentOutOfRangeException(nameof(firstInstance));
        if (instanceCount == 0 || indexCount == 0) return true;
        if (instanceCount > RenderConfig.MaxPersistentInstancesPerFrame - _persistentInstancesThisFrame) return false;
        if (_commands.Length >= _maxDrawCommands) return false;
        var previous = CurrentState.InstanceStream;
        try
        {
            SetInstanceStream(buffer.Handle);
            if (_batchStateDirty) AddBatchState();
            _commands.Add(new DrawCommand
            {
                SortKey = MakeSortKey(order), PassOrder = _currentPass == RenderPass.RenderTexture ? _rtPassIndex : byte.MaxValue,
                IndexOffset = indexOffset, IndexCount = indexCount, BatchState = _currentBatchState,
                FirstInstance = firstInstance, InstanceCount = instanceCount,
            });
            buffer.LastDrawFrame = FrameRevision; _persistentInstancesThisFrame += instanceCount;
        }
        finally { SetInstanceStream(previous); }
        return true;
    }
}
