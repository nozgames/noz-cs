//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class EyeDropperMode : EditorMode<VectorSpriteEditor>
{
    private bool _shift;
    private SpriteEditMode _previousMode;

    public override void OnEnter()
    {
        _previousMode = Editor.CurrentMode;
    }

    public override void Update()
    {
        EditorCursor.SetDropper();

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
            Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            ReturnToPreviousMode();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            _shift = Input.IsShiftDown(InputScope.All);
            var color = Workspace.ReadPixelAtMouse();
            Input.ConsumeButton(InputCode.MouseLeft);
            Editor.ApplyEyeDropperColor(color, _shift);
            ReturnToPreviousMode();
        }
    }

    private void ReturnToPreviousMode()
    {
        // Go back to whichever mode was active before eyedropper
        EditorMode mode = _previousMode switch
        {
            SpriteEditMode.Transform => new TransformMode(),
            SpriteEditMode.Anchor => new AnchorMode(),
            SpriteEditMode.Bevel => new BevelMode(),
            _ => new TransformMode(),
        };
        Editor.SetMode(mode);
    }
}
