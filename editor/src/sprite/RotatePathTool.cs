//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class RotatePathTool : RotateTool
{
    private PathToolState _state;

    private RotatePathTool(PathToolState state, Vector2 worldPivot, Vector2 worldOrigin, Matrix3x2 invTransform)
        : base(worldPivot, state.Centroid, worldOrigin, Vector2.Zero, invTransform)
    {
        _state = state;
    }

    public static RotatePathTool? Create(SpriteDocument document)
    {
        var state = PathToolState.Create(document);
        if (state == null) return null;
        var s = state.Value;
        var worldPivot = Vector2.Transform(s.Centroid, document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, document.Transform);
        Matrix3x2.Invert(document.Transform, out var invTransform);
        return new RotatePathTool(s, worldPivot, worldOrigin, invTransform);
    }

    protected override void OnUpdate(float angle)
    {
        var pivot = Input.IsShiftDown() ? Vector2.Zero : _state.Centroid;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        foreach (var (path, saved) in _state.Snapshots)
        {
            for (var i = 0; i < path.Anchors.Count; i++)
            {
                if (!path.Anchors[i].IsSelected) continue;
                var off = saved[i].Position - pivot;
                var a = path.Anchors[i];
                a.Position = pivot + new Vector2(
                    off.X * cos - off.Y * sin,
                    off.X * sin + off.Y * cos);
                path.Anchors[i] = a;
            }
        }
        _state.UpdatePaths();
    }

    protected override void OnCommit(float angle)
    {
        OnUpdate(angle);
        _state.Document.UpdateBounds();
    }

    protected override void OnCancel() => Undo.Cancel();
}

public class RotatePathTransformTool : RotateTool
{
    private PathTransformToolState _state;

    private RotatePathTransformTool(
        PathTransformToolState state,
        Vector2 worldPivot, Vector2 localPivot,
        Matrix3x2 invTransform)
        : base(worldPivot, localPivot, worldPivot, localPivot, invTransform)
    {
        _state = state;
    }

    public static RotatePathTransformTool? Create(
        SpriteDocument document, List<SpritePath> selectedPaths)
    {
        var state = PathTransformToolState.Create(document, selectedPaths);
        if (state == null) return null;
        var s = state.Value;
        var worldPivot = Vector2.Transform(s.Centroid, document.Transform);
        Matrix3x2.Invert(document.Transform, out var invTransform);
        return new RotatePathTransformTool(s, worldPivot, s.Centroid, invTransform);
    }

    protected override void OnUpdate(float angle)
    {
        var pivot = _state.Centroid;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        foreach (var (path, savedTranslation, savedRotation, savedScale) in _state.Snapshots)
        {
            path.PathRotation = savedRotation + angle;

            // Adjust translation so rotation happens around the combined centroid
            var center = path.LocalBounds.Center;
            var savedWorldCenter = Vector2.Transform(center,
                Matrix3x2.CreateTranslation(-center)
                * Matrix3x2.CreateScale(savedScale)
                * Matrix3x2.CreateRotation(savedRotation)
                * Matrix3x2.CreateTranslation(center)
                * Matrix3x2.CreateTranslation(savedTranslation));
            var off = savedWorldCenter - pivot;
            var rotatedCenter = pivot + new Vector2(
                off.X * cos - off.Y * sin,
                off.X * sin + off.Y * cos);
            var newWorldCenter = Vector2.Transform(center,
                Matrix3x2.CreateTranslation(-center)
                * Matrix3x2.CreateScale(savedScale)
                * Matrix3x2.CreateRotation(savedRotation + angle)
                * Matrix3x2.CreateTranslation(center)
                * Matrix3x2.CreateTranslation(savedTranslation));
            path.PathTranslation = savedTranslation + (rotatedCenter - newWorldCenter);
        }
        _state.UpdatePaths();
    }

    protected override void OnCommit(float angle)
    {
        OnUpdate(angle);
        _state.Document.UpdateBounds();
    }

    protected override void OnCancel() => Undo.Cancel();
}
