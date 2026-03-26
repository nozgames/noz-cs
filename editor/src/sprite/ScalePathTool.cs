//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

// Handle-based scale tool: the mouse tracks the handle position.
// Scale is computed by projecting mouse onto local axes relative to pivot.
internal class HandleScalePathTransformTool : Tool
{
    private PathTransformToolState _state;
    private readonly Vector2 _pivotDoc; // pivot in document-local space
    private readonly float _selectionRotation; // for axis-aligned scaling
    private readonly SpritePathHandle _handle;
    private readonly bool _constrainX; // edge handle: only scale X
    private readonly bool _constrainY; // edge handle: only scale Y
    private readonly Matrix3x2 _invDocTransform;
    private Vector2 _mouseStartDoc; // actual mouse position at drag start

    private HandleScalePathTransformTool(
        PathTransformToolState state,
        Vector2 pivotDoc,
        float selectionRotation,
        SpritePathHandle handle,
        Matrix3x2 invDocTransform)
    {
        _state = state;
        _pivotDoc = pivotDoc;
        _selectionRotation = selectionRotation;
        _handle = handle;
        _constrainX = handle is SpritePathHandle.ScaleTop or SpritePathHandle.ScaleBottom;
        _constrainY = handle is SpritePathHandle.ScaleLeft or SpritePathHandle.ScaleRight;
        _invDocTransform = invDocTransform;
    }

    public static HandleScalePathTransformTool? Create(
        SpriteDocument document, List<SpritePath> selectedPaths,
        Vector2 pivotDoc,
        float selectionRotation, SpritePathHandle handle)
    {
        var state = PathTransformToolState.Create(document, selectedPaths);
        if (state == null) return null;

        Matrix3x2.Invert(document.Transform, out var invDoc);

        return new HandleScalePathTransformTool(
            state.Value, pivotDoc,
            selectionRotation, handle, invDoc);
    }

    public override void Begin()
    {
        base.Begin();
        _mouseStartDoc = Vector2.Transform(Workspace.MouseWorldPosition, _invDocTransform);
        UpdateCursor();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) ||
            Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonReleased(InputCode.MouseLeft, Scope))
        {
            ApplyScale();
            _state.Document.UpdateBounds();
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        UpdateCursor();
        ApplyScale();
    }

    private Vector2 ComputeScale()
    {
        var mouseDoc = Vector2.Transform(Workspace.MouseWorldPosition, _invDocTransform);

        var invRot = Matrix3x2.CreateRotation(-_selectionRotation);
        var pivotSel = Vector2.Transform(_pivotDoc, invRot);
        var startSel = Vector2.Transform(_mouseStartDoc, invRot);
        var mouseSel = Vector2.Transform(mouseDoc, invRot);

        var startDelta = startSel - pivotSel;
        var mouseDelta = mouseSel - pivotSel;

        var scaleX = MathF.Abs(startDelta.X) > 0.0001f ? mouseDelta.X / startDelta.X : 1f;
        var scaleY = MathF.Abs(startDelta.Y) > 0.0001f ? mouseDelta.Y / startDelta.Y : 1f;

        if (_constrainX) scaleX = 1f;
        if (_constrainY) scaleY = 1f;

        if (!_constrainX && !_constrainY && Input.IsShiftDown(InputScope.All))
        {
            var uniform = (scaleX + scaleY) * 0.5f;
            scaleX = uniform;
            scaleY = uniform;
        }

        return new Vector2(scaleX, scaleY);
    }

    private void ApplyScale()
    {
        var scale = ComputeScale();
        var invRot = Matrix3x2.CreateRotation(-_selectionRotation);
        var rot = Matrix3x2.CreateRotation(_selectionRotation);

        foreach (var (path, savedTranslation, savedRotation, savedScale) in _state.Snapshots)
        {
            var center = path.LocalBounds.Center;
            var savedWorldCenter = PathTransformToolState.ComputeWorldCenter(
                center, savedTranslation, savedRotation, savedScale);

            // Scale the center's offset from pivot in selection-aligned space
            var centerSelSpace = Vector2.Transform(savedWorldCenter, invRot);
            var pivotSelSpace = Vector2.Transform(_pivotDoc, invRot);
            var scaledCenterSel = pivotSelSpace + (centerSelSpace - pivotSelSpace) * scale;
            var scaledCenterDoc = Vector2.Transform(scaledCenterSel, rot);

            path.PathScale = savedScale * scale;

            var newWorldCenter = PathTransformToolState.ComputeWorldCenter(
                center, savedTranslation, savedRotation, savedScale * scale);

            path.PathTranslation = savedTranslation + (scaledCenterDoc - newWorldCenter);
        }
        _state.UpdatePaths();
    }

    private void UpdateCursor() => EditorCursor.SetScale(_handle, _selectionRotation);

    public override void Draw()
    {
        using var _ = Gizmos.PushState(EditorLayer.Tool);
        Graphics.SetTransform(Matrix3x2.Identity);
        var worldPivot = Vector2.Transform(_pivotDoc, _state.Document.Transform);
        Gizmos.SetColor(EditorStyle.Tool.PointColor);
        Gizmos.DrawCircle(worldPivot, EditorStyle.Tool.PointSize, order: 10);
    }

    public override void Cancel()
    {
        Undo.Cancel();
        base.Cancel();
    }
}
