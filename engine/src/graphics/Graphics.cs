//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

public enum RenderPass : byte
{
    RenderTexture = 0,
    Scene = 1,
}

public static unsafe partial class Graphics
{
    private static readonly ProfilerMarker s_markerEndFrame = new("Graphics.EndFrame");
    private static readonly ProfilerMarker s_markerExecuteCommands = new("Graphics.ExecuteCommands");
    private static readonly ProfilerMarker s_markerFlush = new("Graphics.Flush");
    private static readonly ProfilerMarker s_markerUploadBones  = new("Graphics.UploadBones");
    private static readonly ProfilerMarker s_markerCreateBatches = new("Graphics.CreateBatches");
    private static readonly ProfilerMarker s_markerUploadGlobals = new("Graphics.UploadGlobals");
    private static readonly ProfilerMarker s_markerDrawElements = new("Graphics.DrawElements");
    private static readonly ProfilerMarker s_markerEndPass = new("Graphics.EndPass");
    private static readonly ProfilerMarker s_markerEndRenderTexturePass = new("Graphics.EndRenderTexturePass");
    private static readonly ProfilerCounter s_counterDrawCalls = new("Graphics.DrawCalls");
    private static readonly ProfilerCounter s_counterVertices = new("Graphics.Vertices");
    private static readonly ProfilerCounter s_counterIndices = new("Graphics.Indices");
    private static readonly ProfilerCounter s_counterCommands = new("Graphics.Commands");
    private static readonly ProfilerCounter s_counterTargetBatches = new("Graphics.Batches.RenderTexture");
    private static readonly ProfilerCounter s_counterScreenBatches = new("Graphics.Batches.Screen");
    private static readonly ProfilerCounter[] s_counterScreenBreaks = BatchBreakCounters("Graphics.Batches.Screen.");
    private static readonly ProfilerCounter[] s_counterTargetBreaks = BatchBreakCounters("Graphics.Batches.RenderTexture.");

    private static ProfilerCounter[] BatchBreakCounters(string prefix) =>
    [
        new(prefix + "Shader"), new(prefix + "Texture"), new(prefix + "Scissor"), new(prefix + "Globals"),
        new(prefix + "Blend"), new(prefix + "Mesh"), new(prefix + "Other")
    ];

    private const int MaxRenderPasses = 64;
    private const int MaxSortGroups = 1526;
    private const int MaxStateStack = 16;
    private const int MaxVertices = 65536;
    private const int MaxIndices = 196608;
    private const int MaxTextures = 8;
    private const int IndexShift = 0;   // bits 0-15 (16 bits)
    private const int OrderShift = 16;  // bits 16-31 (16 bits)
    private const int GroupShift = 32;  // bits 32-47 (16 bits)
    private const int LayerShift = 48;  // bits 48-59 (12 bits, mask to 0xFFF)
    private const long SortKeyMergeMask = 0x7FFFFFFFFFFF0000;

    private struct BatchState()
    {
        public nuint Shader;
        public fixed ulong Textures[MaxTextures];
        public fixed byte TextureFilters[MaxTextures];
        public BlendMode BlendMode;
        public RectInt Viewport;
        public RectInt Scissor;
        public nuint Mesh;
        public nuint InstanceStream;
        public bool ScissorEnabled;
        public byte Pass;
        public ushort GlobalsIndex;
        public nuint RenderTextureHandle;  // 0 = scene pass, otherwise RT handle
        public Color ClearColor;
    }

    private struct Batch
    {
        public int InstanceCount;
        public int FirstInstance;
        public int IndexOffset;
        public int IndexCount;
        public ushort State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlobalsSnapshot
    {
        public Matrix4x4 Projection;
        public float Time;
        public int DrawParameterIndex;
    }

    private const int MaxBoneRows = 1024;
    private const int BoneTextureWidth = 128;
    private static int _boneRow;
    private static float _time;
    public static event Action? AfterEndFrame;
    private static State[] _stateStack = null!;
    private static int _stateStackDepth = 0;
    private static bool _batchStateDirty = true;
    private static RenderTexture? _internalRT;
    private static readonly Camera _blitCamera = new();
    private static Shader? _blitShader;
    
    public static ApplicationConfig Config { get; private set; } = null!;
    public static GraphicsConfig RenderConfig => Config.Graphics!;
    // While the render thread has a frame on its way, the driver is its alone: whatever
    // asks for it on another thread waits until the frame is handed over.
    public static IGraphicsDriver Driver
    {
        get
        {
            if (_renderBusy && !OnRenderThread) WaitForRender();
            return _driver;
        }
        private set => _driver = value;
    }
    public static Camera? Camera { get; private set; }
    public static Texture WhiteTexture { get; private set; } = null!;
    public static ref readonly Matrix3x2 Transform => ref CurrentState.Transform;
    public static Color Color => CurrentState.Color.WithAlpha(CurrentState.Color.A * CurrentState.Opacity);
    public static float PixelsPerUnit { get; private set; }
    public static float PixelsPerUnitInv {  get; private set; }
    public static bool IsScissor => CurrentState.ScissorEnabled;


    public static float RenderScale { get; set; } = 1.0f;
    public static Vector2Int RenderSize { get; private set; }

    private static ref State CurrentState => ref _stateStack[_stateStackDepth];
    
