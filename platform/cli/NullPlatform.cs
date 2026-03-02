using System.Numerics;
using NoZ.Platform;

namespace NoZ;

public class NullPlatform : IPlatform
{
    private Vector2Int _windowSize;
    private bool _running = true;
    private string? _clipboard;

    public NullPlatform(int width = 1280, int height = 720)
    {
        _windowSize = new Vector2Int(width, height);
    }

    public Vector2Int WindowSize => _windowSize;
    public Vector2Int WindowPosition => Vector2Int.Zero;
    public float DisplayScale => 1.0f;
    public bool IsMouseInWindow => false;
    public bool IsMouseCaptured => false;
    public bool IsTextboxVisible => false;
    public bool IsFullscreen => false;
    public nint WindowHandle => 0;

    public event Action<PlatformEvent>? OnEvent;

    public void Init(PlatformConfig config) { }
    public void Shutdown() { }

    public bool PollEvents() => _running;
    public void RequestQuit() => _running = false;

    public void SwapBuffers() { }

    public void SetWindowSize(int width, int height) => _windowSize = new(width, height);
    public void SetWindowPosition(int x, int y) { }
    public void SetResizeCallback(Action? callback) { }

    public void ShowTextbox(Rect rect, string text, NativeTextboxStyle style) { }
    public void HideTextbox() { }
    public void UpdateTextboxRect(Rect rect, int fontSize) { }
    public bool UpdateTextboxText(ref string text) => false;

    public void SetClipboardText(string text) => _clipboard = text;
    public string? GetClipboardText() => _clipboard;

    public void SetMouseCapture(bool enabled) { }
    public void SetCursor(SystemCursor cursor) { }
    public void SetFullscreen(bool fullscreen) { }
    public void SetVSync(bool vsync) { }

    public nint GetGraphicsProcAddress(string name) => 0;

    public Stream? LoadPersistentData(string name, string? appName = null) => null;
    public void SavePersistentData(string name, Stream data, string? appName = null) { }

    public void Log(string message) => Console.Error.WriteLine(message);
    public void OpenURL(string url) { }
}
