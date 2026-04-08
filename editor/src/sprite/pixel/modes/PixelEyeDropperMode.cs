//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PixelEyeDropperMode : EditorMode<PixelSpriteEditor>
{
    public override void Update()
    {
        EditorCursor.SetDropper();

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
            Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            Editor.SetMode(new PencilMode());
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            var color = Workspace.ReadPixelAtMouse();
            Input.ConsumeButton(InputCode.MouseLeft);
            if (color.A > 0)
                Editor.BrushColor = color;
            Editor.SetMode(new PencilMode());
        }
    }

}
