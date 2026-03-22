//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteEditor
{
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];

    public bool HasPathSelection { get; private set; }

    private bool SnapToPixelGrid => Input.IsCtrlDown(InputScope.All);

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

    private void SelectAnchor(Shape shape, ushort anchorIndex, bool toggle)
    {
        if (toggle)
        {
            var anchor = shape.GetAnchor(anchorIndex);
            var isSelected = (anchor.Flags & Shape.AnchorFlags.Selected) != 0;
            shape.SetAnchorSelected(anchorIndex, !isSelected);
        }
        else
        {
            ClearAllSelections();
            shape.SetAnchorSelected(anchorIndex, true);
        }

        UpdateSelection();
    }

    private void SelectSegment(Shape shape, ushort anchorIndex, bool toggle)
    {
        var pathIdx = FindPathForAnchor(shape, anchorIndex);
        if (pathIdx == ushort.MaxValue)
            return;

        var path = shape.GetPath(pathIdx);
        var nextAnchor = (ushort)(path.AnchorStart + ((anchorIndex - path.AnchorStart + 1) % path.AnchorCount));

        if (toggle)
        {
            var anchor1 = shape.GetAnchor(anchorIndex);
            var anchor2 = shape.GetAnchor(nextAnchor);
            var bothSelected = (anchor1.Flags & Shape.AnchorFlags.Selected) != 0 &&
                               (anchor2.Flags & Shape.AnchorFlags.Selected) != 0;
            shape.SetAnchorSelected(anchorIndex, !bothSelected);
            shape.SetAnchorSelected(nextAnchor, !bothSelected);
        }
        else
        {
            ClearAllSelections();
            shape.SetAnchorSelected(anchorIndex, true);
            shape.SetAnchorSelected(nextAnchor, true);
        }

        UpdateSelection();
    }

    private void SelectPath(Shape shape, ushort pathIndex, bool toggle)
    {
        var path = shape.GetPath(pathIndex);

        if (toggle)
        {
            var allSelected = true;
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + a));
                if ((anchor.Flags & Shape.AnchorFlags.Selected) == 0)
                {
                    allSelected = false;
                    break;
                }
            }
            for (ushort a = 0; a < path.AnchorCount; a++)
                shape.SetAnchorSelected((ushort)(path.AnchorStart + a), !allSelected);
        }
        else
        {
            ClearAllSelections();
            for (ushort a = 0; a < path.AnchorCount; a++)
                shape.SetAnchorSelected((ushort)(path.AnchorStart + a), true);
        }

        UpdateSelection();
    }

    private void SelectAll()
    {
        var shape = CurrentShape;
        for (ushort i = 0; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected(i, true);
        UpdateSelection();
    }

    private void ClearSelection()
    {
        ClearAllSelections();
        HasPathSelection = false;
    }

    private void UpdateSelection()
    {
        HasPathSelection = GetShapeWithSelection() != null;
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

        CurrentShape.SelectAnchors(localRect);

        UpdateSelection();
    }

    private void OnSelectionChanged(bool hasSelection)
    {
        var shape = CurrentShape;
        for (ushort p = (ushort)(shape.PathCount - 1); p < shape.PathCount; p--)
        {
            ref readonly var path = ref shape.GetPath(p);
            if (!path.IsSelected) continue;

            Document.CurrentFillColor = path.FillColor;
            Document.CurrentStrokeColor = path.StrokeColor;
            Document.CurrentStrokeWidth = (byte)int.Max(1, (int)path.StrokeWidth);
            Document.CurrentOperation = path.Operation;
        }
    }

    private void ClearAllSelections()
    {
        for (ushort fi = 0; fi < Document.FrameCount; fi++)
            Document.Frames[fi].Shape.ClearSelection();
    }

    private void InvalidateMesh() => _meshVersion = -1;

    private Shape? GetShapeWithSelection()
    {
        var shape = CurrentShape;
        if (shape.HasSelection()) return shape;
        return null;
    }

    #endregion

    #region Shape Manipulation

    private void DeleteSelected()
    {
        var shape = GetShapeWithSelection();
        if (shape == null) return;

        Undo.Record(Document);
        shape.DeleteAnchors();
        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.IncrementVersion();
        Document.UpdateBounds();
        HasPathSelection = false;
    }

    private void DuplicateSelected()
    {
        var shape = GetShapeWithSelection();
        if (shape == null)
            return;

        Undo.Record(Document);

        Span<ushort> pathsToDuplicate = stackalloc ushort[Shape.MaxPaths];
        var pathCount = 0;

        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            var hasSelected = false;
            for (ushort a = 0; a < path.AnchorCount; a++)
                if (shape.GetAnchor((ushort)(path.AnchorStart + a)).IsSelected) { hasSelected = true; break; }
            if (hasSelected)
                pathsToDuplicate[pathCount++] = p;
        }

        if (pathCount == 0)
            return;

        shape.ClearAnchorSelection();

        var firstNewAnchor = shape.AnchorCount;

        for (var i = 0; i < pathCount; i++)
        {
            var srcPath = shape.GetPath(pathsToDuplicate[i]);
            var newPathIndex = shape.AddPath(
                fillColor: srcPath.FillColor,
                strokeColor: srcPath.StrokeColor,
                strokeWidth: srcPath.StrokeWidth,
                operation: srcPath.Operation);
            if (newPathIndex == ushort.MaxValue)
                break;

            for (ushort a = 0; a < srcPath.AnchorCount; a++)
            {
                var srcAnchor = shape.GetAnchor((ushort)(srcPath.AnchorStart + a));
                shape.AddAnchor(newPathIndex, srcAnchor.Position, srcAnchor.Curve);
            }
        }

        for (var i = firstNewAnchor; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected((ushort)i, true);

        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.IncrementVersion();
        Document.UpdateBounds();

        BeginMoveTool();
    }

    private void CopySelected()
    {
        var shape = GetShapeWithSelection();
        if (shape == null)
            return;

        var data = new PathClipboardData(shape);
        if (data.Paths.Length == 0)
            return;

        Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null)
            return;

        Undo.Record(Document);

        var shape = CurrentShape;
        ClearAllSelections();

        clipboardData.PasteInto(shape);

        Document.IncrementVersion();
        Document.UpdateBounds();
        UpdateSelection();
    }

    #endregion

    #region Tools

    public void BeginPenTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new PenTool((IShapeDocument)Document, shape, Document.CurrentFillColor, Document.CurrentOperation));
    }

    public void BeginKnifeTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new KnifeTool((IShapeDocument)Document, shape, commit: () =>
        {
            shape.UpdateSamples();
            shape.UpdateBounds();
        }));
    }

    public void BeginRectangleTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new ShapeTool((IShapeDocument)Document, shape, Document.CurrentFillColor, ShapeType.Rectangle, Document.CurrentOperation));
    }

    public void BeginCircleTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new ShapeTool((IShapeDocument)Document, shape, Document.CurrentFillColor, ShapeType.Circle, Document.CurrentOperation));
    }

    private void BeginMoveTool()
    {
        var shape = GetShapeWithSelection() ?? CurrentShape;
        if (!shape.HasSelection())
            return;

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: delta =>
            {
                shape.TranslateAnchors(delta, _savedPositions, Input.IsCtrlDown(InputScope.All));
                shape.UpdateSamples();
                shape.UpdateBounds();
                InvalidateMesh();
            },
            commit: _ =>
            {
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void BeginRotateTool()
    {
        var shape = GetShapeWithSelection() ?? CurrentShape;
        var localPivot = shape.GetSelectedAnchorsCentroid();
        if (!localPivot.HasValue)
            return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);
        Matrix3x2.Invert(Document.Transform, out var invTransform);

        Undo.Record(Document);

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Workspace.BeginTool(new RotateTool(
            worldPivot,
            localPivot.Value,
            worldOrigin,
            Vector2.Zero,
            invTransform,
            update: angle =>
            {
                var pivot = Input.IsShiftDown() ? Vector2.Zero : localPivot.Value;
                shape.RotateAnchors(pivot, angle, _savedPositions);
                if (SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
            },
            commit: _ =>
            {
                if (SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void OnScale()
    {
        var shape = GetShapeWithSelection() ?? CurrentShape;
        var localPivot = shape.GetSelectedAnchorsCentroid();
        if (!localPivot.HasValue)
            return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);

        Undo.Record(Document);

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            _savedPositions[i] = shape.GetAnchor(i).Position;
            _savedCurves[i] = shape.GetAnchor(i).Curve;
        }

        Workspace.BeginTool(new ScaleTool(
            worldPivot,
            worldOrigin,
            update: scale =>
            {
                var pivot = Input.IsShiftDown(InputScope.All) ? Vector2.Zero : localPivot.Value;
                shape.ScaleAnchors(pivot, scale, _savedPositions, _savedCurves);
                if (SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
            },
            commit: _ =>
            {
                if (SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.RestoreAnchorCurves(_savedCurves);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void BeginCurveTool()
    {
        var shape = GetShapeWithSelection() ?? CurrentShape;

        var hasSelectedSegment = false;
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (shape.IsSegmentSelected(i))
            {
                hasSelectedSegment = true;
                break;
            }
        }

        if (!hasSelectedSegment)
            return;

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedCurves[i] = shape.GetAnchor(i).Curve;

        Undo.Record(Document);

        Workspace.BeginTool(new CurveTool(
            shape,
            Document.Transform,
            _savedCurves,
            update: () => Document.IncrementVersion(),
            commit: () =>
            {
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                Undo.Cancel();
            }
        ));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = CurrentShape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform));

        if (hit.SegmentIndex == ushort.MaxValue)
            return;

        Undo.Record(Document);

        var shape = CurrentShape;
        shape.ClearSelection();
        shape.SplitSegmentAtPoint(hit.SegmentIndex, hit.SegmentPosition);

        var newAnchorIdx = (ushort)(hit.SegmentIndex + 1);
        if (newAnchorIdx < shape.AnchorCount)
            shape.SetAnchorSelected(newAnchorIdx, true);

        Document.UpdateBounds();

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        BeginMoveTool();
    }

    #endregion

    #region Drawing (Shape)

    private static void DrawShapeSegments(Shape shape)
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Gizmos.SetColor(EditorStyle.Palette.PathSegment);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (!shape.IsSegmentSelected(anchorIndex))
                    DrawShapeSegment(shape, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 1);
            }

            Gizmos.SetColor(EditorStyle.Palette.Primary);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (shape.IsSegmentSelected(anchorIndex))
                    DrawShapeSegment(shape, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 2);
            }
        }
    }

    private static void DrawShapeSegment(Shape shape, ushort segmentIndex, float width, ushort order = 0)
    {
        var samples = shape.GetSegmentSamples(segmentIndex);
        ref readonly var anchor = ref shape.GetAnchor(segmentIndex);

        var prev = anchor.Position;
        foreach (var sample in samples)
        {
            Gizmos.DrawLine(prev, sample, width, order: order);
            prev = sample;
        }

        ref readonly var nextAnchor = ref shape.GetNextAnchor(segmentIndex);
        Gizmos.DrawLine(prev, nextAnchor.Position, width, order: order);
    }

    private static void DrawShapeAnchors(Shape shape, bool selectedOnly = false)
    {
        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);

        if (!selectedOnly)
        {
            for (ushort i = 0; i < shape.AnchorCount; i++)
            {
                ref readonly var anchor = ref shape.GetAnchor(i);
                if (anchor.IsSelected) continue;
                Gizmos.SetColor(EditorStyle.Palette.PathAnchor);
                Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize, order: 4);
            }
        }

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (!anchor.IsSelected) continue;
            Gizmos.SetColor(EditorStyle.Palette.Primary);
            Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize, order: 5);
        }
    }

    private static ushort FindPathForAnchor(Shape shape, ushort anchorIndex)
    {
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            var path = shape.GetPath(p);
            if (anchorIndex >= path.AnchorStart && anchorIndex < path.AnchorStart + path.AnchorCount)
                return p;
        }
        return ushort.MaxValue;
    }

    #endregion
}
