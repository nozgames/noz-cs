//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class ScalePathTool : ScaleTool
{
    private PathToolState _state;

    private ScalePathTool(PathToolState state, Vector2 worldPivot, Vector2 worldOrigin)
        : base(worldPivot, worldOrigin)
    {
        _state = state;
    }

    public static ScalePathTool? Create(SpriteDocument document)
    {
        var state = PathToolState.Create(document);
        if (state == null) return null;
        var s = state.Value;
        var worldPivot = Vector2.Transform(s.Centroid, document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, document.Transform);
        return new ScalePathTool(s, worldPivot, worldOrigin);
    }

    protected override void OnUpdate(Vector2 scale)
    {
        var pivot = Input.IsShiftDown(InputScope.All) ? Vector2.Zero : _state.Centroid;
        var avgScale = (MathF.Abs(scale.X) + MathF.Abs(scale.Y)) * 0.5f;

        foreach (var (path, saved) in _state.Snapshots)
        {
            for (var i = 0; i < path.Anchors.Count; i++)
            {
                if (!path.Anchors[i].IsSelected) continue;
                var off = saved[i].Position - pivot;
                var a = path.Anchors[i];
                a.Position = pivot + new Vector2(off.X * scale.X, off.Y * scale.Y);
                a.Curve = SpritePath.ClampCurve(saved[i].Curve * avgScale);
                path.Anchors[i] = a;
            }
        }
        _state.UpdatePaths();
    }

    protected override void OnCommit(Vector2 scale)
    {
        OnUpdate(scale);
        _state.Document.UpdateBounds();
    }

    protected override void OnCancel() => Undo.Cancel();
}