    private static RenderMesh _mesh;
    private static nuint _boneTexture;
    private const int BoneTextureSlot = 1;
    private static RenderPass _currentPass;
    private static byte _rtPassIndex;
    private static Matrix4x4[] _passProjections = new Matrix4x4[MaxRenderPasses];
    private static RenderTexture? _activeRenderTexture;
    public static bool IsRenderTexturePassActive => _activeRenderTexture != null;
    private static int _rtPassCount;
    private static (nuint Handle, Color ClearColor)[] _rtPasses = new (nuint, Color)[MaxRenderPasses];
    private static NativeArray<float> _boneData;
    private static int _maxDrawCommands;
    private static int _maxBatches;
    private static int _maxGlobalSnapshots;
    private static NativeArray<MeshVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static NativeArray<ushort> _sortedIndices;
    private static NativeArray<DrawCommand> _commands;
    private static NativeArray<Batch> _batches;
    private static NativeArray<BatchState> _batchStates;
    private static NativeArray<GlobalsSnapshot> _globalsSnapshots;
    private static GraphicsSnapshotIndex _globalsIndex;
    private static GraphicsSnapshotIndex _batchIndex;
    private static int _globalsBaseIndex; // Base offset for globals buffers to prevent RTT overwriting main frame
    private static ushort _currentBatchState;
    
    public static Color ClearColor { get; set; } = Color.Black;  
    
    public static void Init(ApplicationConfig config)
    {
        Config = config;

        var graphicsConfig = config.Graphics ?? throw new ArgumentNullException(
            nameof(config.Graphics),
            "Render config must be provided.");

        Driver = graphicsConfig.Driver ?? throw new ArgumentNullException(
            nameof(graphicsConfig.Driver),
            "Driver must be provided");

        _maxDrawCommands = RenderConfig.MaxDrawCommands;
        _maxBatches = RenderConfig.MaxBatches;
        _maxGlobalSnapshots = RenderConfig.MaxGlobalSnapshots;
        if (graphicsConfig.MaxInstancesPerFrame < 1)
            throw new ArgumentOutOfRangeException(nameof(graphicsConfig.MaxInstancesPerFrame));
        if (graphicsConfig.MaxPersistentInstancesPerFrame < 1)
            throw new ArgumentOutOfRangeException(nameof(graphicsConfig.MaxPersistentInstancesPerFrame));
        if (_maxBatches is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(graphicsConfig.MaxBatches));
        if (_maxDrawCommands is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(graphicsConfig.MaxDrawCommands));
        if (_maxGlobalSnapshots is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(graphicsConfig.MaxGlobalSnapshots),
                $"MaxGlobalSnapshots must be between 1 and {ushort.MaxValue}.");
        if (graphicsConfig.MaxMeshes < 1)
            throw new ArgumentOutOfRangeException(
                nameof(graphicsConfig.MaxMeshes),
                "MaxMeshes must be at least 1.");
        _stateStack = new State[MaxStateStack];
        _stateStackDepth = 0;

        _globalsBaseIndex = 0;
        _drawParameterCount = 0;

        PixelsPerUnit = graphicsConfig.PixelsPerUnit;
        PixelsPerUnitInv = 1.0f / PixelsPerUnit;

        Driver.Init(new GraphicsDriverConfig
        {
            Platform = config.Platform!,
            VSync = graphicsConfig.Vsync,
            MaxGlobalSnapshots = graphicsConfig.MaxGlobalSnapshots,
            MaxMeshes = graphicsConfig.MaxMeshes,
        });

        _vertices = new NativeArray<MeshVertex>(MaxVertices);
        _indices = new NativeArray<ushort>(MaxIndices);
        _sortedIndices = new NativeArray<ushort>(MaxIndices);
        _commands = new NativeArray<DrawCommand>(_maxDrawCommands);
        _batches = new NativeArray<Batch>(_maxBatches);
        _batchStates = new NativeArray<BatchState>(_maxBatches);
        _globalsSnapshots = new NativeArray<GlobalsSnapshot>(_maxGlobalSnapshots);
        _globalsIndex = new GraphicsSnapshotIndex(_maxGlobalSnapshots);
        _batchIndex = new GraphicsSnapshotIndex(_maxBatches);
        _drawParameterIndex = new GraphicsSnapshotIndex(_maxGlobalSnapshots);
        _drawParameterData = new NativeArray<byte>(_maxGlobalSnapshots * MaxDrawParameterBytes);
        _drawParameterLengths = new NativeArray<int>(_maxGlobalSnapshots, _maxGlobalSnapshots);

        _mesh = CreateMesh<MeshVertex>(
            MaxVertices,
            MaxIndices,
            BufferUsage.Dynamic,
            "Graphics.Main"
        );

        var boneDataLength = BoneTextureWidth * MaxBoneRows * 4;
        _boneData = new NativeArray<float>(boneDataLength, boneDataLength);
        _boneData[0] = 1; _boneData[1] = 0; _boneData[2] = 0; _boneData[3] = 0;
        _boneData[4] = 0; _boneData[5] = 1; _boneData[6] = 0; _boneData[7] = 0;
        _boneTexture = Driver.CreateTexture(
            BoneTextureWidth, MaxBoneRows,
            [],
            TextureFormat.RGBA32F,
            TextureFilter.Point,
            name: "Bones");

        _mainThreadId = Environment.CurrentManagedThreadId;
        _driverFrameBegun = false;
        _driverFrameFailed = false;
        if (graphicsConfig.RenderThread) StartRenderThread();

        ResetState();
    }

