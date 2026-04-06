//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal class KeyframeMode : EditorMode<AnimationEditor>
{
    private enum DragState { None, Move, Rotate, BoxSelect }

    private DragState _dragState;
    private bool _clearSelectionOnUp;
    private bool _ignoreUp;

    // Move state
    private Vector2 _moveStartWorld;

    // Rotate state
    private Vector2 _rotatePivotWorld;
    private Vector2 _rotatePivotLocal;
    private float _rotateStartAngle;
    private Matrix3x2 _rotateInvTransform;

    public override void Update()
    {
        switch (_dragState)
        {
            case DragState.Move:
                if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                    Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
                {
                    CancelDrag();
                    return;
                }
                if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
                {
                    CommitDrag();
                    return;
                }
                Editor.ApplyMoveDelta(Workspace.MouseWorldPosition - _moveStartWorld);
                return;

            case DragState.Rotate:
                if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                    Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
                {
                    CancelDrag();
                    return;
                }
                if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
                {
                    CommitDrag();
                    return;
                }
                Editor.ApplyRotateDelta(GetCurrentAngle());
                return;

            case DragState.BoxSelect:
                if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All) || !Workspace.IsDragging)
                {
                    CommitBoxSelect();
                    return;
                }
                return;
        }

        // Idle
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            HandleDragStart();
            return;
        }

        if (!_ignoreUp && !Workspace.IsDragging && Input.WasButtonReleased(InputCode.MouseLeft))
        {
            _clearSelectionOnUp = false;
            if (Editor.TrySelectBone())
                return;
            _clearSelectionOnUp = true;
        }

        _ignoreUp &= !Input.WasButtonReleased(InputCode.MouseLeft);

        if (Input.WasButtonReleased(InputCode.MouseLeft) && _clearSelectionOnUp && !Input.IsShiftDown())
            Editor.ClearSelection();
    }

    private void HandleDragStart()
    {
        var hitBone = Editor.Document.HitTestBone(Editor.GetBaseTransform(), Workspace.DragWorldPosition);
        if (hitBone >= 0)
        {
            if (!Editor.IsBoneSelected(hitBone))
            {
                if (!Input.IsShiftDown())
                    Editor.ClearSelection();
                Editor.SetBoneSelected(hitBone, true);
            }

            Editor.SaveState();
            Undo.Record(Editor.Document);

            if (Input.IsAltDown(InputScope.All))
            {
                _moveStartWorld = Workspace.DragWorldPosition;
                _dragState = DragState.Move;
            }
            else
            {
                BeginRotate();
                _dragState = DragState.Rotate;
            }
            return;
        }

        _dragState = DragState.BoxSelect;
    }

    private void BeginRotate()
    {
        Editor.UpdateSelectionCenter();
        Matrix3x2.Invert(Editor.GetBaseTransform(), out _rotateInvTransform);

        _rotatePivotLocal = Editor.SelectionCenter;
        _rotatePivotWorld = Editor.SelectionCenterWorld;

        var startMouseLocal = Vector2.Transform(Workspace.DragWorldPosition, _rotateInvTransform);
        var dir = startMouseLocal - _rotatePivotLocal;
        _rotateStartAngle = MathF.Atan2(dir.Y, dir.X);
    }

    private float GetCurrentAngle()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _rotateInvTransform);
        var dir = mouseLocal - _rotatePivotLocal;
        var currentAngle = MathF.Atan2(dir.Y, dir.X);
        var angle = currentAngle - _rotateStartAngle;

        if (Input.IsCtrlDown(InputScope.All))
        {
            var snap = MathF.PI / 12f;
            angle = MathF.Round(angle / snap) * snap;
        }

        return angle * 180f / MathF.PI;
    }

    private void CommitDrag()
    {
        if (_dragState == DragState.Move)
            Editor.ApplyMoveDelta(Workspace.MouseWorldPosition - _moveStartWorld);
        else if (_dragState == DragState.Rotate)
            Editor.ApplyRotateDelta(GetCurrentAngle());

        Editor.Document.UpdateTransforms();
        Editor.Document.IncrementVersion();
        Input.ConsumeButton(InputCode.MouseLeft);
        _dragState = DragState.None;
    }

    private void CancelDrag()
    {
        Undo.Cancel();
        Editor.RevertToSavedState();
        _dragState = DragState.None;
    }

    private void CommitBoxSelect()
    {
        var p0 = Workspace.DragWorldPosition;
        var p1 = Workspace.MouseWorldPosition;
        var bounds = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));
        Input.ConsumeButton(InputCode.MouseLeft);
        Editor.HandleBoxSelect(bounds);
        _dragState = DragState.None;
    }

    public override void Draw()
    {
        switch (_dragState)
        {
            case DragState.BoxSelect:
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

            case DragState.Rotate:
            {
                using var _ = Gizmos.PushState(EditorLayer.Tool);
                Graphics.SetTransform(Matrix3x2.Identity);
                Graphics.SetColor(EditorStyle.Tool.PointColor);
                Gizmos.DrawCircle(_rotatePivotWorld, EditorStyle.Tool.PointSize, order: 2);
                Graphics.SetColor(EditorStyle.Tool.LineColor);
                Gizmos.DrawDashedLine(_rotatePivotWorld, Workspace.MouseWorldPosition, order: 1);
                break;
            }
        }
    }
}
