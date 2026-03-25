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
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (CurrentMode == SpriteEditMode.V && _selectedPaths.Count > 0)
        {
            // V mode: check for handle hit, then move inside bbox, then box select
            var combinedBounds = ComputeCombinedBounds();
            if (combinedBounds.Contains(localMousePos))
            {
                // Drag inside combined bounds — move selected paths
                var tool = MovePathTransformTool.Create(Document, _selectedPaths);
                if (tool != null)
                {
                    tool.CommitOnRelease = true;
                    Undo.Record(Document);
                    Workspace.BeginTool(tool);
                    return;
                }
            }
        }
        else if (CurrentMode == SpriteEditMode.A && _selectedPaths.Count > 0)
        {
            // A mode: check for anchor hit — start move
            var hit = Document.RootLayer.HitTest(localMousePos);
            if (hit.HasValue && hit.Value.Path.IsSelected && hit.Value.Hit.AnchorIndex >= 0)
            {
                // If the anchor isn't selected, select it first
                if (!hit.Value.Path.Anchors[hit.Value.Hit.AnchorIndex].IsSelected)
                {
                    Document.RootLayer.ClearAnchorSelections();
                    hit.Value.Path.SetAnchorSelected(hit.Value.Hit.AnchorIndex, true);
                }

                var tool = MovePathTool.Create(Document);
                if (tool != null)
                {
                    tool.CommitOnRelease = true;
                    Undo.Record(Document);
                    Workspace.BeginTool(tool);
                    return;
                }
            }
        }

        // Fallback: box select
        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelect));
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
        Document.RootLayer.ClearAllSelections();
        RebuildSelectedPaths();
    }

    private void RebuildSelectedPaths()
    {
        _selectedPaths.Clear();
        Document.RootLayer.CollectSelectedPaths(_selectedPaths);
        HasPathSelection = _selectedPaths.Count > 0;
        OnSelectionChanged(HasPathSelection);
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
            {
                var parent = Document.RootLayer.FindParent(path);
                parent?.Children.Remove(path);
            }
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
            path.DeleteSelectedAnchors();

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
            var parent = Document.RootLayer.FindParent(path);
            if (parent == null) continue;

            var clone = path.ClonePath();
            clone.ClearAnchorSelection();
            path.DeselectPath();
            clone.SelectPath();

            parent.Children.Add(clone);
            clone.UpdateSamples();
            clone.UpdateBounds();
        }

        Document.IncrementVersion();
        Document.UpdateBounds();
        RebuildSelectedPaths();
    }

    private void CopySelected()
    {
        var path = GetFirstSelectedPath() ?? GetPathWithSelection();
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
        Document.RootLayer.ClearAllSelections();

        var newPath = clipboardData.PasteAsPath();
        ActiveLayer.Children.Add(newPath);

        Document.IncrementVersion();
        Document.UpdateBounds();
        RebuildSelectedPaths();
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

    private void BeginMoveTool()
    {
        var tool = MovePathTool.Create(Document);
        if (tool == null) return;
        Undo.Record(Document);
        Workspace.BeginTool(tool);
    }

    private void BeginRotateTool()
    {
        var tool = RotatePathTool.Create(Document);
        if (tool == null) return;
        Undo.Record(Document);
        Workspace.BeginTool(tool);
    }

    private void BeginScaleTool()
    {
        var tool = ScalePathTool.Create(Document);
        if (tool == null) return;
        Undo.Record(Document);
        Workspace.BeginTool(tool);
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

        Undo.Record(Document);
        Workspace.BeginTool(new CurveTool(path, Document.Transform, path.SnapshotAnchors()));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        var hit = Document.RootLayer.HitTest(mouseLocal);
        if (!hit.HasValue || hit.Value.Hit.SegmentIndex < 0) return;

        Undo.Record(Document);

        var path = hit.Value.Path;
        path.ClearAnchorSelection();
        path.SplitSegmentAtPoint(hit.Value.Hit.SegmentIndex, hit.Value.Hit.SegmentPosition);

        var newIdx = hit.Value.Hit.SegmentIndex + 1;
        if (newIdx < path.Anchors.Count)
            path.SetAnchorSelected(newIdx, true);

        path.UpdateSamples();
        path.UpdateBounds();
        Document.UpdateBounds();

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
