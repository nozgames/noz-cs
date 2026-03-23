//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class MoveTool : Tool
{
    private readonly Action<Vector2>? _updateCallback;
    private readonly Action<Vector2>? _commitCallback;
    private readonly Action? _cancelCallback;

    private Vector2 _startWorld;
    private Vector2 _deltaScale = Vector2.One;

    public MoveTool() { }

    public MoveTool(Action<Vector2> update, Action<Vector2> commit, Action cancel)
    {
        _updateCallback = update;
        _commitCallback = commit;
        _cancelCallback = cancel;
    }

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
            OnCommit(delta);
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
        OnUpdate(updateDelta);
    }

    protected virtual void OnUpdate(Vector2 delta) => _updateCallback?.Invoke(delta);
    protected virtual void OnCommit(Vector2 delta) => _commitCallback?.Invoke(delta);
    protected virtual void OnCancel() => _cancelCallback?.Invoke();

    public override void Cancel()
    {
        OnCancel();
        base.Cancel();
    }
}
