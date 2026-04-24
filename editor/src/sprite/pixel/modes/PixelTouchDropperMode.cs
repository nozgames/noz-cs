//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PixelTouchDropperMode : EditorMode<PixelEditor>
{
    private readonly EditorMode? _restoreMode;

    public PixelTouchDropperMode(EditorMode? restoreMode)
    {
        _restoreMode = restoreMode;
    }

    public override void Update()
    {
        EditorCursor.SetDropper();

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Editor.SetMode(_restoreMode ?? new PencilMode());
            return;
        }

        var sampled = Workspace.ReadPixelAtMouse();
        if (sampled.A > 0)
            Editor.Document.BrushColor = sampled;

        if (Touch.FingerCount == 0)
            Editor.SetMode(_restoreMode ?? new PencilMode());
    }
}
