//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using NoZ.Platform;

namespace NoZ;

// A thread that hands a frame's draws to the driver and presents it while the main thread
// goes on to the next frame (GraphicsConfig.RenderThread).
//
// The driver is not made for two threads, and does not have to be: one frame is on its way
// at a time, and whatever asks for the driver on another thread while it is (Graphics.Driver)
// waits until it is done. So the two overlap for as long as the next frame leaves the
// driver alone, which is its update, and the driver only ever has one caller.
//
// What the thread reads is what a frame's draws came to: the batches, their states and the
// passes. Those are put aside as the frame is handed over, and the main thread records the
// next frame into another set. Everything else a frame gives the driver (its vertices, its
// globals) is given on the main thread just before, when the thread is known to be idle.
public static unsafe partial class Graphics
{
    private static IGraphicsDriver _driver = null!;
    private static Thread? _renderThread;
    private static int _renderThreadId = -1;
    private static int _mainThreadId;
    private static volatile bool _renderBusy;
    private static volatile bool _renderStop;
    private static readonly ManualResetEventSlim _renderGo = new(false);
    private static readonly ManualResetEventSlim _renderIdle = new(true);
    private static Action? _afterRender;
    private static long _renderWaitTicks;

    // The driver's frame is begun when there is a frame to give it, not as the frame
    // starts: until then the one before may still be on its way to the display.
    private static bool _driverFrameBegun;
    private static bool _driverFrameFailed;

    // What a frame's draws came to, put aside for the thread.
    private static NativeArray<Batch> _renderBatches;
    private static NativeArray<BatchState> _renderBatchStates;
    private static (nuint Handle, Color ClearColor)[] _renderRtPasses = new (nuint, Color)[MaxRenderPasses];
    private static int _renderRtPassCount;
    private static Color _renderClearColor;

    /// <summary>How long the render thread took over the last frame it was given: handing
    /// its draws to the driver and presenting it.</summary>
    public static float RenderThreadMilliseconds { get; private set; }

    /// <summary>How long the main thread waited for the render thread in the last frame:
    /// at the frame's end, and wherever it wanted the driver before the thread was done.</summary>
    public static float RenderWaitMilliseconds { get; private set; }

    public static bool HasRenderThread => _renderThread != null;

    private static bool OnRenderThread => Environment.CurrentManagedThreadId == _renderThreadId;

    private static void StartRenderThread()
    {
        _renderBatches = new NativeArray<Batch>(_maxBatches);
        _renderBatchStates = new NativeArray<BatchState>(_maxBatches);
        _renderStop = false;
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "Render",
            Priority = ThreadPriority.AboveNormal
        };
        _renderThread.Start();
    }

    private static void StopRenderThread()
    {
        if (_renderThread == null) return;

        WaitForRender();
        _renderStop = true;
        _renderGo.Set();
        _renderThread.Join(1000);
        _renderThread = null;
        _renderThreadId = -1;
        _renderBatches.Dispose();
        _renderBatchStates.Dispose();
    }

    // Waits until no frame is on its way, and then, on the main thread, runs what was to
    // follow the frame that was (AfterEndFrame).
    private static void WaitForRender()
    {
        if (_renderBusy)
        {
            var started = Stopwatch.GetTimestamp();

            // It is usually microseconds from done, which is not worth a sleep; with the
            // display's refresh held it may be most of a frame, which is.
            for (var spins = 0; _renderBusy; spins++)
            {
                if (spins < 4000) Thread.SpinWait(8);
                else _renderIdle.Wait(1);
            }

            _renderWaitTicks += Stopwatch.GetTimestamp() - started;
        }

        if (_afterRender is { } after && Environment.CurrentManagedThreadId == _mainThreadId)
        {
            _afterRender = null;
            after();
        }
    }

    // Puts the frame's batches aside and wakes the thread. The main thread's own arrays
    // are the ones the thread had before, which it is done with.
    private static void HandOverFrame(Action? after)
    {
        (_batches, _renderBatches) = (_renderBatches, _batches);
        (_batchStates, _renderBatchStates) = (_renderBatchStates, _batchStates);
        (_rtPasses, _renderRtPasses) = (_renderRtPasses, _rtPasses);
        _renderRtPassCount = _rtPassCount;
        _renderClearColor = ClearColor;
        _afterRender = after;
        _rtPassCount = 0;
        _batches.Clear();
        _batchStates.Clear();

        _renderIdle.Reset();
        _renderBusy = true;
        _renderGo.Set();
    }

    private static void RenderLoop()
    {
        _renderThreadId = Environment.CurrentManagedThreadId;
        while (true)
        {
            _renderGo.Wait();
            _renderGo.Reset();
            if (_renderStop) return;

            var started = Stopwatch.GetTimestamp();
            try
            {
                Replay(_renderBatches.AsSpan(), _renderBatchStates.AsSpan(), _renderRtPasses, _renderRtPassCount, _renderClearColor, 0);
                _driver.EndFrame();
            }
            catch (Exception e)
            {
                Log.Error($"The render thread could not draw a frame: {e}");
            }

            RenderThreadMilliseconds = (float)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _renderBusy = false;
            _renderIdle.Set();
        }
    }
}
