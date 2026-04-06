//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract class AnchorBasedMode : EditorMode<VectorSpriteEditor>
{
    private static readonly List<SpriteNode.AnchorHitResult> _anchorHitResults = [];

    protected bool IsDragging { get; private set; }
    private bool _isBoxSelecting;

    public override void Update()
    {
        if (_isBoxSelecting)
        {
            if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All) || !Workspace.IsDragging)
            {
                CommitBoxSelect();
                return;
            }
            return;
        }

        if (Editor._isGradientDragging)
        {
            if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
            {
                Editor.CancelGradientDrag();
                return;
            }
            if (Input.WasButtonReleasedRaw(InputCode.MouseLeft))
            {
                Editor.CommitGradientDrag();
                return;
            }
            Editor.UpdateGradientDrag();
            return;
        }

        if (IsDragging)
        {
            if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
            {
                OnDragCancel();
                IsDragging = false;
                return;
            }

            if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
            {
                OnDragCommit();
                IsDragging = false;
                return;
            }

            OnDragUpdate();
            return;
        }

        // Idle state
        UpdateIdle();
    }

    private void UpdateIdle()
    {
        Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shift = Input.IsShiftDown(InputScope.All);
        var alt = Input.IsAltDown(InputScope.All);

        // Handle drag start
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            var dragLocal = Vector2.Transform(Workspace.DragWorldPosition, invTransform);

            if (Editor.IsGradientOverlayVisible() && Editor.HandleGradientDrag(dragLocal))
                return;

            if (Editor.SelectedPaths.Count > 0)
            {
                // Alt+drag segment: insert anchor then drag
                if (alt && HandleAltDragInsert(dragLocal))
                    return;

                // Drag anchor
                var anchorHit = Editor.Document.Root.HitTestAnchor(dragLocal, onlySelected: true);
                if (anchorHit.HasValue)
                {
                    var hitPath = anchorHit.Value.Path;
                    var hitCi = anchorHit.Value.ContourIndex;
                    var hitAi = anchorHit.Value.AnchorIndex;

                    if (!hitPath.Contours[hitCi].Anchors[hitAi].IsSelected)
                    {
                        Editor.Document.Root.ClearSelection();
                        hitPath.SetAnchorSelected(hitCi, hitAi, true);
                    }

                    if (OnAnchorDragStart(hitPath, hitCi, hitAi))
                    {
                        IsDragging = true;
                        return;
                    }
                }

                // Drag segment
                var segHit = Editor.Document.Root.HitTestSegment(dragLocal, onlySelected: true);
                if (segHit.HasValue)
                {
                    if (OnSegmentDragStart(segHit.Value.Path, segHit.Value.ContourIndex, segHit.Value.SegmentIndex, segHit.Value.Position))
                    {
                        IsDragging = true;
                        return;
                    }
                }
            }

            // Empty drag: box select
            _isBoxSelecting = true;
            return;
        }

        // Handle click (release without drag)
        if (Input.WasButtonReleased(InputCode.MouseLeft) && !Workspace.WasDragging)
        {
            // Alt+click: insert anchor
            if (alt && Editor.SelectedPaths.Count > 0 && HandleAltClickInsert(mouseLocal))
                return;

            // Anchor click
            if (Editor.SelectedPaths.Count > 0 && HandleAnchorClick(mouseLocal, shift))
                return;

            // Path click fallback
            if (Editor.HandlePathClick(mouseLocal, shift))
                return;

            // Nothing hit — clear
            if (!shift)
                Editor.ClearSelection();
        }
    }

    private bool HandleAnchorClick(Vector2 localMousePos, bool shift)
    {
        _anchorHitResults.Clear();
        Editor.Document.Root.HitTestAnchor(localMousePos, _anchorHitResults, onlySelected: true);

        foreach (var h in _anchorHitResults)
        {
            if (h.Path.Contours[h.ContourIndex].Anchors[h.AnchorIndex].IsSelected && !shift)
            {
                if (CycleAnchorSelection(h))
                    return true;
            }

            if (!shift) Editor.Document.Root.ClearSelection();
            h.Path.SetAnchorSelected(h.ContourIndex, h.AnchorIndex, true);
            Editor.RebuildSelectedPaths();
            return true;
        }
        return false;
    }

    private bool CycleAnchorSelection(SpriteNode.AnchorHitResult currentHit)
    {
        var foundCurrent = false;
        foreach (var h in _anchorHitResults)
        {
            if (!h.Path.IsSelected) continue;

            if (!foundCurrent)
            {
                if (h.Path == currentHit.Path && h.AnchorIndex == currentHit.AnchorIndex)
                    foundCurrent = true;
                continue;
            }

            Editor.Document.Root.ClearSelection();
            h.Path.SetAnchorSelected(h.AnchorIndex, true);
            Editor.RebuildSelectedPaths();
            return true;
        }

        // Wrap around
        foreach (var h in _anchorHitResults)
        {
            if (!h.Path.IsSelected) continue;
            Editor.Document.Root.ClearSelection();
            h.Path.SetAnchorSelected(h.AnchorIndex, true);
            Editor.RebuildSelectedPaths();
            return true;
        }

        return false;
    }

    private bool HandleAltClickInsert(Vector2 localMousePos)
    {
        var hit = Editor.Document.Root.HitTestSegment(localMousePos, onlySelected: true);
        if (!hit.HasValue) return false;

        Undo.Record(Editor.Document);
        var path = hit.Value.Path;
        var ci = hit.Value.ContourIndex;
        path.ClearAnchorSelection();
        path.SplitSegmentAtPoint(ci, hit.Value.SegmentIndex, hit.Value.Position);

        var newIdx = hit.Value.SegmentIndex + 1;
        if (newIdx < path.Contours[ci].Anchors.Count)
            path.SetAnchorSelected(ci, newIdx, true);

        path.UpdateSamples();
        path.UpdateBounds();
        Editor.MarkDirty();
        return true;
    }

    private bool HandleAltDragInsert(Vector2 dragLocal)
    {
        var segHit = Editor.Document.Root.HitTestSegment(dragLocal, onlySelected: true);
        if (!segHit.HasValue) return false;

        Undo.Record(Editor.Document);
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
        Editor.MarkDirty();

        // Start dragging the newly inserted anchor
        return OnAnchorDragStart(path, ci, newIdx);
    }

    private void CommitBoxSelect()
    {
        var p0 = Workspace.DragWorldPosition;
        var p1 = Workspace.MouseWorldPosition;
        var bounds = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));
        Input.ConsumeButton(InputCode.MouseLeft);
        Editor.CommitBoxSelect(bounds);
        _isBoxSelecting = false;
    }

    public override void Draw()
    {
        if (_isBoxSelecting)
            DrawBoxSelect();
    }

    private void DrawBoxSelect()
    {
        var p0 = Workspace.DragWorldPosition;
        var p1 = Workspace.MouseWorldPosition;
        var rect = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));

        using var _ = Gizmos.PushState(EditorLayer.Tool);
        Graphics.SetColor(EditorStyle.BoxSelect.FillColor);
        Graphics.Draw(rect);
        Graphics.SetColor(EditorStyle.BoxSelect.LineColor);
        Gizmos.DrawRect(rect, EditorStyle.BoxSelect.LineWidth);
    }

    // Subclass hooks — return true if drag was started
    protected abstract bool OnAnchorDragStart(SpritePath path, int contourIndex, int anchorIndex);
    protected virtual bool OnSegmentDragStart(SpritePath path, int contourIndex, int segmentIndex, Vector2 position) => false;

    // Called each frame while dragging
    protected virtual void OnDragUpdate() { }
    // Called when drag is committed (mouse released)
    protected virtual void OnDragCommit() { }
    // Called when drag is cancelled (escape/right-click)
    protected virtual void OnDragCancel() { }
}
