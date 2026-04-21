//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum SpriteEditMode
{
    Transform,
    Anchor,
    Bevel,
    Pen,
    Rectangle,
    Circle,
    EyeDropper,
}

public partial class VectorSpriteEditor
{
    public bool HasPathSelection { get; private set; }
    public bool HasLayerSelection { get; private set; }
    public SpriteEditMode CurrentMode { get; private set; } = SpriteEditMode.Transform;
    internal IReadOnlyList<SpritePath> SelectedPaths => _selectedPaths;

    internal readonly List<SpritePath> _selectedPaths = new();
    private readonly List<SpriteGroup> _selectedLayers = new();
    internal SpriteNode? _pivotNode;
    internal float SelectionRotation => _selectionRotation;
    private float _selectionRotation;
    private Rect _selectionLocalBounds;
    private Vector2 _selectionCenter;
    private SpritePath? _hoverPath;
    private int _hoverAnchorIndex = -1;
    private SpritePathHandle _hoverHandle;

    private SpritePath? GetPathWithSelection()
    {
        return Document.Root.GetPathWithSelection();
    }

    private SpritePath? GetFirstSelectedPath()
    {
        return _selectedPaths.Count > 0 ? _selectedPaths[0] : null;
    }

    internal void SetMode(SpriteEditMode mode)
    {
        // Also re-instantiate if currently in EdgeEditMode: CurrentMode still
        // holds the pre-edge value, so the enum check alone would short-circuit
        // the intended restore back to a real tool mode.
        if (CurrentMode == mode && Mode is not EdgeEditMode) return;

        EditorMode newMode = mode switch
        {
            SpriteEditMode.Transform => new TransformMode(),
            SpriteEditMode.Anchor => new AnchorMode(),
            SpriteEditMode.Bevel => new BevelMode(),
            SpriteEditMode.Pen => new PenMode(),
            SpriteEditMode.Rectangle => new ShapeMode(ShapeType.Rectangle),
            SpriteEditMode.Circle => new ShapeMode(ShapeType.Circle),
            SpriteEditMode.EyeDropper => new EyeDropperMode(CurrentMode),
            _ => new TransformMode(),
        };
        CurrentMode = mode;
        SetMode(newMode);
    }

    #region Commands

    private Command[] GetShapeCommands()
    {
        return
        [
            new Command("Delete", DeleteSelected, [InputCode.KeyX, InputCode.KeyDelete], icon:EditorAssets.Sprites.IconDelete),
            new Command("V Mode", () => SetMode(SpriteEditMode.Transform), [InputCode.KeyV]),
            new Command("A Mode", () => SetMode(SpriteEditMode.Anchor), [InputCode.KeyA]),
            new Command("Bevel", () => SetMode(SpriteEditMode.Bevel), [InputCode.KeyB]),
            new Command("Select All", SelectAll, [new KeyBinding(InputCode.KeyA, ctrl: true)]),
            new Command("Pen Tool", () => SetMode(SpriteEditMode.Pen), [InputCode.KeyP]),
            new Command("Rectangle Tool", () => SetMode(SpriteEditMode.Rectangle), [new KeyBinding(InputCode.KeyR, ctrl: true)]),
            new Command("Circle Tool", () => SetMode(SpriteEditMode.Circle), [new KeyBinding(InputCode.KeyO, ctrl: true)]),
            new Command("Duplicate", DuplicateSelected, [new KeyBinding(InputCode.KeyD, ctrl: true)]),
            new Command("Copy", CopySelected, [new KeyBinding(InputCode.KeyC, ctrl: true)]),
            new Command("Paste", PasteSelected, [new KeyBinding(InputCode.KeyV, ctrl: true)]),
            new Command("Cut", CutSelected, [new KeyBinding(InputCode.KeyX, ctrl:true)]),
            new Command("Rename", BeginRename, [InputCode.KeyF2]),
        ];
    }

    #endregion

    #region Input

    #endregion

    #region Selection

    private void SelectAll()
    {
        if (CurrentMode != SpriteEditMode.Transform && _selectedPaths.Count > 0)
        {
            foreach (var path in _selectedPaths)
                path.SelectAll();
        }
        else
        {
            ActiveRoot.ForEachEditablePath(p => p.SelectPath());
            RebuildSelectedPaths();
        }
    }

    internal void ClearSelection()
    {
        Document.Root.ClearSelection();
        Document.Root.ClearSelection();
        _pivotNode = null;
        RebuildSelectedPaths();
    }

    internal void RebuildSelectedPaths(bool expandAncestors = true)
    {
        _selectedPaths.Clear();
        _selectedLayers.Clear();
        Document.Root.CollectSelectedGroups(_selectedLayers);
        HasLayerSelection = _selectedLayers.Count > 0;

        if (HasLayerSelection)
        {
            // Layer selection: collect all editable paths from selected layers for bounds/transforms
            foreach (var layer in _selectedLayers)
                layer.ForEachEditablePath(p => _selectedPaths.Add(p));
        }
        else
        {
            Document.Root.CollectSelectedPaths(_selectedPaths);
        }

        HasPathSelection = _selectedPaths.Count > 0;

        if (_pivotNode != null && !_pivotNode.IsSelected)
            _pivotNode = null;

        UpdateSelectionBounds();
        OnSelectionChanged(HasPathSelection);

        if (expandAncestors)
        {
            foreach (var layer in _selectedLayers)
                layer.ExpandAncestors();
            foreach (var path in _selectedPaths)
                path.ExpandAncestors();
        }
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

    internal void CommitBoxSelect(Rect bounds)
    {
        var shift = Input.IsShiftDown(InputScope.All);

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);

        if (CurrentMode != SpriteEditMode.Transform && _selectedPaths.Count > 0)
        {
            // Anchor-based modes with selected paths: box select anchors within selected paths
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
                Document.Root.ClearSelection();
            ActiveRoot.SelectPathsInRect(localRect);
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
            var remaining = Document.Root.Children.Count - _selectedLayers.Count;
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

        for (var i = _selectedPaths.Count - 1; i >= 0; i--)
        {
            var path = _selectedPaths[i];
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
            Clipboard.Copy(new SpriteClipboardData(_selectedLayers));
            return;
        }

        if (_selectedPaths.Count == 0) return;

        var data = new PathClipboardData(_selectedPaths);
        if (data.Paths.Length > 0)
            Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var nodeData = Clipboard.Get<SpriteClipboardData>();
        if (nodeData != null)
        {
            Undo.Record(Document);
            Document.Root.ClearSelection();
            Document.Root.ClearSelection();

            var nodes = nodeData.PasteAsNodes();
            for (var i = nodes.Count - 1; i >= 0; i--)
                Document.Root.Insert(0, nodes[i]);

            MarkDirty();
            RebuildSelectedPaths();
            return;
        }

        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null) return;

        Undo.Record(Document);
        Document.Root.ClearSelection();

        var newPaths = clipboardData.PasteAsPaths();
        for (var i = newPaths.Count - 1; i >= 0; i--)
            Document.Root.Insert(0, newPaths[i]);

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
