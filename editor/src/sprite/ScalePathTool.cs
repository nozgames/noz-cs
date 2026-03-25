//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

// Handle-based scale tool: the mouse tracks the handle position.
// Scale is computed by projecting mouse onto local axes relative to pivot.
public class HandleScalePathTransformTool : Tool
{
    private PathTransformToolState _state;
    private readonly Vector2 _pivotDoc; // pivot in document-local space
    private readonly float _selectionRotation; // for axis-aligned scaling
    private readonly bool _constrainX; // edge handle: only scale X
    private readonly bool _constrainY; // edge handle: only scale Y
    private readonly Matrix3x2 _invDocTransform;
    private Vector2 _mouseStartDoc; // actual mouse position at drag start

    private HandleScalePathTransformTool(
        PathTransformToolState state,
        Vector2 pivotDoc,
        float selectionRotation,
        bool constrainX, bool constrainY,
        Matrix3x2 invDocTransform)
    {
        _state = state;
        _pivotDoc = pivotDoc;
        _selectionRotation = selectionRotation;
        _constrainX = constrainX;
        _constrainY = constrainY;
        _invDocTransform = invDocTransform;
    }

    public static HandleScalePathTransformTool? Create(
        SpriteDocument document, List<SpritePath> selectedPaths,
        Vector2 pivotDoc,
        float selectionRotation, bool constrainX, bool constrainY)
    {
        var state = PathTransformToolState.Create(document, selectedPaths);
        if (state == null) return null;

        Matrix3x2.Invert(document.Transform, out var invDoc);

        return new HandleScalePathTransformTool(
            state.Value, pivotDoc,
            selectionRotation, constrainX, constrainY, invDoc);
    }

    public override void Begin()
    {
        base.Begin();
        _mouseStartDoc = Vector2.Transform(Workspace.MouseWorldPosition, _invDocTransform);
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

        ApplyScale();
    }

    private void ApplyScale()
    {
        var mouseDoc = Vector2.Transform(Workspace.MouseWorldPosition, _invDocTransform);

        // Project mouse and start position into selection-axis-aligned space
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

        // For corner handles, use uniform scale if Shift held
        if (!_constrainX && !_constrainY && Input.IsShiftDown(InputScope.All))
        {
            var uniform = (scaleX + scaleY) * 0.5f;
            scaleX = uniform;
            scaleY = uniform;
        }

        var scale = new Vector2(scaleX, scaleY);

        // Apply the scale relative to the pivot in document space
        var rot = Matrix3x2.CreateRotation(_selectionRotation);

        foreach (var (path, savedTranslation, savedRotation, savedScale) in _state.Snapshots)
        {
            // Compute path's world center from saved state
            var center = path.LocalBounds.Center;
            var savedXform = Matrix3x2.CreateTranslation(-center)
                * Matrix3x2.CreateScale(savedScale)
                * Matrix3x2.CreateRotation(savedRotation)
                * Matrix3x2.CreateTranslation(center)
                * Matrix3x2.CreateTranslation(savedTranslation);
            var savedWorldCenter = Vector2.Transform(center, savedXform);

            // Scale the center's offset from pivot in selection-aligned space
            var centerSelSpace = Vector2.Transform(savedWorldCenter, invRot);
            var pivotSelSpace = Vector2.Transform(_pivotDoc, invRot);
            var scaledCenterSel = pivotSelSpace + (centerSelSpace - pivotSelSpace) * scale;
            var scaledCenterDoc = Vector2.Transform(scaledCenterSel, rot);

            // Apply scale to path
            path.PathScale = savedScale * scale;

            // Compute where the center would be with new scale (but old translation)
            var newXform = Matrix3x2.CreateTranslation(-center)
                * Matrix3x2.CreateScale(savedScale * scale)
                * Matrix3x2.CreateRotation(savedRotation)
                * Matrix3x2.CreateTranslation(center)
                * Matrix3x2.CreateTranslation(savedTranslation);
            var newWorldCenter = Vector2.Transform(center, newXform);

            // Adjust translation to put center at scaled position
            path.PathTranslation = savedTranslation + (scaledCenterDoc - newWorldCenter);
        }
        _state.UpdatePaths();
    }

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
