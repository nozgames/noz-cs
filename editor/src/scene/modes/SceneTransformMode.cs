//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class SceneTransformMode : EditorMode<SceneEditor>
{
    private enum DragType { None, BoxSelect, Move, Rotate, Scale }

    private struct NodeSnapshot
    {
        public SceneNode Node;
        public Vector2 Position;
        public float Rotation;
        public Vector2 Scale;
        public Matrix3x2 ParentWorldInverse;
    }

    private DragType _dragType;
    private NodeSnapshot[] _snapshots = [];
    private Vector2 _dragStartWorld;
    private Matrix3x2 _invDocTransform;

    // Move
    private Vector2 _selectionCentroid;

    // Rotate
    private Vector2 _rotatePivotWorld;
    private Vector2 _rotatePivotLocal;
    private float _rotateStartAngle;

    // Scale
    private Vector2 _pivotDoc;
    private float _scaleSelectionRotation;
    private SpritePathHandle _scaleHandle;
    private bool _scaleConstrainX;
    private bool _scaleConstrainY;
    private Vector2 _scaleMouseStartDoc;

    public override void OnEnter()
    {
        Editor.RebuildSelection();
    }

    public override void Update()
    {
        UpdateHoverHandle();

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

        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
            var dragLocal = Vector2.Transform(Workspace.DragWorldPosition, invTransform);

            if (Editor.SelectedNodes.Count > 0 && TryStartTransformDrag(dragLocal))
                return;

            _dragType = DragType.BoxSelect;
            return;
        }

        if (Input.WasButtonReleased(InputCode.MouseLeft) && !Workspace.WasDragging)
        {
            var shift = Input.IsShiftDown(InputScope.All);
            Editor.HandleClick(Workspace.MouseWorldPosition - Editor.Document.Position, shift);
        }
    }

    private void UpdateHoverHandle()
    {
        if (Editor.SelectedNodes.Count == 0)
        {
            Editor.SetHoverHandle(SpritePathHandle.None);
            EditorCursor.SetArrow();
            return;
        }

        Matrix3x2.Invert(Editor.Document.Transform, out var inv);
        var localMouse = Vector2.Transform(Workspace.MouseWorldPosition, inv);
        var hit = Editor.HitTestHandles(localMouse);
        Editor.SetHoverHandle(hit);
        SetCursor(hit);
    }

    private void SetCursor(SpritePathHandle hit)
    {
        if (hit == SpritePathHandle.None)
            EditorCursor.SetArrow();
        else if (hit == SpritePathHandle.Move)
            EditorCursor.SetMove();
        else if (IsRotateHandle(hit))
            EditorCursor.SetRotate(hit, Editor.SelectionRotation);
        else if (IsScaleHandle(hit))
            EditorCursor.SetScale(hit, Editor.SelectionRotation);
        else
            EditorCursor.SetArrow();
    }

    private bool TryStartTransformDrag(Vector2 localMouse)
    {
        var hit = Editor.HitTestHandles(localMouse);

        if (hit == SpritePathHandle.None) return false;

        Undo.Record(Editor.Document);
        _snapshots = SnapshotSelection();
        if (_snapshots.Length == 0) return false;

        Matrix3x2.Invert(Editor.Document.Transform, out _invDocTransform);
        _dragStartWorld = Workspace.DragWorldPosition;

        if (IsRotateHandle(hit))
        {
            // Pivot = center of selection in doc-local space
            var b = Editor.SelectionLocalBounds;
            var pivotSel = b.Center;
            var selToDoc = Matrix3x2.CreateRotation(Editor.SelectionRotation);
            _rotatePivotLocal = Vector2.Transform(pivotSel, selToDoc);
            _rotatePivotWorld = Vector2.Transform(_rotatePivotLocal, Editor.Document.Transform);

            var startMouseLocal = Vector2.Transform(_dragStartWorld, _invDocTransform);
            var dir = startMouseLocal - _rotatePivotLocal;
            _rotateStartAngle = MathF.Atan2(dir.Y, dir.X);

            _dragType = DragType.Rotate;
            return true;
        }

        if (IsScaleHandle(hit))
        {
            var selToDoc = Matrix3x2.CreateRotation(Editor.SelectionRotation);
            var pivotSel = Editor.GetOppositePivotInSelSpace(hit);
            _pivotDoc = Vector2.Transform(pivotSel, selToDoc);
            _scaleSelectionRotation = Editor.SelectionRotation;
            _scaleHandle = hit;
            _scaleConstrainX = hit is SpritePathHandle.ScaleTop or SpritePathHandle.ScaleBottom;
            _scaleConstrainY = hit is SpritePathHandle.ScaleLeft or SpritePathHandle.ScaleRight;
            _scaleMouseStartDoc = Vector2.Transform(_dragStartWorld, _invDocTransform);

            _dragType = DragType.Scale;
            return true;
        }

        if (hit == SpritePathHandle.Move)
        {
            _selectionCentroid = Editor.SelectionLocalBounds.Center;
            _dragType = DragType.Move;
            return true;
        }

        return false;
    }

    private NodeSnapshot[] SnapshotSelection()
    {
        var nodes = Editor.EffectiveSelection().ToList();
        var snapshots = new NodeSnapshot[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var parentWorld = ComputeParentWorld(n);
            Matrix3x2.Invert(parentWorld, out var inv);
            snapshots[i] = new NodeSnapshot
            {
                Node = n,
                Position = n.Position,
                Rotation = n.Rotation,
                Scale = n.Scale,
                ParentWorldInverse = inv,
            };
        }
        return snapshots;
    }

    private static Matrix3x2 ComputeParentWorld(SceneNode node)
    {
        var parent = node.Parent;
        if (parent == null) return Matrix3x2.Identity;
        return parent.WorldTransform;
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
        var deltaWorld = Workspace.MouseWorldPosition - _dragStartWorld;
        var deltaLocal = Vector2.TransformNormal(deltaWorld, _invDocTransform);

        // Compute snap correction in document-local space using the first snapshot as the leader
        var snap = Input.IsSnapModifierDown(InputScope.All);
        var snapCorrection = Vector2.Zero;
        if (snap && _snapshots.Length > 0)
        {
            var leader = _snapshots[0];
            Matrix3x2.Invert(leader.ParentWorldInverse, out var leaderParentWorld);
            var leaderParentDelta = Vector2.TransformNormal(deltaLocal, leader.ParentWorldInverse);
            var leaderNewLocal = leader.Position + leaderParentDelta;
            var leaderNewDoc = Vector2.Transform(leaderNewLocal, leaderParentWorld);
            var snapped = Grid.SnapToGrid(leaderNewDoc);
            snapCorrection = snapped - leaderNewDoc;
        }

        foreach (var s in _snapshots)
        {
            var parentDelta = Vector2.TransformNormal(deltaLocal, s.ParentWorldInverse);
            var newLocal = s.Position + parentDelta;
            if (snap && snapCorrection != Vector2.Zero)
            {
                var localCorrection = Vector2.TransformNormal(snapCorrection, s.ParentWorldInverse);
                newLocal += localCorrection;
            }
            s.Node.Position = newLocal;
        }
    }

    private void UpdateRotateDrag()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invDocTransform);
        var dir = mouseLocal - _rotatePivotLocal;
        var currentAngle = MathF.Atan2(dir.Y, dir.X);
        var angle = currentAngle - _rotateStartAngle;

        if (Input.IsSnapModifierDown(InputScope.All))
        {
            var snap = MathF.PI / 12f; // 15 degrees
            angle = MathF.Round(angle / snap) * snap;
        }

        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        foreach (var snap in _snapshots)
        {
            // Rotate the node's translation around the pivot in doc-local space, transformed to parent-local
            // pivot is in doc-local; node's world translation comes from parent.world * (pos in parent-local)
            // Easier: compute the node's saved world translation, rotate around pivot in doc-local, then convert back
            var parentWorldInv = snap.ParentWorldInverse;
            Matrix3x2.Invert(parentWorldInv, out var parentWorld);

            var savedWorldPos = Vector2.Transform(snap.Position, parentWorld);
            var off = savedWorldPos - _rotatePivotLocal;
            var rotated = _rotatePivotLocal + new Vector2(off.X * cos - off.Y * sin, off.X * sin + off.Y * cos);
            snap.Node.Position = Vector2.Transform(rotated, parentWorldInv);
            snap.Node.Rotation = snap.Rotation + angle;
        }
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

        if (Input.IsSnapModifierDown(InputScope.All))
        {
            if (!_scaleConstrainX) scaleX = MathF.Round(scaleX * 4f) / 4f;
            if (!_scaleConstrainY) scaleY = MathF.Round(scaleY * 4f) / 4f;
        }

        var scale = new Vector2(scaleX, scaleY);

        foreach (var snap in _snapshots)
        {
            var parentWorldInv = snap.ParentWorldInverse;
            Matrix3x2.Invert(parentWorldInv, out var parentWorld);

            var savedWorldPos = Vector2.Transform(snap.Position, parentWorld);
            var posSel = Vector2.Transform(savedWorldPos, invRot);
            var scaledPosSel = pivotSel + (posSel - pivotSel) * scale;
            var scaledPosDoc = Vector2.Transform(scaledPosSel, rot);
            snap.Node.Position = Vector2.Transform(scaledPosDoc, parentWorldInv);
            snap.Node.Scale = snap.Scale * scale;
        }

        EditorCursor.SetScale(_scaleHandle, _scaleSelectionRotation);
    }

    private void CommitTransformDrag()
    {
        UpdateTransformDrag();
        Input.ConsumeButton(InputCode.MouseLeft);
        _dragType = DragType.None;
        _snapshots = [];
    }

    private void CancelTransformDrag()
    {
        foreach (var snap in _snapshots)
        {
            snap.Node.Position = snap.Position;
            snap.Node.Rotation = snap.Rotation;
            snap.Node.Scale = snap.Scale;
        }
        Undo.Cancel();
        _dragType = DragType.None;
        _snapshots = [];
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

    private static bool IsScaleHandle(SpritePathHandle hit) => hit >= SpritePathHandle.ScaleTopLeft && hit <= SpritePathHandle.ScaleLeft;
    private static bool IsRotateHandle(SpritePathHandle hit) => hit >= SpritePathHandle.RotateTopLeft && hit <= SpritePathHandle.RotateBottomLeft;
}
