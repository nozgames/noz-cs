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

    public PathClipboardData(IReadOnlyList<SpritePath> paths)
    {
        Paths = new PathData[paths.Count];
        for (var p = 0; p < paths.Count; p++)
        {
            var path = paths[p];
            var anchors = new Vector2[path.Anchors.Count];
            var curves = new float[path.Anchors.Count];

            for (var i = 0; i < path.Anchors.Count; i++)
            {
                anchors[i] = path.Anchors[i].Position;
                curves[i] = path.Anchors[i].Curve;
            }

            Paths[p] = new PathData
            {
                FillColor = path.FillColor,
                StrokeColor = path.StrokeColor,
                StrokeWidth = path.StrokeWidth,
                Operation = path.Operation,
                Anchors = anchors,
                Curves = curves,
            };
        }
    }

    public List<SpritePath> PasteAsPaths()
    {
        var results = new List<SpritePath>(Paths.Length);

        foreach (var pathData in Paths)
        {
            var path = new SpritePath
            {
                FillColor = pathData.FillColor,
                StrokeColor = pathData.StrokeColor,
                StrokeWidth = pathData.StrokeWidth,
                Operation = pathData.Operation,
            };

            for (var a = 0; a < pathData.Anchors.Length; a++)
                path.AddAnchor(pathData.Anchors[a], pathData.Curves[a]);

            path.SelectPath();
            path.UpdateSamples();
            path.UpdateBounds();
            results.Add(path);
        }

        return results;
    }
}
