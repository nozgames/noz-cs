//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;


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
        SpriteDocument document, SpriteEditor editor, List<SpritePath> selectedPaths)
    {
        var state = PathTransformToolState.Create(document, editor, selectedPaths);
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

            var center = path.LocalBounds.Center;
            var savedWorldCenter = PathTransformToolState.ComputeWorldCenter(
                center, savedTranslation, savedRotation, savedScale);
            var off = savedWorldCenter - pivot;
            var rotatedCenter = pivot + new Vector2(
                off.X * cos - off.Y * sin,
                off.X * sin + off.Y * cos);
            var newWorldCenter = PathTransformToolState.ComputeWorldCenter(
                center, savedTranslation, savedRotation + angle, savedScale);
            path.PathTranslation = savedTranslation + (rotatedCenter - newWorldCenter);
        }
        _state.UpdatePaths();
    }

    protected override void OnCommit(float angle)
    {
        OnUpdate(angle);
    }

    protected override void OnCancel() => Undo.Cancel();
}
