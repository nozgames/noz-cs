//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PixelEyeDropperMode(EditorMode previousMode) : EditorMode<PixelEditor>
{
    private readonly EditorMode _previousMode = previousMode;

    public override void Update()
    {
        if (!Application.IsTablet && !Input.IsButtonDown(InputCode.KeyLeftAlt, InputScope.All))
        {
            Finish();
            return;
        }            

        EditorCursor.SetDropper();

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
            Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            Editor.SetMode(_previousMode);
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All) ||
            Input.WasButtonPressed(InputCode.Pen, InputScope.All))
        {
            Input.ConsumeButton(InputCode.MouseLeft);
            Input.ConsumeButton(InputCode.Pen);
            Finish(Workspace.ReadPixelAtMouse());
            return;
        }
    }

    public void Finish(Color32? color = null)
    {
        if (color.HasValue && color.Value.A > 0)
            Editor.Document.BrushColor = color.Value;            

        Editor.SetMode(_previousMode);
    }
}
