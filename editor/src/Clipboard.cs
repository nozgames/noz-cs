//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

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

public sealed class PathClipboardData
{
    public struct PathData
    {
        public Color32 FillColor;
        public Color32 StrokeColor;
        public byte StrokeWidth;
        public SpritePathOperation Operation;
        public Vector2[] Anchors;
        public float[] Curves;
    }

    public PathData[] Paths { get; }
    public Vector2 Center { get; }

    public PathClipboardData(SpritePath path)
    {
        var anchors = new Vector2[path.Anchors.Count];
        var curves = new float[path.Anchors.Count];
        var sum = Vector2.Zero;

        for (var i = 0; i < path.Anchors.Count; i++)
        {
            anchors[i] = path.Anchors[i].Position;
            curves[i] = path.Anchors[i].Curve;
            sum += anchors[i];
        }

        Paths =
        [
            new PathData
            {
                FillColor = path.FillColor,
                StrokeColor = path.StrokeColor,
                StrokeWidth = path.StrokeWidth,
                Operation = path.Operation,
                Anchors = anchors,
                Curves = curves,
            }
        ];

        Center = path.Anchors.Count > 0 ? sum / path.Anchors.Count : Vector2.Zero;
    }

    public SpritePath PasteAsPath()
    {
        var result = new SpritePath();

        foreach (var pathData in Paths)
        {
            result.FillColor = pathData.FillColor;
            result.StrokeColor = pathData.StrokeColor;
            result.StrokeWidth = pathData.StrokeWidth;
            result.Operation = pathData.Operation;

            for (var a = 0; a < pathData.Anchors.Length; a++)
                result.AddAnchor(pathData.Anchors[a], pathData.Curves[a]);
        }

        result.SelectAll();
        result.UpdateSamples();
        result.UpdateBounds();
        return result;
    }
}
