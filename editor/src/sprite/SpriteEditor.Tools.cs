//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum SpriteEditMode
{
    V,  // Transform mode — select/move/rotate/scale paths
    A,  // Anchor mode — select/edit anchors within selected paths
}

public partial class SpriteEditor
{
    public bool HasPathSelection { get; private set; }
    public SpriteEditMode CurrentMode { get; private set; } = SpriteEditMode.V;

    private readonly List<SpritePath> _selectedPaths = new();
    private float _selectionRotation; // rotation of the selection bounding box
    private Rect _selectionLocalBounds; // AABB in selection-rotated space
    private Vector2 _selectionCenter; // center in document-local space

    // A-mode hover state
    private SpritePath? _hoverPath;
    private int _hoverAnchorIndex = -1;

    // V-mode hover state
    private SpritePathHandle _hoverHandle;

    private SpriteLayer ActiveLayer => Document.ActiveLayer ?? Document.RootLayer;

    private SpritePath? GetPathWithSelection()
    {
        return Document.RootLayer.GetPathWithSelection();
    }

    private SpritePath? GetFirstSelectedPath()
    {
        return _selectedPaths.Count > 0 ? _selectedPaths[0] : null;
    }

    private void SetMode(SpriteEditMode mode)
    {
        if (CurrentMode == mode) return;
        CurrentMode = mode;

        if (mode == SpriteEditMode.V)
            Document.RootLayer.ClearAnchorSelections();
    }

    #region Commands

    private Command[] GetShapeCommands()
    {
        return
        [
            new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete },
            new Command { Name = "V Mode", Handler = () => SetMode(SpriteEditMode.V), Key = InputCode.KeyV },
            new Command { Name = "A Mode", Handler = () => SetMode(SpriteEditMode.A), Key = InputCode.KeyA },
            new Command { Name = "Curve", Handler = BeginCurveTool, Key = InputCode.KeyC },
            new Command { Name = "Select All", Handler = SelectAll, Key = InputCode.KeyA, Ctrl = true },
            new Command { Name = "Pen Tool", Handler = BeginPenTool, Key = InputCode.KeyP },
            new Command { Name = "Knife Tool", Handler = BeginKnifeTool, Key = InputCode.KeyK },
            new Command { Name = "Rectangle Tool", Handler = BeginRectangleTool, Key = InputCode.KeyR, Ctrl = true },
            new Command { Name = "Circle Tool", Handler = BeginCircleTool, Key = InputCode.KeyO, Ctrl = true },
            new Command { Name = "Duplicate", Handler = DuplicateSelected, Key = InputCode.KeyD, Ctrl = true },
            new Command { Name = "Copy", Handler = CopySelected, Key = InputCode.KeyC, Ctrl = true },
            new Command { Name = "Paste", Handler = PasteSelected, Key = InputCode.KeyV, Ctrl = true },
            new Command { Name = "Cut", Handler = CutSelected, Key = InputCode.KeyX, Ctrl = true },
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
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.DragWorldPosition, invTransform);

        if (CurrentMode == SpriteEditMode.V && _selectedPaths.Count > 0 && HandleVModeDrag(localMousePos))
            return;

