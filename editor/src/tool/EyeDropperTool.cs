//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class EyeDropperTool(SpriteEditor editor) : Tool
{
    private readonly SpriteEditor _editor = editor;
    private Task<Color>? _readbackTask;
    private bool _shift;

    public override void Update()
    {
        EditorCursor.SetDropper();

        // Check for pending framebuffer readback result
        if (_readbackTask != null)
        {
            if (!_readbackTask.IsCompleted)
                return;

            var color = _readbackTask.Result;
            _readbackTask = null;
            _editor.ApplyEyeDropperColor(color.ToColor32(), _shift);
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) ||
            Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, Scope))
        {
            _shift = Input.IsShiftDown(InputScope.All);
            InitiateReadback();
        }
    }

    private void InitiateReadback()
    {
        if (!UI.TryGetSceneRenderInfo(Workspace.SceneWidgetId, out var info) || info.Handle == 0)
            return;

        var mouseScreen = Workspace.MousePosition;
        var relX = (mouseScreen.X - info.ScreenRect.X) / info.ScreenRect.Width;
        var relY = (mouseScreen.Y - info.ScreenRect.Y) / info.ScreenRect.Height;
        var px = Math.Clamp((int)(relX * info.Width), 0, info.Width - 1);
        var py = Math.Clamp((int)(relY * info.Height), 0, info.Height - 1);

        _readbackTask = Graphics.Driver.ReadPixelAsync(info.Handle, px, py);
        Input.ConsumeButton(InputCode.MouseLeft);
    }
}
