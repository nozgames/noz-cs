//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

// A-mode tool: moves selected anchors in path-local space.
// Snapshots world-to-local transform at drag start — immune to bounds/pivot changes.
public class AnchorMoveTool : Tool
{
    private readonly SpriteDocument _document;
    private readonly (SpritePath Path, SpritePathAnchor[] Saved, Matrix3x2 WorldToLocal, Vector2 SavedBoundsCenter, Vector2 SavedTranslation)[] _entries;
    private Vector2 _startWorld;

    private AnchorMoveTool(SpriteDocument document,
        (SpritePath, SpritePathAnchor[], Matrix3x2, Vector2, Vector2)[] entries)
    {
        _document = document;
        _entries = entries;
    }

    public static AnchorMoveTool? Create(SpriteDocument document)
    {
        var paths = new List<SpritePath>();
        document.RootLayer.CollectPathsWithSelection(paths);
        if (paths.Count == 0) return null;

        var docXform = document.Transform;
        var entries = new (SpritePath, SpritePathAnchor[], Matrix3x2, Vector2, Vector2)[paths.Count];

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var pathToWorld = path.HasTransform ? path.PathTransform * docXform : docXform;
            Matrix3x2.Invert(pathToWorld, out var worldToLocal);
            entries[i] = (path, path.SnapshotAnchors(), worldToLocal, path.LocalBounds.Center, path.PathTranslation);
        }

        return new AnchorMoveTool(document, entries);
    }

    public override void Begin()
    {
        base.Begin();
        _startWorld = Workspace.MouseWorldPosition;
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) ||
            Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonReleased(InputCode.MouseLeft, Scope))
        {
            ApplyDelta();
            _document.UpdateBounds();
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        ApplyDelta();
    }

    private void ApplyDelta()
    {
        foreach (var (path, saved, worldToLocal, savedCenter, savedTranslation) in _entries)
        {
            var startLocal = Vector2.Transform(_startWorld, worldToLocal);
            var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, worldToLocal);
            var delta = mouseLocal - startLocal;

            for (var i = 0; i < path.Anchors.Count; i++)
            {
                if (!path.Anchors[i].IsSelected) continue;
                var a = path.Anchors[i];
                a.Position = saved[i].Position + delta;
                path.Anchors[i] = a;
            }

            path.MarkDirty();
            path.UpdateSamples();
            path.UpdateBounds();
            path.PathTranslation = savedTranslation;
            path.CompensateTranslation(savedCenter);
        }
        _document.IncrementVersion();
    }

    public override void Cancel()
    {
        Undo.Cancel();
        base.Cancel();
    }
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
