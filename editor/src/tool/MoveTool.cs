//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class MoveTool(Action<Vector2> update, Action<Vector2> commit, Action cancel) : Tool
{
    private readonly Action<Vector2> _update = update;
    private readonly Action<Vector2> _commit = commit;
    private readonly Action _cancel = cancel;

    private Vector2 _startWorld;
    private Vector2 _deltaScale = Vector2.One;

    public override void Begin()
    {
        base.Begin();
        _startWorld = Workspace.MouseWorldPosition;
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
            var delta = Workspace.MouseWorldPosition - _startWorld;
            delta *= _deltaScale;
            _commit(delta);
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyX, Scope))
            _deltaScale = _deltaScale.X > 0 ? new Vector2(1, 0) : Vector2.One;
        if (Input.WasButtonPressed(InputCode.KeyY, Scope))
            _deltaScale = _deltaScale.Y > 0 ? new Vector2(0, 1) : Vector2.One;

        var updateDelta = Workspace.MouseWorldPosition - _startWorld;
        updateDelta *= _deltaScale;
        _update(updateDelta);
    }

    public override void Cancel()
    {
        _cancel();
        base.Cancel();
    }
}
