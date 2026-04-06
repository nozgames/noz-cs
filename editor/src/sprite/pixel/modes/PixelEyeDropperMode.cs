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
    }

    public override void Draw()
    {
        var pixel = Editor.WorldToPixel(Workspace.MouseWorldPosition);
        if (!Editor.IsPixelInBounds(pixel)) return;

        var bounds = Editor.CanvasRect;
        var cellW = bounds.Width / Editor.Document.CanvasSize.X;
        var cellH = bounds.Height / Editor.Document.CanvasSize.Y;
        var pixelRect = new Rect(
            bounds.X + pixel.X * cellW,
            bounds.Y + pixel.Y * cellH,
            cellW, cellH);

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Editor.Document.Transform);
            Graphics.SetColor(new Color(1f, 1f, 1f, 0.6f));
            Gizmos.DrawRect(pixelRect, EditorStyle.Workspace.DocumentBoundsLineWidth);
        }
    }
}