    public static void Shutdown()
    {
        StopRenderThread();
        foreach (var stream in _instanceStreams.Values) stream.Dispose();
        _instanceStreams.Clear();
        _drawParameterData.Dispose();
        _drawParameterLengths.Dispose();
        _drawParameterIndex.Dispose();
        _globalsIndex.Dispose();
        _batchIndex.Dispose();
        _drawParameterCount = 0;
        _batches.Dispose();
        _vertices.Dispose();
        _commands.Dispose();
        _indices.Dispose();
        _globalsSnapshots.Dispose();
        _batchStates.Dispose();
        _sortedIndices.Dispose();
        _boneData.Dispose();

        Driver.DestroyMesh(_mesh.Handle);
        Driver.DestroyTexture(_boneTexture);
        WhiteTexture?.Dispose();
        WhiteTexture = null!;

        Driver.Shutdown();

        _mesh = default;
        _boneTexture = 0;
    }

    internal static bool BeginFrame()
    {
        FrameRevision++;
        FrameOpen = true;
        _persistentInstancesThisFrame = 0;
        // Only recycle snapshots at a frame boundary, never during an internal
        // blit/flush: earlier draws may still be waiting for GPU submission.
        _globalsBaseIndex = 0;
        _drawParameterCount = 0;
        _drawParameterIndex.Clear();
        foreach (var stream in _instanceStreams.Values) stream.BeginFrame();
        // Also discard a partially recorded frame after an exception. No old
        // command may reference parameter slots that are about to be reused.
        _commands.Clear();
        _vertices.Clear();
        _indices.Clear();
        _sortedIndices.Clear();
        _batches.Clear();
        _batchStates.Clear();
        _batchIndex.Clear();
        _globalsSnapshots.Clear();
        _globalsIndex.Clear();
        ResetState();

        if (WhiteTexture == null)
            WhiteTexture = Texture.Create(1, 1, [255, 255, 255, 255], name: "White");

        // With a render thread the driver's frame begins when this one is handed over
        // (EndFrame), so that the frame before can be on its way meanwhile. After a frame
        // the driver would not begin (no surface: the window is minimized) it is asked
        // here, as without the thread, and frames are passed over until it will.
        if (!HasRenderThread || _driverFrameFailed)
        {
            WaitForRender();
            if (!BeginDriverFrame())
            {
                FrameOpen = false;
                return false;
            }
        }

        RenderTexturePool.FlushPendingReleases();

        // Compute render size for this frame
        if (RenderScale < 1.0f)
        {
            var win = Application.WindowSize;
            RenderSize = new Vector2Int(
                Math.Max(1, (int)MathF.Round(win.X * RenderScale)),
                Math.Max(1, (int)MathF.Round(win.Y * RenderScale)));
            _internalRT = RenderTexturePool.Acquire(RenderSize.X, RenderSize.Y);
        }
        else
        {
            RenderSize = Application.WindowSize;
        }

        _time += Time.DeltaTime;

        return true;
    }

    public static void BeginPass(RenderTexture rt) => BeginPass(rt, Color.Transparent);

    public static void BeginPass(RenderTexture rt, Color clearColor)
    {
        if (rt == null)
            throw new InvalidOperationException("Cannot begin pass with invalid render texture");

        if (_activeRenderTexture != null)
            throw new InvalidOperationException("Cannot nest render texture passes - call EndPass first");

        if (_rtPassIndex >= MaxRenderPasses)
            throw new InvalidOperationException($"Render texture pass budget exhausted ({MaxRenderPasses}).");

        PushState();

        CurrentState.ClearColor = clearColor;
        _currentPass = RenderPass.RenderTexture;
        _rtPassIndex++;
        _activeRenderTexture = rt;
        _rtPasses[_rtPassCount++] = (rt.Handle, CurrentState.ClearColor);
        SetViewport(0, 0, rt.Width, rt.Height);
        ClearScissor();
        _batchStateDirty = true;
    }

    public static void EndPass()
    {
        if (_activeRenderTexture == null)
            throw new InvalidOperationException("No render texture pass is active - call BeginPass first");

        _currentPass = RenderPass.Scene;
        _activeRenderTexture = null;

        PopState();
        _batchStateDirty = true;
    }

    private static bool BeginDriverFrame()
    {
        if (_driverFrameBegun) return true;

        _driverFrameBegun = _driver.BeginFrame();
        _driverFrameFailed = !_driverFrameBegun;
        return _driverFrameBegun;
    }

    internal static void EndFrame()
    {
        // One frame is on its way at a time: the one before is waited for, and then the
        // driver is this thread's until this frame is handed over.
        WaitForRender();
        RenderWaitMilliseconds = (float)Stopwatch.GetElapsedTime(0, _renderWaitTicks).TotalMilliseconds;
        _renderWaitTicks = 0;

        if (!BeginDriverFrame())
        {
            DiscardFrame();
            AfterEndFrame?.Invoke();
            AfterEndFrame = null;
            FrameOpen = false;
            return;
        }

        // A frame drawn at another scale is drawn twice over, and instance streams are
        // filled as a frame is recorded: those frames are drawn here, as without the thread.
        var handOver = HasRenderThread && _internalRT == null && _instanceStreams.Count == 0;

        using (s_markerExecuteCommands.Begin())
            ExecuteCommands(handOver);

        if (_internalRT != null)
            BlitInternalRT();

        var after = AfterEndFrame;
        AfterEndFrame = null;
        _driverFrameBegun = false;

        if (handOver)
        {
            HandOverFrame(after);
        }
        else
        {
            after?.Invoke();
            using (s_markerEndFrame.Begin())
                _driver.EndFrame();
        }

        FrameOpen = false;
    }

