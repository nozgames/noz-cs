//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum SpriteEditMode
{
    Transform,
    Anchor,
}

public partial class SpriteEditor
{
    public bool HasPathSelection { get; private set; }
    public bool HasLayerSelection { get; private set; }
    public SpriteEditMode CurrentMode { get; private set; } = SpriteEditMode.Transform;

    private readonly List<SpritePath> _selectedPaths = new();
    private readonly List<SpriteLayer> _selectedLayers = new();
    private float _selectionRotation;
    private Rect _selectionLocalBounds;
    private Vector2 _selectionCenter;
    private SpritePath? _hoverPath;
    private int _hoverAnchorIndex = -1;
    private SpritePathHandle _hoverHandle;

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

        if (mode == SpriteEditMode.Transform)
            Document.RootLayer.ClearAnchorSelections();
    }

    #region Commands

    private Command[] GetShapeCommands()
    {
        return
        [
            new Command("Delete", DeleteSelected, [InputCode.KeyX, InputCode.KeyDelete], icon:EditorAssets.Sprites.IconDelete),
            new Command("V Mode", () => SetMode(SpriteEditMode.Transform), [InputCode.KeyV]),
            new Command("A Mode", () => SetMode(SpriteEditMode.Anchor), [InputCode.KeyA]),
            new Command("Curve", BeginCurveTool, [InputCode.KeyC]),
            new Command("Bevel", BeginBevelTool, [InputCode.KeyB]),
            new Command("Select All", SelectAll, [new KeyBinding(InputCode.KeyA, ctrl: true)]),
            new Command("Pen Tool", BeginPenTool, [InputCode.KeyP]),
            new Command("Rectangle Tool", BeginRectangleTool, [new KeyBinding(InputCode.KeyR, ctrl: true)]),
            new Command("Circle Tool", BeginCircleTool, [new KeyBinding(InputCode.KeyO, ctrl: true)]),
            new Command("Duplicate", DuplicateSelected, [new KeyBinding(InputCode.KeyD, ctrl: true)]),
            new Command("Copy", CopySelected, [new KeyBinding(InputCode.KeyC, ctrl: true)]),
            new Command("Paste", PasteSelected, [new KeyBinding(InputCode.KeyV, ctrl: true)]),
            new Command("Cut", CutSelected, [new KeyBinding(InputCode.KeyX, ctrl:true)]),
            new Command("Rename", BeginRename, [InputCode.KeyF2]),
        ];
    }

    #endregion

    #region Input


    private void HandleDragStart()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.DragWorldPosition, invTransform);

        if (CurrentMode == SpriteEditMode.Transform && _selectedPaths.Count > 0 && HandleVModeDrag(localMousePos))
            return;

        if (CurrentMode == SpriteEditMode.Anchor && _selectedPaths.Count > 0 && HandleAModeDrag(localMousePos))
            return;

        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelect));
    }

    private bool HandleVModeDrag(Vector2 localMousePos)
    {
        var handleHit = HitTestHandles(localMousePos);

        if (IsRotateHandle(handleHit))
        {
            var tool = RotatePathTransformTool.Create(Document, this, _selectedPaths);
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
                Document, this, _selectedPaths,
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
            var tool = MovePathTransformTool.Create(Document, this, _selectedPaths);
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
            var segHit = Document.RootLayer.HitTestSegment(localMousePos, onlySelected: true);
            if (segHit.HasValue)
            {
                Undo.Record(Document);
                var path = segHit.Value.Path;
                var ci = segHit.Value.ContourIndex;
                path.ClearAnchorSelection();
                path.SplitSegmentAtPoint(ci, segHit.Value.SegmentIndex, segHit.Value.Position);

                var newIdx = segHit.Value.SegmentIndex + 1;
                if (newIdx < path.Contours[ci].Anchors.Count)
                    path.SetAnchorSelected(ci, newIdx, true);

                var oldCenter = path.LocalBounds.Center;
                path.UpdateSamples();
                path.UpdateBounds();
                path.CompensateTranslation(oldCenter);
                MarkDirty();

                var moveTool = AnchorMoveTool.Create(Document, this);
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

            var tool = AnchorMoveTool.Create(Document, this);
            if (tool != null)
            {
                Undo.Record(Document);
                Workspace.BeginTool(tool);
                return true;
            }
        }

        // Drag on segment edge — adjust curve
        var segDragHit = Document.RootLayer.HitTestSegment(localMousePos, onlySelected: true);
        if (segDragHit.HasValue)
        {
            var path = segDragHit.Value.Path;
            var segCi = segDragHit.Value.ContourIndex;
            Undo.Record(Document);
            Workspace.BeginTool(new CurveTool(this, path, Document.Transform, path.SnapshotAnchors(segCi), segDragHit.Value.SegmentIndex, contourIndex: segCi) { CommitOnRelease = true });
            return true;
        }

        return false;
    }

    #endregion

    #region Selection

    private void SelectAll()
    {
        if (CurrentMode == SpriteEditMode.Anchor && _selectedPaths.Count > 0)
        {
            foreach (var path in _selectedPaths)
                path.SelectAll();
        }
        else
        {
            Document.RootLayer.ForEachEditablePath(p => p.SelectPath());
            RebuildSelectedPaths();
        }
    }

    private void ClearSelection()
    {
        Document.RootLayer.ClearSelection();
        Document.RootLayer.ClearLayerSelections();
        RebuildSelectedPaths();
    }

    private void RebuildSelectedPaths()
    {
        _selectedPaths.Clear();
        _selectedLayers.Clear();
        Document.RootLayer.CollectSelectedLayers(_selectedLayers);
        HasLayerSelection = _selectedLayers.Count > 0;

        if (HasLayerSelection)
        {
            // Layer selection: collect all editable paths from selected layers for bounds/transforms
            foreach (var layer in _selectedLayers)
                layer.ForEachEditablePath(p => _selectedPaths.Add(p));
        }
        else
        {
            Document.RootLayer.CollectSelectedPaths(_selectedPaths);
        }

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

        if (CurrentMode == SpriteEditMode.Anchor && _selectedPaths.Count > 0)
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

    private void InvalidateMesh() => _meshDirty = true;

    internal void MarkDirty()
    {
        InvalidateMesh();
        Document.UpdateBounds();
    }

    #endregion

    #region Manipulation

    private void DeleteSelected()
    {
        if (HasLayerSelection)
        {
            // Don't delete the last layer
            var remaining = Document.RootLayer.Children.Count - _selectedLayers.Count;
            if (remaining < 1) return;

            Undo.Record(Document);
            foreach (var layer in _selectedLayers)
                layer.RemoveFromParent();
            MarkDirty();
            ClearSelection();
        }
        else if (CurrentMode == SpriteEditMode.Transform)
        {
            // V mode: delete entire selected paths
            if (_selectedPaths.Count == 0) return;

            Undo.Record(Document);
            foreach (var path in _selectedPaths)
                path.RemoveFromParent();
            MarkDirty();
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

            MarkDirty();
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

            parent.Insert(0, clone);
            clone.UpdateSamples();
            clone.UpdateBounds();
        }

        MarkDirty();
        RebuildSelectedPaths();
    }

    private void CopySelected()
    {
        if (HasLayerSelection)
        {
            Clipboard.Copy(new NodeClipboardData(_selectedLayers));
            return;
        }

        if (_selectedPaths.Count == 0) return;

        var data = new PathClipboardData(_selectedPaths);
        if (data.Paths.Length > 0)
            Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var nodeData = Clipboard.Get<NodeClipboardData>();
        if (nodeData != null)
        {
            Undo.Record(Document);
            Document.RootLayer.ClearSelection();
            Document.RootLayer.ClearLayerSelections();

            var nodes = nodeData.PasteAsNodes();
            foreach (var node in nodes)
                Document.RootLayer.Insert(0, node);

            MarkDirty();
            RebuildSelectedPaths();
            return;
        }

        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null) return;

        Undo.Record(Document);
        Document.RootLayer.ClearSelection();

        var newPaths = clipboardData.PasteAsPaths();
        foreach (var path in newPaths)
            Document.RootLayer.Insert(0, path);

        MarkDirty();
        RebuildSelectedPaths();
    }

    private void CutSelected()
    {
        if (!HasLayerSelection && _selectedPaths.Count == 0) return;

        CopySelected();
        DeleteSelected();
    }

    #endregion

    #region Tools

    public void BeginPenTool()
    {
        Workspace.BeginTool(new PenTool(this, Document.RootLayer, Document.RootLayer,
            Document.CurrentFillColor, Document.CurrentOperation));
    }


    public void BeginRectangleTool()
    {
        Workspace.BeginTool(new ShapeTool(this, Document.RootLayer,
            Document.CurrentFillColor, ShapeType.Rectangle, Document.CurrentOperation));
    }

    public void BeginCircleTool()
    {
        Workspace.BeginTool(new ShapeTool(this, Document.RootLayer,
            Document.CurrentFillColor, ShapeType.Circle, Document.CurrentOperation));
    }

    private void BeginAnchorMoveTool()
    {
        var tool = AnchorMoveTool.Create(Document, this);
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
        Workspace.BeginTool(new CurveTool(this, path, Document.Transform, path.SnapshotAnchors(foundContour), contourIndex: foundContour));
    }

    private void BeginBevelTool()
    {
        var path = GetPathWithSelection();
        if (path == null) return;

        for (var ci = 0; ci < path.Contours.Count; ci++)
        {
            var contour = path.Contours[ci];
            if (contour.Anchors.Count < 3) continue;

            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                if (!contour.Anchors[i].IsSelected) continue;
                if (contour.Open && (i == 0 || i == contour.Anchors.Count - 1)) continue;

                Undo.Record(Document);
                Workspace.BeginTool(new BevelTool(this, path, Document.Transform,
                    path.SnapshotAnchors(ci), i, ci));
                return;
            }
        }
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
        MarkDirty();

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
