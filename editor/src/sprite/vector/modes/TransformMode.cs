//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class TransformMode : EditorMode<VectorSpriteEditor>
{
    private enum DragType { None, BoxSelect, Move, Rotate, Scale }

    private DragType _dragType;

    // Move/Rotate/Scale shared state
    private PathTransformToolState _transformState;
    private HashSet<SpritePath>? _movingPaths;
    private Vector2 _dragStartWorld;
    private Matrix3x2 _invDocTransform;

    // Move-specific
    private SnapType _snapType;
    private Vector2 _snapDocLocal;

    // Rotate-specific
    private Vector2 _rotatePivotWorld;
    private Vector2 _rotatePivotLocal;
    private float _rotateStartAngle;

    // Scale-specific
    private Vector2 _pivotDoc;
    private float _scaleSelectionRotation;
    private SpritePathHandle _scaleHandle;
    private bool _scaleConstrainX;
    private bool _scaleConstrainY;
    private Vector2 _scaleMouseStartDoc;

    public override void OnEnter()
    {
        Editor.Document.Root.ClearSelection();
    }

    public override void Update()
    {
        switch (_dragType)
        {
            case DragType.BoxSelect:
                if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All) || !Workspace.IsDragging)
                {
                    CommitBoxSelect();
                    _dragType = DragType.None;
                }
                return;

            case DragType.Move:
            case DragType.Rotate:
            case DragType.Scale:
                if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                    Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
                {
                    CancelTransformDrag();
                    return;
                }
                if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
                {
                    CommitTransformDrag();
                    return;
                }
                UpdateTransformDrag();
                return;
        }

        // Handle drag start
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
            var dragLocal = Vector2.Transform(Workspace.DragWorldPosition, invTransform);

            if (Editor.SelectedPaths.Count > 0 && TryStartTransformDrag(dragLocal))
                return;

            _dragType = DragType.BoxSelect;
            return;
        }

        // Handle click
        if (Input.WasButtonReleased(InputCode.MouseLeft) && !Workspace.WasDragging)
        {
            Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
            var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
            var shift = Input.IsShiftDown(InputScope.All);

            if (Editor.HandlePathClick(mouseLocal, shift))
                return;

            if (!shift)
                Editor.ClearSelection();
        }
    }

    private bool TryStartTransformDrag(Vector2 localMousePos)
    {
        var handleHit = Editor.HitTestHandles(localMousePos);

        if (VectorSpriteEditor.IsRotateHandle(handleHit))
        {
            var state = PathTransformToolState.Create(Editor.Document, Editor, Editor._selectedPaths);
            if (state == null) return false;

            Undo.Record(Editor.Document);
            _transformState = state.Value;
            _dragStartWorld = Workspace.DragWorldPosition;
            _rotatePivotLocal = _transformState.Centroid;
            _rotatePivotWorld = Vector2.Transform(_rotatePivotLocal, Editor.Document.Transform);
            Matrix3x2.Invert(Editor.Document.Transform, out _invDocTransform);

            var startMouseLocal = Vector2.Transform(_dragStartWorld, _invDocTransform);
            var dir = startMouseLocal - _rotatePivotLocal;
            _rotateStartAngle = MathF.Atan2(dir.Y, dir.X);

            _dragType = DragType.Rotate;
            return true;
        }

        if (VectorSpriteEditor.IsScaleHandle(handleHit))
        {
            var state = PathTransformToolState.Create(Editor.Document, Editor, Editor._selectedPaths);
            if (state == null) return false;

            Undo.Record(Editor.Document);
            _transformState = state.Value;

            var selToDoc = Matrix3x2.CreateRotation(Editor.SelectionRotation);
            var pivotSel = Editor.GetOppositePivotInSelSpace(handleHit);
            _pivotDoc = Vector2.Transform(pivotSel, selToDoc);
            _scaleSelectionRotation = Editor.SelectionRotation;
            _scaleHandle = handleHit;
            _scaleConstrainX = handleHit is SpritePathHandle.ScaleTop or SpritePathHandle.ScaleBottom;
            _scaleConstrainY = handleHit is SpritePathHandle.ScaleLeft or SpritePathHandle.ScaleRight;

            Matrix3x2.Invert(Editor.Document.Transform, out _invDocTransform);
            _scaleMouseStartDoc = Vector2.Transform(Workspace.DragWorldPosition, _invDocTransform);

            _dragType = DragType.Scale;
            return true;
        }

        if (handleHit == SpritePathHandle.Move)
        {
            var state = PathTransformToolState.Create(Editor.Document, Editor, Editor._selectedPaths);
            if (state == null) return false;

            Undo.Record(Editor.Document);
            _transformState = state.Value;
            _dragStartWorld = Workspace.DragWorldPosition;
            Matrix3x2.Invert(Editor.Document.Transform, out _invDocTransform);
            _movingPaths = new HashSet<SpritePath>(_transformState.Snapshots.Length);
            foreach (var (path, _, _, _) in _transformState.Snapshots)
                _movingPaths.Add(path);
            _snapType = SnapType.None;

            _dragType = DragType.Move;
            return true;
        }

        return false;
    }

    private void UpdateTransformDrag()
    {
        switch (_dragType)
        {
            case DragType.Move: UpdateMoveDrag(); break;
            case DragType.Rotate: UpdateRotateDrag(); break;
            case DragType.Scale: UpdateScaleDrag(); break;
        }
    }

    private void UpdateMoveDrag()
    {
        var delta = Workspace.MouseWorldPosition - _dragStartWorld;
        var localDelta = Vector2.TransformNormal(delta, _invDocTransform);
        _snapType = SnapType.None;

        if (Input.IsCtrlDown(InputScope.All))
        {
            var candidateDocLocal = _transformState.Centroid + localDelta;
            var snappedDocLocal = SnapHelper.Snap(
                candidateDocLocal, Editor.Document.Root, _movingPaths!, out _snapType);

            if (_snapType != SnapType.None)
            {
                _snapDocLocal = snappedDocLocal;
                localDelta = snappedDocLocal - _transformState.Centroid;
            }
        }

        foreach (var (path, savedTranslation, _, _) in _transformState.Snapshots)
            path.PathTranslation = savedTranslation + localDelta;
        _transformState.UpdatePaths();
    }

    private void UpdateRotateDrag()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invDocTransform);
        var dir = mouseLocal - _rotatePivotLocal;
        var currentAngle = MathF.Atan2(dir.Y, dir.X);
        var angle = currentAngle - _rotateStartAngle;

        if (Input.IsCtrlDown(InputScope.All))
        {
            var snap = MathF.PI / 12f; // 15 degrees
            angle = MathF.Round(angle / snap) * snap;
        }

        var pivot = _rotatePivotLocal;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        foreach (var (path, savedTranslation, savedRotation, savedScale) in _transformState.Snapshots)
        {
            path.PathRotation = savedRotation + angle;

            var center = path.LocalBounds.Center;
            var savedWorldCenter = PathTransformToolState.ComputeWorldCenter(center, savedTranslation, savedRotation, savedScale);
            var off = savedWorldCenter - pivot;
            var rotatedCenter = pivot + new Vector2(off.X * cos - off.Y * sin, off.X * sin + off.Y * cos);
            var newWorldCenter = PathTransformToolState.ComputeWorldCenter(center, savedTranslation, savedRotation + angle, savedScale);
            path.PathTranslation = savedTranslation + (rotatedCenter - newWorldCenter);
        }
        _transformState.UpdatePaths();
    }

    private void UpdateScaleDrag()
    {
        var mouseDoc = Vector2.Transform(Workspace.MouseWorldPosition, _invDocTransform);

        var invRot = Matrix3x2.CreateRotation(-_scaleSelectionRotation);
        var rot = Matrix3x2.CreateRotation(_scaleSelectionRotation);
        var pivotSel = Vector2.Transform(_pivotDoc, invRot);
        var startSel = Vector2.Transform(_scaleMouseStartDoc, invRot);
        var mouseSel = Vector2.Transform(mouseDoc, invRot);

        var startDelta = startSel - pivotSel;
        var mouseDelta = mouseSel - pivotSel;

        var scaleX = MathF.Abs(startDelta.X) > 0.0001f ? mouseDelta.X / startDelta.X : 1f;
        var scaleY = MathF.Abs(startDelta.Y) > 0.0001f ? mouseDelta.Y / startDelta.Y : 1f;

        if (_scaleConstrainX) scaleX = 1f;
        if (_scaleConstrainY) scaleY = 1f;

        if (!_scaleConstrainX && !_scaleConstrainY && Input.IsShiftDown(InputScope.All))
        {
            var uniform = (scaleX + scaleY) * 0.5f;
            scaleX = uniform;
            scaleY = uniform;
        }

        var scale = new Vector2(scaleX, scaleY);

        foreach (var (path, savedTranslation, savedRotation, savedScale) in _transformState.Snapshots)
        {
            var center = path.LocalBounds.Center;
            var savedWorldCenter = PathTransformToolState.ComputeWorldCenter(center, savedTranslation, savedRotation, savedScale);

            var centerSelSpace = Vector2.Transform(savedWorldCenter, invRot);
            var scaledCenterSel = pivotSel + (centerSelSpace - pivotSel) * scale;
            var scaledCenterDoc = Vector2.Transform(scaledCenterSel, rot);

            path.PathScale = savedScale * scale;
            var newWorldCenter = PathTransformToolState.ComputeWorldCenter(center, savedTranslation, savedRotation, savedScale * scale);
            path.PathTranslation = savedTranslation + (scaledCenterDoc - newWorldCenter);
        }
        _transformState.UpdatePaths();

        EditorCursor.SetScale(_scaleHandle, _scaleSelectionRotation);
    }

    private void CommitTransformDrag()
    {
        UpdateTransformDrag();
        Input.ConsumeButton(InputCode.MouseLeft);
        _dragType = DragType.None;
        _movingPaths = null;
    }

    private void CancelTransformDrag()
    {
        // Restore snapshots
        foreach (var (path, savedTranslation, savedRotation, savedScale) in _transformState.Snapshots)
        {
            path.PathTranslation = savedTranslation;
            path.PathRotation = savedRotation;
            path.PathScale = savedScale;
        }
        _transformState.UpdatePaths();
        Undo.Cancel();
        _dragType = DragType.None;
        _movingPaths = null;
    }

    private void CommitBoxSelect()
    {
        var p0 = Workspace.DragWorldPosition;
        var p1 = Workspace.MouseWorldPosition;
        var bounds = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));
        Input.ConsumeButton(InputCode.MouseLeft);
        Editor.CommitBoxSelect(bounds);
    }

    public override void Draw()
    {
        switch (_dragType)
        {
            case DragType.BoxSelect:
            {
                var p0 = Workspace.DragWorldPosition;
                var p1 = Workspace.MouseWorldPosition;
                var rect = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));
                using var _ = Gizmos.PushState(EditorLayer.Tool);
                Graphics.SetColor(EditorStyle.BoxSelect.FillColor);
                Graphics.Draw(rect);
                Graphics.SetColor(EditorStyle.BoxSelect.LineColor);
                Gizmos.DrawRect(rect, EditorStyle.BoxSelect.LineWidth);
                break;
            }

            case DragType.Move:
                SnapHelper.DrawSnapIndicator(_snapType, _snapDocLocal, Editor.Document.Transform);
                break;

            case DragType.Rotate:
            {
                using var _ = Gizmos.PushState(EditorLayer.Tool);
                Graphics.SetTransform(Matrix3x2.Identity);
                Graphics.SetColor(EditorStyle.Tool.PointColor);
                Gizmos.DrawCircle(_rotatePivotWorld, EditorStyle.Tool.PointSize, order: 2);
                Graphics.SetColor(EditorStyle.Tool.LineColor);
                Gizmos.DrawDashedLine(_rotatePivotWorld, Workspace.MouseWorldPosition, order: 1);
                break;
            }

            case DragType.Scale:
            {
                using var _ = Gizmos.PushState(EditorLayer.Tool);
                Graphics.SetTransform(Matrix3x2.Identity);
                var worldPivot = Vector2.Transform(_pivotDoc, Editor.Document.Transform);
                Gizmos.SetColor(EditorStyle.Tool.PointColor);
                Gizmos.DrawCircle(worldPivot, EditorStyle.Tool.PointSize, order: 10);
                break;
            }
        }
    }
}
