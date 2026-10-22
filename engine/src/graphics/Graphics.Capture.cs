//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public sealed class CapturedFrame(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    // Tightly packed RGBA8, top row first.
    public byte[] Pixels { get; } = pixels;
}

public static partial class Graphics
{
    private static readonly List<TaskCompletionSource<CapturedFrame>> _captureRequests = [];
    private static readonly List<TaskCompletionSource<CapturedFrame>> _captureActive = [];
    private static RenderTexture? _captureRT;

    // Captures the next fully drawn frame, UI included. The frame is drawn into an
    // offscreen target which is read back once the frame has been submitted.
    public static Task<CapturedFrame> CaptureFrameAsync()
    {
        var tcs = new TaskCompletionSource<CapturedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_captureRequests)
            _captureRequests.Add(tcs);
        return tcs.Task;
    }

    private static RenderTexture? BeginCapture(int width, int height)
    {
        lock (_captureRequests)
        {
            if (_captureRequests.Count == 0 || _captureActive.Count > 0)
                return null;
            _captureActive.AddRange(_captureRequests);
            _captureRequests.Clear();
        }

        _captureRT = RenderTexturePool.Acquire(width, height, 1, TextureFormat.BGRA8);
        return _captureRT;
    }

    // A frame that was never drawn hands its requests to the next one.
    private static void CancelCapture()
    {
        if (_captureRT == null) return;
        _captureRT = null;
        lock (_captureRequests)
        {
            _captureRequests.InsertRange(0, _captureActive);
            _captureActive.Clear();
        }
    }

    private static void EndCapture()
    {
        var rt = _captureRT;
        if (rt == null) return;
        _captureRT = null;

        TaskCompletionSource<CapturedFrame>[] requests;
        lock (_captureRequests)
        {
            requests = [.. _captureActive];
            _captureActive.Clear();
        }

        var width = rt.Width;
        var height = rt.Height;
        Task<byte[]> readback;
        try
        {
            readback = _driver.ReadRenderTexturePixelsAsync(rt.Handle);
        }
        catch (Exception e)
        {
            foreach (var request in requests)
                request.TrySetException(e);
            return;
        }

        readback.ContinueWith(task =>
        {
            foreach (var request in requests)
            {
                if (task.IsFaulted)
                    request.TrySetException(task.Exception!.InnerExceptions);
                else if (task.Result.Length != width * height * 4)
                    request.TrySetException(new InvalidOperationException($"Frame capture returned {task.Result.Length} bytes, expected {width * height * 4}"));
                else
                    request.TrySetResult(new CapturedFrame(width, height, task.Result));
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }
}
