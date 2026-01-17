//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class RotateTool : Tool
{
    private readonly Vector2 _pivotWorld;
    private readonly Vector2 _pivotLocal;
    private readonly Matrix3x2 _invTransform;
    private readonly Action<float> _update;
    private readonly Action<float> _commit;
    private readonly Action _cancel;

    private float _startAngle;

    public RotateTool(Vector2 pivotWorld, Vector2 pivotLocal, Matrix3x2 invTransform, Action<float> update, Action<float> commit, Action cancel)
    {
        _pivotWorld = pivotWorld;
        _pivotLocal = pivotLocal;
        _invTransform = invTransform;
        _update = update;
        _commit = commit;
        _cancel = cancel;
    }

    public override void Begin()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        _startAngle = GetAngleFromPivot(mouseLocal);
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.KeyEnter))
        {
            var angle = GetCurrentAngle();
            _commit(angle);
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        var currentAngle = GetCurrentAngle();
        _update(currentAngle);
    }

    private float GetAngleFromPivot(Vector2 localPos)
    {
        var dir = localPos - _pivotLocal;
        return MathF.Atan2(dir.Y, dir.X);
    }

    private float GetCurrentAngle()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var currentAngle = GetAngleFromPivot(mouseLocal);
        var angle = currentAngle - _startAngle;

        if (Input.IsShiftDown())
        {
            var snap = MathF.PI / 12f; // 15 degrees
            angle = MathF.Round(angle / snap) * snap;
        }

        return angle;
    }

    public override void Draw()
    {
        Render.PushState();
        Render.SetLayer(EditorLayer.Tool);

        // Draw pivot point
        Render.SetColor(EditorStyle.SelectionColor);
        Gizmos.DrawRect(_pivotWorld, EditorStyle.Shape.AnchorSize * 1.5f, order: 10);

        // Draw line from pivot to mouse
        Render.SetColor(EditorStyle.SelectionColor.WithAlpha(0.7f));
        Gizmos.DrawLine(_pivotWorld, Workspace.MouseWorldPosition, EditorStyle.Shape.SegmentWidth * 2, order: 9);

        Render.PopState();
    }

    public override void Cancel()
    {
        _cancel();
    }
}
