//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class EyeDropperMode : EditorMode<VectorSpriteEditor>
{
    private Task<Color>? _readbackTask;
    private bool _shift;
    private SpriteEditMode _previousMode;

    public override void OnEnter()
    {
        _previousMode = Editor.CurrentMode;
    }

    public override void Update()
    {
        EditorCursor.SetDropper();

        if (_readbackTask != null)
        {
            if (!_readbackTask.IsCompleted)
                return;

            var color = _readbackTask.Result;
            _readbackTask = null;
            Editor.ApplyEyeDropperColor(color.ToColor32(), _shift);
            ReturnToPreviousMode();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
            Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            ReturnToPreviousMode();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            _shift = Input.IsShiftDown(InputScope.All);
            InitiateReadback();
        }
    }

    private void InitiateReadback()
    {
        _readbackTask = Workspace.ReadPixelAtMouse();
        if (_readbackTask != null)
            Input.ConsumeButton(InputCode.MouseLeft);
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
