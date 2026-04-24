//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PixelEyeDropperMode(EditorMode previousMode, bool altMode=false) : EditorMode<PixelEditor>
{
    private readonly EditorMode _previousMode = previousMode;
    private readonly bool _altMode = altMode;

    public override void Update()
    {
        if (_altMode && !Input.IsButtonDown(InputCode.KeyLeftAlt, InputScope.All))
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
            Editor.Document.BrushColor = color.Value.WithAlpha(Editor.Document.BrushColor.A);

        if (_previousMode is PixelEraserMode)
            Editor.SetBrushMode(Editor.Document.BrushType);
        else
            Editor.SetMode(_previousMode);
    }
}
