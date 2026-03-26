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
    public struct ContourData
    {
        public Vector2[] Anchors;
        public float[] Curves;
        public bool Open;
    }

    public struct PathData
    {
        public Color32 FillColor;
        public Color32 StrokeColor;
        public byte StrokeWidth;
        public SpritePathOperation Operation;
        public ContourData[] Contours;

        // Backward compat helpers
        public Vector2[] Anchors => Contours[0].Anchors;
        public float[] Curves => Contours[0].Curves;
    }

    public PathData[] Paths { get; }

    public PathClipboardData(IReadOnlyList<SpritePath> paths)
    {
        Paths = new PathData[paths.Count];
        for (var p = 0; p < paths.Count; p++)
        {
            var path = paths[p];
            var contours = new ContourData[path.Contours.Count];

            for (var ci = 0; ci < path.Contours.Count; ci++)
            {
                var contour = path.Contours[ci];
                var anchors = new Vector2[contour.Anchors.Count];
                var curves = new float[contour.Anchors.Count];

                for (var i = 0; i < contour.Anchors.Count; i++)
                {
                    anchors[i] = contour.Anchors[i].Position;
                    curves[i] = contour.Anchors[i].Curve;
                }

                contours[ci] = new ContourData
                {
                    Anchors = anchors,
                    Curves = curves,
                    Open = contour.Open,
                };
            }

            Paths[p] = new PathData
            {
                FillColor = path.FillColor,
                StrokeColor = path.StrokeColor,
                StrokeWidth = path.StrokeWidth,
                Operation = path.Operation,
                Contours = contours,
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

            for (var ci = 0; ci < pathData.Contours.Length; ci++)
            {
                var contourData = pathData.Contours[ci];
                var contour = ci == 0 ? path.Contours[0] : new SpriteContour();
                contour.Open = contourData.Open;

                for (var a = 0; a < contourData.Anchors.Length; a++)
                    contour.Anchors.Add(new SpritePathAnchor { Position = contourData.Anchors[a], Curve = contourData.Curves[a] });

                if (ci > 0)
                    path.Contours.Add(contour);
            }

            path.SelectPath();
            path.UpdateSamples();
            path.UpdateBounds();
            results.Add(path);
        }

        return results;
    }
}
