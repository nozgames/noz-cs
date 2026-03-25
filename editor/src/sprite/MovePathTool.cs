//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class MovePathTool : MoveTool
{
    private PathToolState _state;

    private MovePathTool(PathToolState state)
    {
        _state = state;
    }

    public static MovePathTool? Create(SpriteDocument document)
    {
        var state = PathToolState.Create(document);
        if (state == null) return null;
        return new MovePathTool(state.Value);
    }

    protected override void OnUpdate(Vector2 delta)
    {
        foreach (var (path, saved) in _state.Snapshots)
        {
            for (var i = 0; i < path.Anchors.Count; i++)
            {
                if (!path.Anchors[i].IsSelected) continue;
                var a = path.Anchors[i];
                a.Position = saved[i].Position + delta;
                path.Anchors[i] = a;
            }
        }
        _state.UpdatePaths();
    }

    protected override void OnCommit(Vector2 delta)
    {
        OnUpdate(delta);
        _state.Document.UpdateBounds();
    }

    protected override void OnCancel() => Undo.Cancel();
}

public class MovePathTransformTool : MoveTool
{
    private PathTransformToolState _state;

    private MovePathTransformTool(PathTransformToolState state)
    {
        _state = state;
    }

    public static MovePathTransformTool? Create(SpriteDocument document, List<SpritePath> selectedPaths)
    {
        var state = PathTransformToolState.Create(document, selectedPaths);
        if (state == null) return null;
        return new MovePathTransformTool(state.Value);
    }

    protected override void OnUpdate(Vector2 delta)
    {
        // Transform delta from world space to local space
        Matrix3x2.Invert(_state.Document.Transform, out var invDoc);
        var localDelta = Vector2.TransformNormal(delta, invDoc);

        foreach (var (path, savedTranslation, _, _) in _state.Snapshots)
            path.PathTranslation = savedTranslation + localDelta;
        _state.UpdatePaths();
    }

    protected override void OnCommit(Vector2 delta)
    {
        OnUpdate(delta);
        _state.Document.UpdateBounds();
    }

    protected override void OnCancel() => Undo.Cancel();
}
