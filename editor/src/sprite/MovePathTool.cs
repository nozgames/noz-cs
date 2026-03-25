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
    private readonly HashSet<SpritePath> _movingPaths;
    private Vector2 _startWorld;
    private SnapType _snapType;
    private Vector2 _snapDocLocal;

    private AnchorMoveTool(SpriteDocument document,
        (SpritePath, SpritePathAnchor[], Matrix3x2, Vector2, Vector2)[] entries)
    {
        _document = document;
        _entries = entries;
        _movingPaths = new HashSet<SpritePath>(entries.Length);
        foreach (var (path, _, _, _, _) in entries)
            _movingPaths.Add(path);
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
        var mouseWorld = Workspace.MouseWorldPosition;
        _snapType = SnapType.None;

        // Snap when Ctrl is held: compute where the reference anchor would land,
        // snap it, then derive a world-space correction for all paths.
        if (Input.IsCtrlDown(Scope))
        {
            var (refPath, refSaved, refW2L, _, _) = _entries[0];
            var refIndex = FindFirstSelectedIndex(refPath);
            if (refIndex >= 0)
            {
                var startLocal = Vector2.Transform(_startWorld, refW2L);
                var mouseLocal = Vector2.Transform(mouseWorld, refW2L);
                var candidatePathLocal = refSaved[refIndex].Position + (mouseLocal - startLocal);

                // Path-local → doc-local
                var candidateDocLocal = refPath.HasTransform
                    ? Vector2.Transform(candidatePathLocal, refPath.PathTransform)
                    : candidatePathLocal;

                var snappedDocLocal = SnapHelper.Snap(
                    candidateDocLocal, _document.RootLayer, _movingPaths, out _snapType);

                if (_snapType != SnapType.None)
                {
                    _snapDocLocal = snappedDocLocal;
                    var docXform = _document.Transform;
                    var candidateWorld = Vector2.Transform(candidateDocLocal, docXform);
                    var snappedWorld = Vector2.Transform(snappedDocLocal, docXform);
                    mouseWorld += snappedWorld - candidateWorld;
                }
            }
        }

        foreach (var (path, saved, worldToLocal, savedCenter, savedTranslation) in _entries)
        {
            var startLocal = Vector2.Transform(_startWorld, worldToLocal);
            var mouseLocal = Vector2.Transform(mouseWorld, worldToLocal);
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

    private static int FindFirstSelectedIndex(SpritePath path)
    {
        for (var i = 0; i < path.Anchors.Count; i++)
            if (path.Anchors[i].IsSelected) return i;
        return -1;
    }

    public override void Draw()
    {
        if (_snapType == SnapType.None) return;
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_document.Transform);
            var size = Gizmos.GetVertexSize();
            Gizmos.SetColor(_snapType == SnapType.Anchor
                ? EditorStyle.Palette.Primary
                : EditorStyle.Workspace.SelectionColor);
            Gizmos.DrawRect(_snapDocLocal, _snapType == SnapType.Anchor ? size * 1.3f : size);
        }
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
    private readonly HashSet<SpritePath> _movingPaths;
    private SnapType _snapType;
    private Vector2 _snapDocLocal;

    private MovePathTransformTool(PathTransformToolState state)
    {
        _state = state;
        _movingPaths = new HashSet<SpritePath>(state.Snapshots.Length);
        foreach (var (path, _, _, _) in state.Snapshots)
            _movingPaths.Add(path);
    }

    public static MovePathTransformTool? Create(SpriteDocument document, List<SpritePath> selectedPaths)
    {
        var state = PathTransformToolState.Create(document, selectedPaths);
        if (state == null) return null;
        return new MovePathTransformTool(state.Value);
    }

    protected override void OnUpdate(Vector2 delta)
    {
        Matrix3x2.Invert(_state.Document.Transform, out var invDoc);
        var localDelta = Vector2.TransformNormal(delta, invDoc);
        _snapType = SnapType.None;

        if (Input.IsCtrlDown(Scope))
        {
            var candidateDocLocal = _state.Centroid + localDelta;
            var snappedDocLocal = SnapHelper.Snap(
                candidateDocLocal, _state.Document.RootLayer, _movingPaths, out _snapType);

            if (_snapType != SnapType.None)
            {
                _snapDocLocal = snappedDocLocal;
                localDelta = snappedDocLocal - _state.Centroid;
            }
        }

        foreach (var (path, savedTranslation, _, _) in _state.Snapshots)
            path.PathTranslation = savedTranslation + localDelta;
        _state.UpdatePaths();
    }

    protected override void OnCommit(Vector2 delta)
    {
        OnUpdate(delta);
        _state.Document.UpdateBounds();
    }

    public override void Draw()
    {
        if (_snapType == SnapType.None) return;
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_state.Document.Transform);
            var size = Gizmos.GetVertexSize();
            Gizmos.SetColor(_snapType == SnapType.Anchor
                ? EditorStyle.Palette.Primary
                : EditorStyle.Workspace.SelectionColor);
            Gizmos.DrawRect(_snapDocLocal, _snapType == SnapType.Anchor ? size * 1.3f : size);
        }
    }

    protected override void OnCancel() => Undo.Cancel();
}
