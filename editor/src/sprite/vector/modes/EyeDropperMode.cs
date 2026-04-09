//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class EyeDropperMode : EditorMode<VectorSpriteEditor>
{
    private readonly SpriteEditMode _previousMode;
    private bool _shift;

    public EyeDropperMode(SpriteEditMode previousMode)
    {
        // Defensive: never loop back into EyeDropper if something weird happens
        _previousMode = previousMode == SpriteEditMode.EyeDropper
            ? SpriteEditMode.Transform
            : previousMode;
    }

    public override void Update()
    {
        EditorCursor.SetDropper();

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
            Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            Editor.SetMode(_previousMode);
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            _shift = Input.IsShiftDown(InputScope.All);
            var color = Workspace.ReadPixelAtMouse();
            Input.ConsumeButton(InputCode.MouseLeft);
            Editor.ApplyEyeDropperColor(color, _shift);
            Editor.SetMode(_previousMode);
        }
    }
}
