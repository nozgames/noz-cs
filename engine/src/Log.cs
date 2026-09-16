//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static class Log
{
    private static readonly object _lock = new();
    private static bool _initialized;

    public static string? Path { get; set; }
    public static bool Muted { get; set; }

    // An optional mirror so a host application can SEE these lines while it runs.
    // A desktop build is typically OutputType=WinExe: it has no console,
    // Debug.WriteLine reaches only an attached debugger, and Path is often unset
    // - so by default every Log.* call is invisible at runtime, and a subsystem
    // that logs its whole story tells it to nobody. One callback lets a game
    // mirror the stream into an on-screen console without touching a call site.
    //
    // Called on WHATEVER THREAD logged, which is frequently not the main thread
    // (asset loads, downloads, socket receive), so an implementation must be
    // thread-safe and must not touch GPU resources directly. A sink that throws
    // is swallowed: logging must never be the thing that takes the caller down.
    public static Action<string>? Sink { get; set; }

    private static void EnsureInitialized()
    {
        if (_initialized || Path == null)
            return;

        lock (_lock)
        {
            if (_initialized || Path == null)
                return;

            File.WriteAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log started\n");
            _initialized = true;
        }
    }

    private static void WriteToFile(string message)
    {
        EnsureInitialized();
        lock (_lock)
        {
            File.AppendAllText(Path!, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
    }

    private static void Write(string message)
    {
        if (Muted) return;

        if (OperatingSystem.IsBrowser())
            Console.WriteLine(message);

        System.Diagnostics.Debug.WriteLine(message);

        if (Path != null)
            WriteToFile(message);

        // Read once into a local: an unsubscribe on another thread between the
        // null check and the call would otherwise be a NullReferenceException
        // inside the logger.
        var sink = Sink;
        if (sink == null) return;
        try { sink(message); } catch { }
    }

    public static void Info(string message) => Write($"[INFO] {message}");

    public static void Debug(string message) => Write($"[DEBUG] {message}");

    public static void Warning(string message) => Write($"[WARNING] {message}");

    public static void Error(string message) => Write($"[ERROR] {message}");

    public static string Params((string name, object? value, bool condition)[]? values)
    {
        if (values == null)
            return string.Empty;

        var stringBuilder = new System.Text.StringBuilder(1024);
        foreach (var (name, value, condition) in values)
            if (condition)
                stringBuilder.Append($"  {name}={value}");  

        return stringBuilder.ToString();
    }

    public static string Param(string name, object? value, bool condition=true) =>
        condition ? $"  {name}={value}" : "";
}
