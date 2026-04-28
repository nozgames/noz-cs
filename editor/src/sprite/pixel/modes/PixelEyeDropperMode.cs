//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public enum PixelEyeDropperTrigger { Click, AltHold, TouchHold }

public class PixelEyeDropperMode : EditorMode<PixelEditor>
{
    private readonly EditorMode _previousMode;
    private readonly PixelEyeDropperTrigger _trigger;

    public PixelEyeDropperMode(EditorMode previousMode, PixelEyeDropperTrigger trigger = PixelEyeDropperTrigger.Click)
    {
        _previousMode = previousMode;
        _trigger = trigger;

        // Click trigger is typically activated by a UI button press in the same frame.
        // Consume the press so this mode's own WasButtonPressed check doesn't immediately
        // pick a color at the toolbar's position and exit.
        if (trigger == PixelEyeDropperTrigger.Click)
        {
            Input.ConsumeButton(InputCode.MouseLeft);
            Input.ConsumeButton(InputCode.Pen);
        }
    }

    public override void Update()
    {
        if (_trigger == PixelEyeDropperTrigger.AltHold && !Input.IsAltDown(InputScope.All))
        {
            Finish();
            return;
        }

        EditorCursor.SetDropper();

        if (_trigger == PixelEyeDropperTrigger.TouchHold)
        {
            // Touch long-press fires before mouse simulation kicks in, so MouseWorldPosition
            // is stale. Sample at the actual finger position instead.
            var live = default(Color32);
            foreach (var f in Touch.Fingers)
            {
                if (!f.Active) continue;
                live = Workspace.ReadPixelAt(Workspace.Camera.ScreenToWorld(f.Position));
                break;
            }
            if (live.A > 0)
                Editor.Document.BrushColor = live.WithAlpha(Editor.Document.BrushColor.A);

            if (Touch.FingerCount == 0)
                Editor.SetMode(_previousMode);
            return;
        }

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
