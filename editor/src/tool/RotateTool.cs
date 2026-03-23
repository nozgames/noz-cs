//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class RotateTool : Tool
{
    private readonly Action<float>? _updateCallback;
    private readonly Action<float>? _commitCallback;
    private readonly Action? _cancelCallback;

    private readonly Vector2 _pivotWorld;
    private readonly Vector2 _pivotLocal;
    private readonly Vector2 _originWorld;
    private readonly Vector2 _originLocal;
    private readonly Matrix3x2 _invTransform;

    private Vector2 _startMouseLocal;

    public RotateTool(
        in Vector2 pivotWorld, in Vector2 pivotLocal,
        in Vector2 originWorld, in Vector2 originLocal,
        in Matrix3x2 invTransform)
    {
        _pivotWorld = pivotWorld;
        _pivotLocal = pivotLocal;
        _originWorld = originWorld;
        _originLocal = originLocal;
        _invTransform = invTransform;
    }

    public RotateTool(
        in Vector2 pivotWorld, in Vector2 pivotLocal,
        in Vector2 originWorld, in Vector2 originLocal,
        in Matrix3x2 invTransform,
        Action<float> update, Action<float> commit, Action cancel)
        : this(pivotWorld, pivotLocal, originWorld, originLocal, invTransform)
    {
        _updateCallback = update;
        _commitCallback = commit;
        _cancelCallback = cancel;
    }

    public override void Begin()
    {
        base.Begin();
        _startMouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) ||
            Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, Scope) ||
            Input.WasButtonPressed(InputCode.KeyEnter, Scope))
        {
            OnCommit(GetCurrentAngle());
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        OnUpdate(GetCurrentAngle());
    }

    private static float GetAngleFromPivot(Vector2 localPos, Vector2 pivotLocal)
    {
        var dir = localPos - pivotLocal;
        return MathF.Atan2(dir.Y, dir.X);
    }

    protected Vector2 GetCurrentPivotLocal() => Input.IsShiftDown(Scope) ? _originLocal : _pivotLocal;
    protected Vector2 GetCurrentPivotWorld() => Input.IsShiftDown(Scope) ? _originWorld : _pivotWorld;

    protected float GetCurrentAngle()
    {
        var pivotLocal = GetCurrentPivotLocal();
        var startAngle = GetAngleFromPivot(_startMouseLocal, pivotLocal);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var currentAngle = GetAngleFromPivot(mouseLocal, pivotLocal);
        var angle = currentAngle - startAngle;

        if (Input.IsCtrlDown(Scope))
        {
            var snap = MathF.PI / 12f; // 15 degrees
            angle = MathF.Round(angle / snap) * snap;
        }

        return angle;
    }

    protected virtual void OnUpdate(float angle) => _updateCallback?.Invoke(angle);
    protected virtual void OnCommit(float angle) => _commitCallback?.Invoke(angle);
    protected virtual void OnCancel() => _cancelCallback?.Invoke();

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            var pivotWorld = GetCurrentPivotWorld();
            Graphics.SetTransform(Matrix3x2.Identity);
            Graphics.SetColor(EditorStyle.Tool.PointColor);
            Gizmos.DrawCircle(pivotWorld, EditorStyle.Tool.PointSize, order: 2);
            Graphics.SetColor(EditorStyle.Tool.LineColor);
            Gizmos.DrawDashedLine(pivotWorld, Workspace.MouseWorldPosition, order: 1);
        }
    }

    public override void Cancel()
    {
        base.Cancel();
        OnCancel();
    }
}