    // A frame that cannot be drawn leaves nothing behind for the next.
    private static void DiscardFrame()
    {
        _rtPassCount = 0;
        _commands.Clear();
        _vertices.Clear();
        _indices.Clear();
        _batches.Clear();
        _batchStates.Clear();
        _batchIndex.Clear();
        _globalsSnapshots.Clear();
        _globalsIndex.Clear();
        _batchStateDirty = true;
        _currentBatchState = 0;
    }

    private static void BlitInternalRT()
    {
        var savedRT = _internalRT;
        _internalRT = null;
        if (savedRT == null) return;

        var savedScale = RenderScale;
        RenderScale = 1.0f;
        RenderSize = Application.WindowSize;

        var winSize = Application.WindowSize;
        _blitCamera.SetExtents(new Rect(0, 0, winSize.X, winSize.Y));
        _blitCamera.Update(winSize);

        ResetState();
        SetCamera(_blitCamera);
        _blitShader ??= Asset.Get<Shader>(AssetType.Shader, "texture")!;
        SetShader(_blitShader);
        SetTextureFilter(TextureFilter.Point);
        SetTexture(savedRT.Handle);
        SetBlendMode(BlendMode.None);
        Draw(0, 0, winSize.X, winSize.Y);

        using (s_markerExecuteCommands.Begin())
            ExecuteCommands();

        _internalRT = savedRT;
        RenderScale = savedScale;
    }

    internal static void ResolveAssets()
    {
    }

    private static void UploadBones()
    {
        Driver.UpdateTextureRegion(
            _boneTexture,
            new RectInt(0,0,BoneTextureWidth,_boneRow),
            _boneData.AsByteSpan(),
            BoneTextureWidth);
    }

    private static void UploadGlobals()
    {
        var count = _globalsSnapshots.Length;
        if (count == 0)
            return;

        // Ensure driver has enough buffers for base + count
        Driver.SetGlobalsCount(_globalsBaseIndex + count);

        Span<byte> data = stackalloc byte[GlobalsPrefixBytes + MaxDrawParameterBytes];
        for (int i = 0; i < count; i++)
        {
            ref var snapshot = ref _globalsSnapshots[i];
            var transposed = Matrix4x4.Transpose(snapshot.Projection);
            data.Clear();
            MemoryMarshal.Write(data, in transposed);
            MemoryMarshal.Write(data[64..], in snapshot.Time);
            var size = GlobalsPrefixBytes;
            if (snapshot.DrawParameterIndex != 0)
            {
                var parameters = GetDrawParameters(snapshot.DrawParameterIndex - 1);
                parameters.CopyTo(data[GlobalsPrefixBytes..]);
                size += parameters.Length;
            }
            Driver.SetGlobals(_globalsBaseIndex + i, data[..size]);
        }
    }

