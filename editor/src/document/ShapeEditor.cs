//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class ShapeEditor
{
    private readonly IShapeEditorHost _host;
    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];

    public bool HasPathSelection { get; private set; }

    private Document Document => _host.Document;
    private IShapeDocument ShapeDocument => (IShapeDocument)Document;

    public ShapeEditor(IShapeEditorHost host)
    {
        _host = host;
    }

    public Command[] GetCommands()
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

    #region Input

    public void HandleDeleteKey()
    {
        if (Input.WasButtonPressed(InputCode.KeyDelete))
            DeleteSelected();
    }

    public void HandleDragStart()
    {
        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelectAnchors));
    }

    #endregion

    #region Selection

    public void SelectAnchor(Shape shape, ushort anchorIndex, bool toggle)
    {
        if (toggle)
        {
            var anchor = shape.GetAnchor(anchorIndex);
            var isSelected = (anchor.Flags & Shape.AnchorFlags.Selected) != 0;
            shape.SetAnchorSelected(anchorIndex, !isSelected);
        }
        else
        {
            _host.ClearAllSelections();
            shape.SetAnchorSelected(anchorIndex, true);
        }

        UpdateSelection();
    }

    public void SelectSegment(Shape shape, ushort anchorIndex, bool toggle)
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
            _host.ClearAllSelections();
            shape.SetAnchorSelected(anchorIndex, true);
            shape.SetAnchorSelected(nextAnchor, true);
        }

        UpdateSelection();
    }

    public void SelectPath(Shape shape, ushort pathIndex, bool toggle)
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
            _host.ClearAllSelections();
            for (ushort a = 0; a < path.AnchorCount; a++)
                shape.SetAnchorSelected((ushort)(path.AnchorStart + a), true);
        }

        UpdateSelection();
    }

    public void SelectAll()
    {
        var shape = _host.CurrentShape;
        for (ushort i = 0; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected(i, true);
        UpdateSelection();
    }

    public void ClearSelection()
    {
        _host.ClearAllSelections();
        HasPathSelection = false;
    }

    public void UpdateSelection()
    {
        HasPathSelection = _host.GetShapeWithSelection() != null;
        _host.OnSelectionChanged(HasPathSelection);
    }

    public void CommitBoxSelectAnchors(Rect bounds)
    {
        var shift = Input.IsShiftDown(InputScope.All);

        if (!shift)
            _host.ClearAllSelections();

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);

        _host.ForEachEditableShape(shape => shape.SelectAnchors(localRect));

        UpdateSelection();
    }

    #endregion

    #region Shape Manipulation

    public void DeleteSelected()
    {
        var shape = _host.GetShapeWithSelection();
        if (shape == null) return;

        Undo.Record(Document);
        shape.DeleteAnchors();
        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.IncrementVersion();
        ShapeDocument.UpdateBounds();
        HasPathSelection = false;
    }

    public void DuplicateSelected()
    {
        var shape = _host.GetShapeWithSelection();
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
        ShapeDocument.UpdateBounds();

        BeginMoveTool();
    }

    public void CopySelected()
    {
        var shape = _host.GetShapeWithSelection();
        if (shape == null)
            return;

        var data = new PathClipboardData(shape);
        if (data.Paths.Length == 0)
            return;

        Clipboard.Copy(data);
    }

    public void PasteSelected()
    {
        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null)
            return;

        Undo.Record(Document);

        var shape = _host.CurrentShape;
        _host.ClearAllSelections();

        clipboardData.PasteInto(shape);

        Document.IncrementVersion();
        ShapeDocument.UpdateBounds();
        UpdateSelection();
    }

    public void SetPathOperation(PathOperation operation)
    {
        var shape = _host.GetShapeWithSelection();
        if (shape == null) return;

        Undo.Record(Document);
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            var hasSelected = false;
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                if (shape.GetAnchor((ushort)(path.AnchorStart + a)).IsSelected)
                {
                    hasSelected = true;
                    break;
                }
            }
            if (hasSelected)
                shape.SetPathOperation(p, operation);
        }
        Document.IncrementVersion();
    }

    public static PathOperation GetSelectedPathOperation(Shape shape)
    {
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + a));
                if (anchor.IsSelected)
                    return path.Operation;
            }
        }
        return PathOperation.Normal;
    }

    #endregion

    #region Tools

    public void BeginPenTool()
    {
        var shape = _host.CurrentShape;
        Workspace.BeginTool(new PenTool(ShapeDocument, shape, _host.NewPathFillColor, _host.NewPathOperation));
    }

    public void BeginKnifeTool()
    {
        var shape = _host.CurrentShape;
        Workspace.BeginTool(new KnifeTool(ShapeDocument, shape, commit: () =>
        {
            shape.UpdateSamples();
            shape.UpdateBounds();
        }));
    }

    public void BeginRectangleTool()
    {
        var shape = _host.CurrentShape;
        Workspace.BeginTool(new ShapeTool(ShapeDocument, shape, _host.NewPathFillColor, ShapeType.Rectangle, _host.NewPathOperation));
    }

    public void BeginCircleTool()
    {
        var shape = _host.CurrentShape;
        Workspace.BeginTool(new ShapeTool(ShapeDocument, shape, _host.NewPathFillColor, ShapeType.Circle, _host.NewPathOperation));
    }

    public void BeginMoveTool()
    {
        var shape = _host.GetShapeWithSelection() ?? _host.CurrentShape;
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
                _host.InvalidateMesh();
            },
            commit: _ =>
            {
                Document.IncrementVersion();
                ShapeDocument.UpdateBounds();
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

    public void BeginRotateTool()
    {
        var shape = _host.GetShapeWithSelection() ?? _host.CurrentShape;
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
                if (_host.SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
            },
            commit: _ =>
            {
                if (_host.SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
                ShapeDocument.UpdateBounds();
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

    public void OnScale()
    {
        var shape = _host.GetShapeWithSelection() ?? _host.CurrentShape;
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
                if (_host.SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
            },
            commit: _ =>
            {
                if (_host.SnapToPixelGrid)
                    shape.SnapSelectedAnchorsToPixelGrid();
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
                ShapeDocument.UpdateBounds();
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

    public void BeginCurveTool()
    {
        var shape = _host.GetShapeWithSelection() ?? _host.CurrentShape;

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
                ShapeDocument.UpdateBounds();
            },
            cancel: () =>
            {
                Undo.Cancel();
            }
        ));
    }

    public void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = _host.CurrentShape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform));

        if (hit.SegmentIndex == ushort.MaxValue)
            return;

        Undo.Record(Document);

        var shape = _host.CurrentShape;
        shape.ClearSelection();
        shape.SplitSegmentAtPoint(hit.SegmentIndex, hit.SegmentPosition);

        var newAnchorIdx = (ushort)(hit.SegmentIndex + 1);
        if (newAnchorIdx < shape.AnchorCount)
            shape.SetAnchorSelected(newAnchorIdx, true);

        ShapeDocument.UpdateBounds();

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        BeginMoveTool();
    }

    #endregion

    #region Drawing

    public static void DrawSegments(Shape shape)
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Gizmos.SetColor(EditorStyle.Palette.PathSegment);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (!shape.IsSegmentSelected(anchorIndex))
                    DrawSegment(shape, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 1);
            }

            Gizmos.SetColor(EditorStyle.Palette.Selection);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (shape.IsSegmentSelected(anchorIndex))
                    DrawSegment(shape, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 2);
            }
        }
    }

    public static void DrawSegment(Shape shape, ushort segmentIndex, float width, ushort order = 0)
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

    public static void DrawAnchors(Shape shape, bool selectedOnly = false)
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
            Gizmos.SetColor(EditorStyle.Palette.Selection);
            Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize, order: 5);
        }
    }

    public static ushort FindPathForAnchor(Shape shape, ushort anchorIndex)
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
