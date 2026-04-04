//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class Clipboard
{
    private static object? _content;
    private static Type? _contentType;

    public static bool HasContent => _content != null;
    public static Type? ContentType => _contentType;

    public static void Copy<T>(T content) where T : class
    {
        if (_content is IDisposable disposable)
            disposable.Dispose();

        _content = content;
        _contentType = typeof(T);
    }

    public static T? Get<T>() where T : class
    {
        if (_content is T typed)
            return typed;
        return null;
    }

    public static bool Is<T>() where T : class => _content is T;

    public static void Clear()
    {
        if (_content is IDisposable disposable)
            disposable.Dispose();

        _content = null;
        _contentType = null;
    }
}
