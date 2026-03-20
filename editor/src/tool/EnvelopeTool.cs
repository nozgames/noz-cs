//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class EnvelopeTool(
    Vector2 boneHead,
    Vector2 boneTail,
    Action<float> update,
    Action commit,
    Action cancel) : Tool
{
    private readonly Vector2 _boneHead = boneHead;
    private readonly Vector2 _boneTail = boneTail;
    private readonly Action<float> _update = update;
    private readonly Action _commit = commit;
    private readonly Action _cancel = cancel;

    public override void Begin()
    {
        base.Begin();
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
            _commit();
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        var radius = MathEx.DistanceFromLineSegment(_boneHead, _boneTail, Workspace.MouseWorldPosition);
        _update(radius);
    }

    public override void Draw()
    {
        using var _ = Gizmos.PushState(EditorLayer.Tool);
        Gizmos.SetColor(EditorStyle.Tool.LineColor);
        Gizmos.DrawDashedLine(_boneHead, Workspace.MouseWorldPosition);
    }

    public override void Cancel()
    {
        _cancel();
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}
