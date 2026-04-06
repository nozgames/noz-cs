//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class EyeDropperMode : EditorMode<SpriteEditor>
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
