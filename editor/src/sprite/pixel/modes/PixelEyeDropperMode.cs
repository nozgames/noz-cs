//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PixelEyeDropperMode : EditorMode<PixelSpriteEditor>
{
    private Task<Color>? _readbackTask;

    public override void Update()
    {
        EditorCursor.SetDropper();

        if (_readbackTask != null)
        {
            if (!_readbackTask.IsCompleted)
                return;

            var color = _readbackTask.Result.ToColor32();
            _readbackTask = null;
            if (color.A > 0)
                Editor.BrushColor = color;
            Editor.SetMode(new PencilMode());
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
            Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            Editor.SetMode(new PencilMode());
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            _readbackTask = Workspace.ReadPixelAtMouse();
            if (_readbackTask != null)
                Input.ConsumeButton(InputCode.MouseLeft);
        }
    }

}