    private static long MakeSortKey(ushort order)
    {
        return
            (((long)(CurrentState.SortLayer & 0xFFF)) << LayerShift) |
            (((long)CurrentState.SortGroup) << GroupShift) |
            (((long)order) << OrderShift) |
            (((long)(_commands.Length & 0xFFFF)) << IndexShift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetOrAddGlobals(in Matrix4x4 projection)
    {
        // Time is the same for all batches; caller-defined parameters are not.
        // Matrix equality treats positive and negative zero as equal. Float hashes
        // preserve that contract; raw matrix bytes would miss those matches.
        Span<int> key = stackalloc int[17];
        var components = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(in projection, 1));
        for (var i = 0; i < 16; i++) key[i] = components[i].GetHashCode();
        key[16] = CurrentState.DrawParameterIndex;
        var hash = GraphicsSnapshotIndex.Hash(MemoryMarshal.AsBytes(key));
        for (var i = _globalsIndex.First(hash); i >= 0; i = _globalsIndex.Next(i))
            if (_globalsSnapshots[i].Projection == projection &&
                _globalsSnapshots[i].DrawParameterIndex == CurrentState.DrawParameterIndex)
                return (ushort)(_globalsBaseIndex + i);

        var nextIndex = _globalsBaseIndex + _globalsSnapshots.Length;
        if (nextIndex >= _maxGlobalSnapshots)
            throw new InvalidOperationException(
                $"Graphics global snapshot budget exhausted ({_maxGlobalSnapshots}). " +
                $"Increase {nameof(GraphicsConfig)}.{nameof(GraphicsConfig.MaxGlobalSnapshots)} " +
                "or reduce the number of unique projections/parameters submitted in one frame.");

        var index = (ushort)nextIndex;
        _globalsIndex.Add(hash, _globalsSnapshots.Length);
        _globalsSnapshots.Add() = new GlobalsSnapshot
        {
            Projection = projection,
            Time = _time,
            DrawParameterIndex = CurrentState.DrawParameterIndex
        };
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddBatchState()
    {
        ValidateBatchState();

        var currentProjection = _passProjections[(int)_currentPass];

        BatchState candidate;
        Unsafe.InitBlockUnaligned(&candidate, 0, (uint)sizeof(BatchState));
        candidate.Pass = (byte)_currentPass;
        candidate.GlobalsIndex = GetOrAddGlobals(currentProjection);
        candidate.Shader = CurrentState.Shader?.Native ?? nuint.Zero;
        candidate.BlendMode = CurrentState.BlendMode;
        candidate.Viewport = CurrentState.Viewport;
        candidate.ScissorEnabled = CurrentState.ScissorEnabled;
        candidate.Scissor = CurrentState.Scissor;
        candidate.Mesh = CurrentState.Mesh.Handle;
        candidate.InstanceStream = CurrentState.InstanceStream;
        candidate.RenderTextureHandle = _activeRenderTexture?.Handle ?? 0;
        candidate.ClearColor = CurrentState.ClearColor;

        for (int t = 0; t < MaxTextures; t++)
        {
            candidate.Textures[t] = CurrentState.Textures[t];
            candidate.TextureFilters[t] = CurrentState.TextureFilters[t];
        }

        var candidateSpan = new ReadOnlySpan<byte>(&candidate, sizeof(BatchState));
        var hash = GraphicsSnapshotIndex.Hash(candidateSpan);
        for (var i = _batchIndex.First(hash); i >= 0; i = _batchIndex.Next(i))
        {
            var existingSpan = new ReadOnlySpan<byte>(Unsafe.AsPointer(ref _batchStates[i]), sizeof(BatchState));
            if (candidateSpan.SequenceEqual(existingSpan))
            {
                _currentBatchState = (ushort)i;
                _batchStateDirty = false;
                return;
            }
        }

        if (!_batchStates.CheckCapacity(1))
            throw new InvalidOperationException($"Graphics batch state budget exhausted ({_maxBatches}).");
        _currentBatchState = (ushort)_batchStates.Length;
        _batchIndex.Add(hash, _batchStates.Length);
        ref var added = ref _batchStates.Add();
        candidateSpan.CopyTo(new Span<byte>(Unsafe.AsPointer(ref added), sizeof(BatchState)));
        _batchStateDirty = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddQuad(
        in Vector2 p0,
        in Vector2 p1,
        in Vector2 p2,
        in Vector2 p3,
        in Vector2 uv0,
        in Vector2 uv1,
        in Vector2 uv2,
        in Vector2 uv3,
        ushort order,
        int atlasIndex = 0,
        int bone = -1)
    {

        s_markerTemp.Begin();
        Span<MeshVertex> verts =
        [
            new MeshVertex { Position = p0, UV = uv0, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p1, UV = uv1, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p2, UV = uv2, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p3, UV = uv3, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
        ];
        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        s_markerTemp.End();
        AddTriangles(verts, indices, order: order, bone: bone);
    }

private static readonly ProfilerMarker s_markerTemp = new("temp");

    private static void AddTriangles(
        ReadOnlySpan<MeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        ushort order,
        int bone)
    {
        if (CurrentState.Shader == null)
            return;            

        SetMesh(_mesh);
        SetInstanceStream(0);

        if (_batchStateDirty)
            AddBatchState();

        if (_commands.Length >= _maxDrawCommands)
            return;

        if (_vertices.Length + vertices.Length > MaxVertices ||
            _indices.Length + indices.Length > MaxIndices)
            return;

        var sortKey = MakeSortKey(order);
        ref var cmd = ref _commands.Add();
        cmd.SortKey = sortKey;
        cmd.PassOrder = _currentPass == RenderPass.RenderTexture ? _rtPassIndex : byte.MaxValue;
        cmd.IndexOffset = _indices.Length;
        cmd.IndexCount = indices.Length;
        cmd.BatchState = _currentBatchState;
        cmd.InstanceCount = 1;
        cmd.FirstInstance = 0;

        var baseVertex = _vertices.Length;
        var color = Color;
        var overlayColor = CurrentState.OverlayColor;

        if (bone == -1)
        {
            for (var i = 0; i < vertices.Length; i++)
            {
                ref readonly var v = ref vertices[i];
                _vertices.Add(v with
                {
                    Position = Vector2.Transform(v.Position, CurrentState.Transform),
                    Color = v.Color * color,
                    OverlayColor = overlayColor,
                    Bone = 0
                });
            }
        }
        else
        {
            bone += CurrentState.BoneIndex;
            foreach (var v in vertices)
            {
                _vertices.Add(v with
                {
                    Color = v.Color * color,
                    OverlayColor = overlayColor,
                    Bone = bone
                });
            }
        }

        for (var i = 0; i < indices.Length; i++)
            _indices.Add((ushort)(baseVertex + indices[i]));
    }

    private static void AddBatch(ushort batchState, int indexOffset, int indexCount)
    {
        if (indexCount == 0)
            return;
        
        ref var batch = ref _batches.Add();
        batch.IndexOffset = indexOffset;
        batch.IndexCount = indexCount;
        batch.State = batchState;
    }

    public static void DrawElements(int indexCount, int indexOffset = 0, ushort order=0)
    {
        SetInstanceStream(0);
        if (_batchStateDirty)
            AddBatchState();

        var sortKey = MakeSortKey(order);
        
        if (_commands.Length > 0)
        {
            ref var lastCommand = ref _commands[^1];

            if (lastCommand.BatchState == _currentBatchState &&
                lastCommand.PassOrder == (_currentPass == RenderPass.RenderTexture ? _rtPassIndex : byte.MaxValue) &&
                (lastCommand.SortKey & SortKeyMergeMask) == (sortKey & SortKeyMergeMask) &&
                lastCommand.IndexOffset + lastCommand.IndexCount == indexOffset)
            {
                lastCommand.IndexCount += indexCount;
                return;
            }
        }

        if (_commands.Length >= _maxDrawCommands)
            return;

        ref var cmd = ref _commands.Add();
        cmd.SortKey = sortKey;
        cmd.PassOrder = _currentPass == RenderPass.RenderTexture ? _rtPassIndex : byte.MaxValue;
        cmd.IndexOffset = indexOffset;
        cmd.IndexCount = indexCount;
        cmd.BatchState = _currentBatchState;
        cmd.InstanceCount = 1;
        cmd.FirstInstance = 0;
    }

    private static void CreateBatches()
    {
        _batches.Clear();

        if (_commands.Length == 0)
            return;

        _batches.Add();
        _sortedIndices.Clear();

        ref var firstBatch = ref _batches[0];
        ref var firstState = ref _batchStates[_commands[0].BatchState];
        firstBatch.IndexOffset = firstState.Mesh != _mesh.Handle ? _commands[0].IndexOffset : 0;
        firstBatch.IndexCount = _commands[0].IndexCount;
        firstBatch.State = _commands[0].BatchState;
        firstBatch.InstanceCount = _commands[0].InstanceCount;
        firstBatch.FirstInstance = _commands[0].FirstInstance;

        if (firstState.Mesh == _mesh.Handle)
            _sortedIndices.AddRange(
                _indices.AsReadonlySpan(_commands[0].IndexOffset, _commands[0].IndexCount)
            );

        for (int commandIndex = 1, commandCount = _commands.Length; commandIndex < commandCount; commandIndex++)
        {
            ref var cmd = ref _commands[commandIndex];
            ref var cmdState = ref _batchStates[cmd.BatchState];

            // External mesh: merge if same state and contiguous indices, otherwise new batch.
            if (cmdState.Mesh != _mesh.Handle)
            {
                ref var prevBatch = ref _batches[^1];
                if (cmdState.InstanceStream == 0 && cmd.BatchState == prevBatch.State &&
                    cmd.IndexOffset == prevBatch.IndexOffset + prevBatch.IndexCount)
                {
                    prevBatch.IndexCount += cmd.IndexCount;
                }
                else if (cmdState.InstanceStream != 0 && cmd.BatchState == prevBatch.State &&
                    cmd.IndexOffset == prevBatch.IndexOffset && cmd.IndexCount == prevBatch.IndexCount &&
                    cmd.FirstInstance == prevBatch.FirstInstance + prevBatch.InstanceCount)
                {
                    // Adjacent ranges preserve submission order and can share a draw.
                    prevBatch.InstanceCount += cmd.InstanceCount;
                }
                else
                {
                    ref var newBatch = ref AddBatch();
                    newBatch.IndexOffset = cmd.IndexOffset;
                    newBatch.IndexCount = cmd.IndexCount;
                    newBatch.State = cmd.BatchState;
                    newBatch.InstanceCount = cmd.InstanceCount;
                    newBatch.FirstInstance = cmd.FirstInstance;
                }
                continue;
            }

            ref var currentBatch = ref _batches[^1];
            if (cmd.BatchState != currentBatch.State)
            {
                ref var newBatch = ref AddBatch();
                newBatch.IndexOffset = _sortedIndices.Length;
                newBatch.IndexCount = cmd.IndexCount;
                newBatch.State = cmd.BatchState;
                newBatch.InstanceCount = 1;
                newBatch.FirstInstance = 0;
            }
            else
            {
                currentBatch.IndexCount += cmd.IndexCount;
            }

            _sortedIndices.AddRange(
                _indices.AsReadonlySpan(cmd.IndexOffset, cmd.IndexCount)
            );
        }
    }
    
    // For a profiler that is listening: how many batches went to render textures and how
    // many to the screen, and for each batch that follows another to the same target, the
    // first thing that kept it out of that batch. A different shader usually brings a
    // different texture with it, so the shader is looked at first.
    private static void CountBatchBreaks()
    {
        for (int i = 0, count = _batches.Length; i < count; i++)
        {
            ref var state = ref _batchStates[_batches[i].State];
            if (state.RenderTextureHandle != 0) s_counterTargetBatches.Increment();
            else s_counterScreenBatches.Increment();

            if (i == 0)
                continue;

            ref var before = ref _batchStates[_batches[i - 1].State];
            if (before.RenderTextureHandle != state.RenderTextureHandle || before.Pass != state.Pass)
                continue;

            var textures = false;
            for (var t = 0; t < MaxTextures; t++)
                textures |= before.Textures[t] != state.Textures[t] || before.TextureFilters[t] != state.TextureFilters[t];

            var reason =
                before.Shader != state.Shader ? 0 :
                textures ? 1 :
                before.ScissorEnabled != state.ScissorEnabled || before.Scissor != state.Scissor ? 2 :
                before.GlobalsIndex != state.GlobalsIndex ? 3 :
                before.BlendMode != state.BlendMode ? 4 :
                before.Mesh != state.Mesh || before.InstanceStream != state.InstanceStream ? 5 :
                6;

            (state.RenderTextureHandle != 0 ? s_counterTargetBreaks : s_counterScreenBreaks)[reason].Increment();
        }
    }

    private static ref Batch AddBatch()
    {
        if (!_batches.CheckCapacity(1))
            throw new InvalidOperationException($"Graphics batch budget exhausted ({_maxBatches}).");
        return ref _batches.Add();
    }

    private static void EndRenderPass(IGraphicsDriver driver, nuint currentRT, nuint internalRT)
    {
        if (currentRT == 0)
        {
            if (internalRT != 0)
            {
                using (s_markerEndRenderTexturePass.Begin())
                    driver.EndRenderTexturePass();
            }
            else
            {
                using (s_markerEndPass.Begin())
                    driver.EndScenePass();
            }
        }
        else if (currentRT != nuint.MaxValue)
        {
            using (s_markerEndRenderTexturePass.Begin())
                driver.EndRenderTexturePass();
        }
    }

    // Turns what was recorded into batches and gives the driver the frame's vertices and
    // globals. The batches are then handed to the driver here, or, when the frame is to be
    // handed over to the render thread, left as they are for it (EndFrame).
    private static void ExecuteCommands(bool handOver = false)
    {
        var internalRT = _internalRT != null ? _internalRT.Handle : 0;

        // If no commands, just clear the target and return early
        if (_commands.Length == 0)
        {
            _batches.Clear();
            _batchStates.Clear();
            if (!handOver)
            {
                Replay(_batches.AsSpan(), _batchStates.AsSpan(), _rtPasses, _rtPassCount, ClearColor, internalRT);
                _rtPassCount = 0;
            }

            return;
        }

        using (s_markerFlush.Begin())
        {
            TextRender.Flush();
            UI.Flush();
            ElementTree.Flush();            
        }

        _commands.AsSpan().Sort();

        using (s_markerCreateBatches.Begin())
            CreateBatches();

        if (Profiler.Enabled)
            CountBatchBreaks();

        if (_vertices.Length > 0 || _indices.Length > 0)
        {
            // Pad indices to 4-byte alignment for WebGPU
            if ((_sortedIndices.Length & 1) != 0)
                _sortedIndices.Add(0);

            Driver.BindMesh(_mesh.Handle);
            Driver.UpdateMesh(_mesh.Handle, _vertices.AsByteSpan(), _sortedIndices.AsSpan());
        }

        using (s_markerUploadBones.Begin())
            UploadBones();

        // Upload all globals snapshots to driver
        foreach (var stream in _instanceStreams.Values) stream.Upload();
        using (s_markerUploadGlobals.Begin())
            UploadGlobals();

        s_counterDrawCalls.Increment(_batches.Length);
        s_counterCommands.Increment(_commands.Length);
        s_counterVertices.Increment(_vertices.Length);
        s_counterIndices.Increment(_indices.Length);

        // The render thread takes the batches, their states and the passes as they are
        // (HandOverFrame); otherwise they go to the driver here.
        if (!handOver)
        {
            Replay(_batches.AsSpan(), _batchStates.AsSpan(), _rtPasses, _rtPassCount, ClearColor, internalRT);

            // These targets have been rendered/cleared. A later flush in the same
            // frame must not clear them again merely because it has no draws there.
            _rtPassCount = 0;
            _batches.Clear();
            _batchStates.Clear();
        }

        _commands.Clear();
        _vertices.Clear();
        _indices.Clear();
        _batchIndex.Clear();

        // Advance base index so subsequent ExecuteCommands calls (like RTT) use different buffer slots
        // This prevents RTT from overwriting globals that main frame draw commands still reference
        _globalsBaseIndex += _globalsSnapshots.Length;
        _globalsSnapshots.Clear();
        _globalsIndex.Clear();

        _batchStateDirty = true;
        _currentBatchState = 0;
    }

    // Hands batches to the driver: each render texture's pass, then the scene's. It reads
    // nothing of the frame being recorded, so the render thread can run it on what was put
    // aside for it while the next frame is recorded.
    private static void Replay(
        ReadOnlySpan<Batch> batches,
        ReadOnlySpan<BatchState> batchStates,
        (nuint Handle, Color ClearColor)[] rtPasses,
        int rtPassCount,
        Color clearColor,
        nuint internalRT)
    {
        var driver = _driver;

        // With nothing to draw the targets are still cleared, as they were promised.
        if (batches.Length == 0)
        {
            if (internalRT != 0)
            {
                driver.BeginRenderTexturePass(internalRT, clearColor);
                driver.EndRenderTexturePass();
            }
            else
            {
                driver.BeginScenePass(clearColor);
                driver.EndScenePass();
            }

            for (var r = 0; r < rtPassCount; r++)
            {
                driver.BeginRenderTexturePass(rtPasses[r].Handle, rtPasses[r].ClearColor);
                driver.EndRenderTexturePass();
            }

            return;
        }

        driver.BindTexture(_boneTexture, BoneTextureSlot);

        // Track current render target for pass switching (0 = scene pass, non-zero = RT pass)
        nuint currentRT = nuint.MaxValue;  // Invalid value to force first pass begin
        bool scenePassStarted = false;
        Span<bool> rtVisited = stackalloc bool[rtPassCount];
        rtVisited.Clear();

        for (int batchIndex = 0, batchCount = batches.Length; batchIndex < batchCount; batchIndex++)
        {
            ref readonly var batch = ref batches[batchIndex];
            ref readonly var batchState = ref batchStates[batch.State];

            // Handle pass switching based on render target
            if (currentRT != batchState.RenderTextureHandle)
            {
                EndRenderPass(driver, currentRT, internalRT);
                currentRT = batchState.RenderTextureHandle;

                // Begin new pass
                if (currentRT == 0)
                {
                    if (!scenePassStarted)
                    {
                        if (internalRT != 0)
                            driver.BeginRenderTexturePass(internalRT, clearColor);
                        else
                            driver.BeginScenePass(clearColor);
                        scenePassStarted = true;
                    }
                    else
                    {
                        if (internalRT != 0)
                            driver.ResumeRenderTexturePass(internalRT);
                        else
                            driver.ResumeScenePass();
                    }
                }
                else
                {
                    // RT pass - get clear color from batch state
                    driver.BeginRenderTexturePass(currentRT, batchState.ClearColor);

                    // Mark this RT as visited
                    for (int r = 0; r < rtPassCount; r++)
                    {
                        if (rtPasses[r].Handle == currentRT)
                        {
                            rtVisited[r] = true;
                            break;
                        }
                    }
                }

                // Re-bind bone texture — driver state was reset by BeginPass
                driver.BindTexture(_boneTexture, BoneTextureSlot);
            }

            // Apply all state unconditionally — driver early-exits handle optimization
            driver.SetViewport(batchState.Viewport);
            if (batchState.ScissorEnabled)
                driver.SetScissor(batchState.Scissor);
            else
                driver.ClearScissor();
            driver.BindShader(batchState.Shader);
            driver.BindGlobals(batchState.GlobalsIndex);
            for (int t = 0; t < MaxTextures; t++)
            {
                if (batchState.Textures[t] != 0)
                    driver.BindTexture((nuint)batchState.Textures[t], t, (TextureFilter)batchState.TextureFilters[t]);
            }
            driver.SetBlendMode(batchState.BlendMode);
            driver.BindMesh(batchState.Mesh);
            driver.BindInstanceStream(batchState.InstanceStream);

            using (s_markerDrawElements.Begin())
            {
                if (batchState.InstanceStream != 0)
                    driver.DrawElementsInstanced(batch.IndexOffset, batch.IndexCount, batch.InstanceCount, batch.FirstInstance);
                else
                    driver.DrawElements(batch.IndexOffset, batch.IndexCount, 0);
            }
        }

        // Clear scissor before ending the final pass
        driver.ClearScissor();

        // End the final pass
        EndRenderPass(driver, currentRT, internalRT);

        // Clear any RT passes that had no draw commands (e.g. empty workspace with grid hidden)
        for (int r = 0; r < rtPassCount; r++)
        {
            if (!rtVisited[r])
            {
                driver.BeginRenderTexturePass(rtPasses[r].Handle, rtPasses[r].ClearColor);
                driver.EndRenderTexturePass();
            }
        }
    }

    [Conditional("DEBUG")]
    private static void ValidateBatchState()
    {
        var shader = CurrentState.Shader;
        if (shader == null) return;

        foreach (var binding in shader.Bindings)
        {
            switch (binding.Type)
            {
                case ShaderBindingType.Texture2D:
                case ShaderBindingType.Texture2DArray:
                {
                    int slot = FindTextureSlotForBinding(binding, shader);

                    // Bone texture slot is bound globally in ExecuteCommands, not per draw call
                    if (slot == BoneTextureSlot)
                        break;

                    var textureHandle = slot >= 0 ? (nuint)CurrentState.Textures[slot] : 0;
                    Debug.Assert(textureHandle != 0,
                        $"Shader '{shader.Name}' binding {binding.Binding} ('{binding.Name}') expects a texture but none is bound");

                    if (textureHandle != 0)
                    {
                        var texture = Asset.Get<Texture>(AssetType.Texture, textureHandle);
                        if (texture != null)
                        {
                            if (binding.Type == ShaderBindingType.Texture2DArray)
                                Debug.Assert(texture.IsArray,
                                    $"Shader '{shader.Name}' binding {binding.Binding} ('{binding.Name}') expects texture_2d_array but got texture_2d");
                            else
                                Debug.Assert(!texture.IsArray,
                                    $"Shader '{shader.Name}' binding {binding.Binding} ('{binding.Name}') expects texture_2d but got texture_2d_array");
                        }
                    }
                    break;
                }
            }
        }

        if (CurrentState.Mesh.Handle != 0 && shader.VertexFormatHash != 0)
        {
            var meshHash = CurrentState.Mesh.VertexHash;
            Debug.Assert(meshHash == 0 || meshHash == shader.VertexFormatHash,
                $"Vertex format mismatch: shader '{shader.Name}' (hash 0x{shader.VertexFormatHash:X8}) " +
                $"is incompatible with bound mesh (hash 0x{meshHash:X8})");
        }
    }

    private static int FindTextureSlotForBinding(ShaderBinding target, Shader shader)
    {
        int slotIndex = 0;
        foreach (var binding in shader.Bindings)
        {
            if (binding.Type is ShaderBindingType.Texture2D
                or ShaderBindingType.Texture2DArray
                or ShaderBindingType.Texture2DUnfilterable)
            {
                if (binding.Binding == target.Binding)
                    return slotIndex;
                slotIndex++;
            }
        }
        return -1;
    }
}
