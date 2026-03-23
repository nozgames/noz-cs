//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteEditor
{
    private const int MaxSavedAnchors = 1024;
    private readonly Vector2[] _savedPositions = new Vector2[MaxSavedAnchors];
    private readonly float[] _savedCurves = new float[MaxSavedAnchors];

    private struct SavedPathState
    {
        public SpritePath Path;
        public int Offset;
    }

    private readonly List<SpritePath> _pathsBuffer = new();
    private readonly List<SavedPathState> _savedPaths = new();

    public bool HasPathSelection { get; private set; }

    private bool SnapToPixelGrid => Input.IsCtrlDown(InputScope.All);

    private SpriteLayer ActiveLayer => Document.ActiveLayer ?? Document.RootLayer;

    private SpritePath? GetPathWithSelection()
    {
        return Document.RootLayer.GetPathWithSelection();
    }

    #region Commands

    private Command[] GetShapeCommands()
    {
        return
        [
            new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete },
            new Command { Name = "Move", Handler = BeginMoveTool, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove },
            new Command { Name = "Rotate", Handler = BeginRotateTool, Key = InputCode.KeyR },
            new Command { Name = "Scale", Handler = OnScale, Key = InputCode.KeyS },
            new Command { Name = "Curve", Handler = BeginCurveTool, Key = InputCode.KeyC },
            new Command { Name = "Select All", Handler = SelectAll, Key = InputCode.KeyA },
            new Command { Name = "Insert Anchor", Handler = InsertAnchorAtHover, Key = InputCode.KeyV },
            new Command { Name = "Pen Tool", Handler = BeginPenTool, Key = InputCode.KeyP },
            new Command { Name = "Knife Tool", Handler = BeginKnifeTool, Key = InputCode.KeyK },
            new Command { Name = "Rectangle Tool", Handler = BeginRectangleTool, Key = InputCode.KeyR, Ctrl = true },
            new Command { Name = "Circle Tool", Handler = BeginCircleTool, Key = InputCode.KeyO, Ctrl = true },
            new Command { Name = "Duplicate", Handler = DuplicateSelected, Key = InputCode.KeyD, Ctrl = true },
            new Command { Name = "Copy", Handler = CopySelected, Key = InputCode.KeyC, Ctrl = true },
            new Command { Name = "Paste", Handler = PasteSelected, Key = InputCode.KeyV, Ctrl = true },
        ];
    }

    #endregion

    #region Input

    private void HandleDeleteKey()
    {
        if (Input.WasButtonPressed(InputCode.KeyDelete))
            DeleteSelected();
    }

    private void HandleDragStart()
    {
        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelectAnchors));
    }

    #endregion

    #region Selection

    private void SelectAll()
    {
        Document.RootLayer.ForEachEditablePath(p => p.SelectAll());
        UpdateSelection();
    }

    private void ClearSelection()
    {
        ClearAllSelections();
        HasPathSelection = false;
    }

    private void UpdateSelection()
    {
        HasPathSelection = GetPathWithSelection() != null;
        OnSelectionChanged(HasPathSelection);
    }

    private void CommitBoxSelectAnchors(Rect bounds)
    {
        var shift = Input.IsShiftDown(InputScope.All);

        if (!shift)
            ClearAllSelections();

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);

        Document.RootLayer.SelectAnchorsInRect(localRect);
        UpdateSelection();
    }

    private void OnSelectionChanged(bool hasSelection)
    {
        var path = GetPathWithSelection();
        if (path != null)
        {
            Document.CurrentFillColor = path.FillColor;
            Document.CurrentStrokeColor = path.StrokeColor;
            Document.CurrentStrokeWidth = (byte)int.Max(1, (int)path.StrokeWidth);
            Document.CurrentOperation = path.Operation;
        }
    }

    private void ClearAllSelections()
    {
        Document.RootLayer.ClearAllSelections();
    }

    private void InvalidateMesh() => _meshVersion = -1;

    private bool CollectAndSavePathStates(bool saveCurves)
    {
        _pathsBuffer.Clear();
        Document.RootLayer.CollectPathsWithSelection(_pathsBuffer);
        if (_pathsBuffer.Count == 0) return false;

        _savedPaths.Clear();
        var offset = 0;
        foreach (var path in _pathsBuffer)
        {
            path.SavePositions(_savedPositions, offset);
            if (saveCurves) path.SaveCurves(_savedCurves, offset);
            _savedPaths.Add(new SavedPathState { Path = path, Offset = offset });
            offset += path.Anchors.Count;
        }
        return true;
    }

    private Vector2? GetMultiPathCentroid()
    {
        var sum = Vector2.Zero;
        var count = 0;
        foreach (var sp in _savedPaths)
        {
            for (var i = 0; i < sp.Path.Anchors.Count; i++)
            {
                if (!sp.Path.Anchors[i].IsSelected) continue;
                sum += sp.Path.Anchors[i].Position;
                count++;
            }
        }
        return count > 0 ? sum / count : null;
    }

    private void UpdateAllSavedPaths()
    {
        foreach (var sp in _savedPaths)
        {
            sp.Path.UpdateSamples();
            sp.Path.UpdateBounds();
        }
        InvalidateMesh();
    }

    #endregion

    #region Manipulation

    private void DeleteSelected()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        Undo.Record(Document);
        path.DeleteSelectedAnchors();

        // Remove path if too few anchors remain
        if (path.Anchors.Count < 3)
        {
            var parent = Document.RootLayer.FindParent(path);
            parent?.Children.Remove(path);
        }
        else
        {
            path.UpdateSamples();
            path.UpdateBounds();
        }

        Document.IncrementVersion();
        Document.UpdateBounds();
        HasPathSelection = false;
    }

    private void DuplicateSelected()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        var parent = Document.RootLayer.FindParent(path);
        if (parent == null) return;

        Undo.Record(Document);

        var clone = path.ClonePath();
        clone.ClearSelection();
        clone.SelectAll();
        path.ClearSelection();

        parent.Children.Add(clone);

        clone.UpdateSamples();
        clone.UpdateBounds();
        Document.IncrementVersion();
        Document.UpdateBounds();

        BeginMoveTool();
    }

    private void CopySelected()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        var data = new PathClipboardData(path);
        if (data.Paths.Length > 0)
            Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null) return;

        Undo.Record(Document);
        ClearAllSelections();

        var newPath = clipboardData.PasteAsPath();
        ActiveLayer.Children.Add(newPath);

        Document.IncrementVersion();
        Document.UpdateBounds();
        UpdateSelection();
    }

    #endregion

    #region Tools

    public void BeginPenTool()
    {
        Workspace.BeginTool(new PenTool(Document, Document.RootLayer, ActiveLayer,
            Document.CurrentFillColor, Document.CurrentOperation));
    }

    public void BeginKnifeTool()
    {
        Workspace.BeginTool(new KnifeTool(Document, Document.RootLayer));
    }

    public void BeginRectangleTool()
    {
        Workspace.BeginTool(new ShapeTool(Document, ActiveLayer,
            Document.CurrentFillColor, ShapeType.Rectangle, Document.CurrentOperation));
    }

    public void BeginCircleTool()
    {
        Workspace.BeginTool(new ShapeTool(Document, ActiveLayer,
            Document.CurrentFillColor, ShapeType.Circle, Document.CurrentOperation));
    }

    private void BeginMoveTool()
    {
        if (!CollectAndSavePathStates(saveCurves: false)) return;

        Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: delta =>
            {
                var snap = Input.IsCtrlDown(InputScope.All);
                foreach (var sp in _savedPaths)
                    sp.Path.TranslateSelected(delta, _savedPositions, snap, sp.Offset);
                UpdateAllSavedPaths();
            },
            commit: _ =>
            {
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                foreach (var sp in _savedPaths)
                    sp.Path.RestorePositions(_savedPositions, sp.Offset);
                UpdateAllSavedPaths();
                Undo.Cancel();
            }
        ));
    }

    private void BeginRotateTool()
    {
        if (!CollectAndSavePathStates(saveCurves: false)) return;

        var localPivot = GetMultiPathCentroid();
        if (!localPivot.HasValue) return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);
        Matrix3x2.Invert(Document.Transform, out var invTransform);

        Undo.Record(Document);

        Workspace.BeginTool(new RotateTool(
            worldPivot, localPivot.Value, worldOrigin, Vector2.Zero, invTransform,
            update: angle =>
            {
                var pivot = Input.IsShiftDown() ? Vector2.Zero : localPivot.Value;
                foreach (var sp in _savedPaths)
                {
                    sp.Path.RotateSelected(pivot, angle, _savedPositions, sp.Offset);
                    if (SnapToPixelGrid) sp.Path.SnapSelectedToPixelGrid();
                }
                UpdateAllSavedPaths();
            },
            commit: _ =>
            {
                foreach (var sp in _savedPaths)
                {
                    if (SnapToPixelGrid) sp.Path.SnapSelectedToPixelGrid();
                }
                UpdateAllSavedPaths();
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                foreach (var sp in _savedPaths)
                    sp.Path.RestorePositions(_savedPositions, sp.Offset);
                UpdateAllSavedPaths();
                Undo.Cancel();
            }
        ));
    }

    private void OnScale()
    {
        if (!CollectAndSavePathStates(saveCurves: true)) return;

        var localPivot = GetMultiPathCentroid();
        if (!localPivot.HasValue) return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);

        Undo.Record(Document);

        Workspace.BeginTool(new ScaleTool(
            worldPivot, worldOrigin,
            update: scale =>
            {
                var pivot = Input.IsShiftDown(InputScope.All) ? Vector2.Zero : localPivot.Value;
                foreach (var sp in _savedPaths)
                {
                    sp.Path.ScaleSelected(pivot, scale, _savedPositions, _savedCurves, sp.Offset);
                    if (SnapToPixelGrid) sp.Path.SnapSelectedToPixelGrid();
                }
                UpdateAllSavedPaths();
                Document.IncrementVersion();
            },
            commit: _ =>
            {
                foreach (var sp in _savedPaths)
                {
                    if (SnapToPixelGrid) sp.Path.SnapSelectedToPixelGrid();
                }
                UpdateAllSavedPaths();
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                foreach (var sp in _savedPaths)
                {
                    sp.Path.RestorePositions(_savedPositions, sp.Offset);
                    sp.Path.RestoreCurves(_savedCurves, sp.Offset);
                }
                UpdateAllSavedPaths();
                Undo.Cancel();
            }
        ));
    }

    private void BeginCurveTool()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        var hasSelectedSegment = false;
        for (var i = 0; i < path.Anchors.Count; i++)
        {
            if (path.IsSegmentSelected(i))
            {
                hasSelectedSegment = true;
                break;
            }
        }

        if (!hasSelectedSegment) return;

        path.SaveCurves(_savedCurves);
        Undo.Record(Document);

        Workspace.BeginTool(new CurveTool(
            path, Document.Transform, _savedCurves,
            update: () => Document.IncrementVersion(),
            commit: () =>
            {
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () => Undo.Cancel()
        ));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        var hit = Document.RootLayer.HitTest(mouseLocal);
        if (!hit.HasValue || hit.Value.Hit.SegmentIndex < 0) return;

        Undo.Record(Document);

        var path = hit.Value.Path;
        path.ClearSelection();
        path.SplitSegmentAtPoint(hit.Value.Hit.SegmentIndex, hit.Value.Hit.SegmentPosition);

        var newIdx = hit.Value.Hit.SegmentIndex + 1;
        if (newIdx < path.Anchors.Count)
            path.SetAnchorSelected(newIdx, true);

        path.UpdateSamples();
        path.UpdateBounds();
        Document.UpdateBounds();

        path.SavePositions(_savedPositions);
        BeginMoveTool();
    }

    #endregion

    #region Drawing (SpritePath)

    private static void DrawPathSegments(SpritePath path, Matrix3x2 transform)
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(transform);
            var segmentCount = path.Open ? path.Anchors.Count - 1 : path.Anchors.Count;

            Gizmos.SetColor(EditorStyle.Palette.PathSegment);
            for (var i = 0; i < segmentCount; i++)
            {
                if (!path.IsSegmentSelected(i))
                    DrawPathSegment(path, i, EditorStyle.Shape.SegmentLineWidth, 1);
            }

            Gizmos.SetColor(EditorStyle.Palette.Primary);
            for (var i = 0; i < segmentCount; i++)
            {
                if (path.IsSegmentSelected(i))
                    DrawPathSegment(path, i, EditorStyle.Shape.SegmentLineWidth, 2);
            }
        }
    }

    private static void DrawPathSegment(SpritePath path, int segmentIndex, float width, ushort order = 0)
    {
        var samples = path.GetSegmentSamples(segmentIndex);
        var prev = path.Anchors[segmentIndex].Position;
        foreach (var sample in samples)
        {
            Gizmos.DrawLine(prev, sample, width, order: order);
            prev = sample;
        }
        var nextIdx = (segmentIndex + 1) % path.Anchors.Count;
        Gizmos.DrawLine(prev, path.Anchors[nextIdx].Position, width, order: order);
    }

    private static void DrawPathAnchors(SpritePath path, Matrix3x2 transform, bool selectedOnly = false)
    {
        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);
        Graphics.SetTransform(transform);

        if (!selectedOnly)
        {
            for (var i = 0; i < path.Anchors.Count; i++)
            {
                if (path.Anchors[i].IsSelected) continue;
                Gizmos.SetColor(EditorStyle.Palette.PathAnchor);
                Gizmos.DrawRect(path.Anchors[i].Position, EditorStyle.Shape.AnchorSize, order: 4);
            }
        }

        for (var i = 0; i < path.Anchors.Count; i++)
        {
            if (!path.Anchors[i].IsSelected) continue;
            Gizmos.SetColor(EditorStyle.Palette.Primary);
            Gizmos.DrawRect(path.Anchors[i].Position, EditorStyle.Shape.AnchorSize, order: 5);
        }
    }


    #endregion
}
