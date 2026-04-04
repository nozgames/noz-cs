//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace NoZ;

public static class Profiler
{
    private const int MaxMarkers = 256;
    private const int MaxCounters = 64;
    private const int MaxDepthStack = 32;

    public struct MarkerState
    {
        public string Name;
        public long ElapsedTicks;
        public long StartTicks;
        public long AllocBytes;
        public long AllocStart;
        public int RefCount;
        public int CallCount;
        public byte Depth;
        public bool Used;
    }

    public struct CounterState
    {
        public string Name;
        public float Value;
    }

    private static readonly object _lock = new();
    private static readonly MarkerState[] _markers = new MarkerState[MaxMarkers];
    private static readonly CounterState[] _counters = new CounterState[MaxCounters];
    private static ushort _markerCount;
    private static ushort _counterCount;

    // Frame order tracking
    private static readonly ushort[] _markerOrder = new ushort[MaxMarkers];
    private static int _markerOrderCount;

    // Depth tracking stack
    private static readonly ushort[] _depthStack = new ushort[MaxDepthStack];
    private static int _depthStackSize;

    // Frame tracking
    private static int _frameNumber;
    private static long _frameAllocStart;
    private static readonly ProfilerCounter s_counterAllocations = new("GC.Allocations");

    public static bool Enabled { get; set; }
    public static int FrameNumber => _frameNumber;
    public static int MarkerCount => _markerCount;
    public static int CounterCount => _counterCount;
    public static int MarkerOrderCount => _markerOrderCount;

    public static ushort GetMarkerOrderId(int index) => _markerOrder[index];
    public static ref readonly MarkerState GetMarker(ushort id) => ref _markers[id];
    public static ref readonly CounterState GetCounter(ushort id) => ref _counters[id];

    public static void Init()
    {
        _frameNumber = 0;
        _depthStackSize = 0;
        Enabled = true;
        Log.Info("Profiler initialized");
    }

    public static void Shutdown()
    {
        ProfilerClient.Shutdown();
        Log.Info("Profiler shutdown");
    }

    public static void BeginFrame()
    {
        if (!Enabled) return;

        _depthStackSize = 0;
        _markerOrderCount = 0;

        for (var i = 0; i < _markerCount; i++)
        {
            ref var m = ref _markers[i];
            m.ElapsedTicks = 0;
            m.RefCount = 0;
            m.CallCount = 0;
            m.AllocBytes = 0;
            m.Used = false;
        }

        for (var i = 0; i < _counterCount; i++)
            _counters[i].Value = 0f;

        _frameAllocStart = GC.GetAllocatedBytesForCurrentThread();
    }

    public static void EndFrame()
    {
        if (!Enabled || Application.IsResizing) return;

        s_counterAllocations.Value = GC.GetAllocatedBytesForCurrentThread() - _frameAllocStart;

        _frameNumber++;
        ProfilerClient.SendFrame();
    }

    internal static ushort RegisterMarker(string name)
    {
        lock (_lock)
        {
            var id = _markerCount++;
            _markers[id].Name = name;
            return id;
        }
    }

    internal static ushort RegisterCounter(string name)
    {
        lock (_lock)
        {
            var id = _counterCount++;
            _counters[id].Name = name;
            return id;
        }
    }

    internal static void BeginMarker(ushort id)
    {
        if (!Enabled || Application.IsResizing) return;
        if (_depthStackSize >= MaxDepthStack) return;

        ref var m = ref _markers[id];

        if (!m.Used)
        {
            m.Depth = (byte)_depthStackSize;
            m.Used = true;
            _markerOrder[_markerOrderCount++] = id;
        }
        m.CallCount++;

        _depthStack[_depthStackSize++] = id;

        if (m.RefCount++ == 0)
        {
            m.StartTicks = Stopwatch.GetTimestamp();
            m.AllocStart = GC.GetAllocatedBytesForCurrentThread();
        }
    }

    internal static void EndMarker(ushort id)
    {
        if (!Enabled || Application.IsResizing) return;
        if (_depthStackSize <= 0) return;

        _depthStackSize--;

        ref var m = ref _markers[id];
        if (--m.RefCount == 0)
        {
            m.ElapsedTicks += Stopwatch.GetTimestamp() - m.StartTicks;
            m.AllocBytes += GC.GetAllocatedBytesForCurrentThread() - m.AllocStart;
        }
    }

    internal static float GetCounterValue(ushort id) => _counters[id].Value;

    internal static void SetCounterValue(ushort id, float value)
    {
        if (!Enabled) return;
        _counters[id].Value = value;
    }

    internal static void IncrementCounter(ushort id, float amount)
    {
        if (!Enabled) return;
        _counters[id].Value += amount;
    }
}