        if (CurrentMode == SpriteEditMode.A && _selectedPaths.Count > 0 && HandleAModeDrag(localMousePos))
            return;

        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelect));
    }

    private bool HandleVModeDrag(Vector2 localMousePos)
    {
        var handleHit = HitTestHandles(localMousePos);

        if (IsRotateHandle(handleHit))
        {
            var tool = RotatePathTransformTool.Create(Document, _selectedPaths);
            if (tool != null)
            {
                tool.CommitOnRelease = true;
                Undo.Record(Document);
                Workspace.BeginTool(tool);
                return true;
            }
        }

        if (IsScaleHandle(handleHit))
        {
            var selToDoc = Matrix3x2.CreateRotation(_selectionRotation);
            var pivotSel = GetOppositePivotInSelSpace(handleHit);
            var pivotDoc = Vector2.Transform(pivotSel, selToDoc);

            var tool = HandleScalePathTransformTool.Create(
                Document, _selectedPaths,
                pivotDoc, _selectionRotation, handleHit);
            if (tool != null)
            {
                Undo.Record(Document);
                Workspace.BeginTool(tool);
                return true;
            }
        }

        if (handleHit == SpritePathHandle.Move)
        {
            var tool = MovePathTransformTool.Create(Document, _selectedPaths);
            if (tool != null)
            {
                tool.CommitOnRelease = true;
                Undo.Record(Document);
                Workspace.BeginTool(tool);
                return true;
            }
        }

        return false;
    }

    private bool HandleAModeDrag(Vector2 localMousePos)
    {
        // Alt: insert anchor on segment edge, then drag it
        if (Input.IsAltDown(InputScope.All))
        {
            var segHit = Document.RootLayer.HitTestSegment(localMousePos);
            if (segHit.HasValue && segHit.Value.Path.IsSelected)
            {
                Undo.Record(Document);
                var path = segHit.Value.Path;
                var ci = segHit.Value.ContourIndex;
                path.ClearAnchorSelection();
                path.SplitSegmentAtPoint(ci, segHit.Value.SegmentIndex, segHit.Value.Position);

                var newIdx = segHit.Value.SegmentIndex + 1;
                if (newIdx < path.Contours[ci].Anchors.Count)
                    path.SetAnchorSelected(ci, newIdx, true);

                path.UpdateSamples();
                path.UpdateBounds();
                Document.UpdateBounds();

                var moveTool = AnchorMoveTool.Create(Document);
                if (moveTool != null)
                {
                    Workspace.BeginTool(moveTool);
                    return true;
                }
            }
        }

        // Check for anchor hit — start move
        var anchorHit = Document.RootLayer.HitTestAnchor(localMousePos, onlySelected: true);
        if (anchorHit.HasValue)
        {
            if (!anchorHit.Value.Path.Contours[anchorHit.Value.ContourIndex].Anchors[anchorHit.Value.AnchorIndex].IsSelected)
            {
                Document.RootLayer.ClearAnchorSelections();
                anchorHit.Value.Path.SetAnchorSelected(anchorHit.Value.ContourIndex, anchorHit.Value.AnchorIndex, true);
            }

            var tool = AnchorMoveTool.Create(Document);
            if (tool != null)
            {
                Undo.Record(Document);
                Workspace.BeginTool(tool);
                return true;
            }
        }

        // Drag on segment edge — adjust curve
        var segDragHit = Document.RootLayer.HitTestSegment(localMousePos);
        if (segDragHit.HasValue && segDragHit.Value.Path.IsSelected)
        {
            var path = segDragHit.Value.Path;
            var segCi = segDragHit.Value.ContourIndex;
            Undo.Record(Document);
            Workspace.BeginTool(new CurveTool(Document, path, Document.Transform, path.SnapshotAnchors(segCi), segDragHit.Value.SegmentIndex, contourIndex: segCi) { CommitOnRelease = true });
            return true;
        }

        return false;
    }

    #endregion

    #region Selection

    private void SelectAll()
    {
        Document.RootLayer.ForEachEditablePath(p => p.SelectPath());
        RebuildSelectedPaths();
    }

    private void ClearSelection()
    {
        Document.RootLayer.ClearSelection();
        RebuildSelectedPaths();
    }

    private void RebuildSelectedPaths()
    {
        _selectedPaths.Clear();
        Document.RootLayer.CollectSelectedPaths(_selectedPaths);
        HasPathSelection = _selectedPaths.Count > 0;
        UpdateSelectionBounds();
        OnSelectionChanged(HasPathSelection);
    }

    private void UpdateSelectionBounds()
    {
        if (_selectedPaths.Count == 0)
        {
            _selectionRotation = 0;
            _selectionLocalBounds = Rect.Zero;
            _selectionCenter = Vector2.Zero;
            return;
        }

        // If all selected paths share the same rotation, use it; otherwise axis-aligned
        var firstRotation = _selectedPaths[0].PathRotation;
        var allSameRotation = true;
        for (var i = 1; i < _selectedPaths.Count; i++)
        {
            if (MathF.Abs(_selectedPaths[i].PathRotation - firstRotation) > 0.001f)
            {
                allSameRotation = false;
                break;
            }
        }

        _selectionRotation = allSameRotation ? firstRotation : 0;
        ComputeOrientedBounds();
    }

    private void ComputeOrientedBounds()
    {
        // Transform each anchor and curve sample through PathTransform into document space,
        // then into selection-rotated space to compute a tight AABB.
        var invRot = Matrix3x2.CreateRotation(-_selectionRotation);
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var path in _selectedPaths)
        {
            if (path.TotalAnchorCount < 2) continue;

            // Compose: anchor-local → document → selection space
            var toSelSpace = path.HasTransform
                ? path.PathTransform * invRot
                : invRot;

            foreach (var contour in path.Contours)
            {
                var count = contour.Anchors.Count;
                if (count < 2) continue;
                var segmentCount = contour.Open ? count - 1 : count;

                for (var i = 0; i < count; i++)
                {
                    var p = Vector2.Transform(contour.Anchors[i].Position, toSelSpace);
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);

                    if (i < segmentCount && MathF.Abs(contour.Anchors[i].Curve) > SpritePath.MinCurve)
                    {
                        var samples = contour.GetSegmentSamples(i);
                        foreach (var sample in samples)
                        {
                            var sp = Vector2.Transform(sample, toSelSpace);
                            min = Vector2.Min(min, sp);
                            max = Vector2.Max(max, sp);
                        }
                    }
                }
            }
        }

        if (min.X <= max.X)
        {
            _selectionLocalBounds = Rect.FromMinMax(min, max);
            _selectionCenter = Vector2.Transform(_selectionLocalBounds.Center, Matrix3x2.CreateRotation(_selectionRotation));
        }
        else
        {
            _selectionLocalBounds = Rect.Zero;
            _selectionCenter = Vector2.Zero;
        }
    }

    private void CommitBoxSelect(Rect bounds)
    {
        var shift = Input.IsShiftDown(InputScope.All);

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);

        if (CurrentMode == SpriteEditMode.A && _selectedPaths.Count > 0)
        {
            // A mode with selected paths: box select anchors within selected paths
            if (!shift)
            {
                foreach (var p in _selectedPaths)
                    p.ClearAnchorSelection();
            }
            foreach (var p in _selectedPaths)
                p.SelectAnchorsInRect(localRect);
        }
        else
        {
            // V mode or A mode with no paths: box select paths
            if (!shift)
                Document.RootLayer.ClearPathSelections();
            Document.RootLayer.SelectPathsInRect(localRect);
        }

        RebuildSelectedPaths();
    }

    private void OnSelectionChanged(bool hasSelection)
    {
        var path = GetFirstSelectedPath();
        if (path != null)
        {
            Document.CurrentFillColor = path.FillColor;
            Document.CurrentStrokeColor = path.StrokeColor;
            Document.CurrentStrokeWidth = (byte)int.Max(1, (int)path.StrokeWidth);
            Document.CurrentStrokeJoin = path.StrokeJoin;
            Document.CurrentOperation = path.Operation;
        }
    }

    private void InvalidateMesh() => _meshVersion = -1;

    #endregion

    #region Manipulation

    private void DeleteSelected()
    {
        if (CurrentMode == SpriteEditMode.V)
        {
            // V mode: delete entire selected paths
            if (_selectedPaths.Count == 0) return;

            Undo.Record(Document);
            foreach (var path in _selectedPaths)
                path.RemoveFromParent();
            Document.IncrementVersion();
            Document.UpdateBounds();
            ClearSelection();
        }
        else
        {
            // A mode: delete selected anchors
            var path = GetPathWithSelection();
            if (path == null) return;

            Undo.Record(Document);
            var oldCenter = path.LocalBounds.Center;
            path.DeleteSelectedAnchors();

            if (path.TotalAnchorCount < 3)
                path.RemoveFromParent();
            else
            {
                path.UpdateSamples();
                path.UpdateBounds();
                path.CompensateTranslation(oldCenter);
            }

            Document.IncrementVersion();
            Document.UpdateBounds();
            RebuildSelectedPaths();
        }
    }

    private void DuplicateSelected()
    {
        if (_selectedPaths.Count == 0) return;

        Undo.Record(Document);

        // Duplicate all selected paths
        foreach (var path in _selectedPaths)
        {
            var parent = path.Parent;
            if (parent == null) continue;

            var clone = path.ClonePath();
            clone.ClearAnchorSelection();
            path.DeselectPath();
            clone.SelectPath();

            parent.Add(clone);
            clone.UpdateSamples();
            clone.UpdateBounds();
        }

        Document.IncrementVersion();
        Document.UpdateBounds();
        RebuildSelectedPaths();
    }

    private void CopySelected()
    {
        if (_selectedPaths.Count == 0) return;

        var data = new PathClipboardData(_selectedPaths);
        if (data.Paths.Length > 0)
            Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null) return;

        Undo.Record(Document);
        Document.RootLayer.ClearSelection();

        var newPaths = clipboardData.PasteAsPaths();
        foreach (var path in newPaths)
            ActiveLayer.Add(path);

        Document.UpdateBounds();
        RebuildSelectedPaths();
    }

    private void CutSelected()
    {
        if (_selectedPaths.Count == 0) return;

        CopySelected();
        DeleteSelected();
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
        if (_selectedPaths.Count == 0) return;
        Workspace.BeginTool(new KnifeTool(Document, _selectedPaths));
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

    private void BeginAnchorMoveTool()
    {
        var tool = AnchorMoveTool.Create(Document);
        if (tool == null) return;
        Undo.Record(Document);
        Workspace.BeginTool(tool);
    }

    private void BeginCurveTool()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        // Find first contour with a selected segment
        var foundContour = -1;
        for (var ci = 0; ci < path.Contours.Count; ci++)
        {
            var contour = path.Contours[ci];
            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                if (path.IsSegmentSelected(ci, i))
                {
                    foundContour = ci;
                    break;
                }
            }
            if (foundContour >= 0) break;
        }

        if (foundContour < 0) return;

        Undo.Record(Document);
        Workspace.BeginTool(new CurveTool(Document, path, Document.Transform, path.SnapshotAnchors(foundContour), contourIndex: foundContour));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        var hit = Document.RootLayer.HitTestSegment(mouseLocal);
        if (!hit.HasValue) return;

        Undo.Record(Document);

        var path = hit.Value.Path;
        var ci = hit.Value.ContourIndex;
        path.ClearAnchorSelection();
        path.SplitSegmentAtPoint(ci, hit.Value.SegmentIndex, hit.Value.Position);

        var newIdx = hit.Value.SegmentIndex + 1;
        if (newIdx < path.Contours[ci].Anchors.Count)
            path.SetAnchorSelected(ci, newIdx, true);

        path.UpdateSamples();
        path.UpdateBounds();
        Document.UpdateBounds();

        BeginAnchorMoveTool();
    }

    #endregion

    #region Drawing (SpritePath)

    private static void DrawPathSegments(SpritePath path, Matrix3x2 localTransform, Matrix3x2 docTransform)
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(docTransform);

            for (var ci = 0; ci < path.Contours.Count; ci++)
            {
                var contour = path.Contours[ci];
                var segmentCount = contour.Open ? contour.Anchors.Count - 1 : contour.Anchors.Count;

                Gizmos.SetColor(EditorStyle.Palette.PathSegment);
                for (var i = 0; i < segmentCount; i++)
                {
                    if (!path.IsSegmentSelected(ci, i))
                        DrawContourSegment(contour, i, localTransform, EditorStyle.SpritePath.SegmentLineWidth, 1);
                }

                Gizmos.SetColor(EditorStyle.Palette.Primary);
                for (var i = 0; i < segmentCount; i++)
                {
                    if (path.IsSegmentSelected(ci, i))
                        DrawContourSegment(contour, i, localTransform, EditorStyle.SpritePath.SegmentLineWidth, 2);
                }
            }
        }
    }

    private static void DrawContourSegment(SpriteContour contour, int segmentIndex, Matrix3x2 localTransform, float width, ushort order = 0)
    {
        var samples = contour.GetSegmentSamples(segmentIndex);
        var prev = Vector2.Transform(contour.Anchors[segmentIndex].Position, localTransform);
        foreach (var sample in samples)
        {
            var transformed = Vector2.Transform(sample, localTransform);
            Gizmos.DrawLine(prev, transformed, width, order: order);
            prev = transformed;
        }
        var nextIdx = (segmentIndex + 1) % contour.Anchors.Count;
        Gizmos.DrawLine(prev, Vector2.Transform(contour.Anchors[nextIdx].Position, localTransform), width, order: order);
    }

    private void DrawPathAnchors(SpritePath path, Matrix3x2 localTransform, Matrix3x2 docTransform, bool selectedOnly = false)
    {
        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);
        Graphics.SetTransform(docTransform);

        var isHoverPath = path == _hoverPath;

        foreach (var contour in path.Contours)
        {
            if (!selectedOnly)
            {
                for (var i = 0; i < contour.Anchors.Count; i++)
                {
                    if (contour.Anchors[i].IsSelected) continue;
                    var pos = Vector2.Transform(contour.Anchors[i].Position, localTransform);
                    var hovered = isHoverPath && contour == path.Contours[0] && i == _hoverAnchorIndex;
                    Gizmos.DrawAnchor(pos, selected: false, scale: hovered ? 1.3f : 1.0f, order: 4);
                }
            }

            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                if (!contour.Anchors[i].IsSelected) continue;
                Gizmos.DrawAnchor(Vector2.Transform(contour.Anchors[i].Position, localTransform), selected: true, order: 5);
            }
        }
    }


    #endregion
}
