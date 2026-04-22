//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract class PixelStrokeMode : EditorMode<PixelSpriteEditor>
{
    private Vector2Int _lastPixel = new(-1, -1);
    private bool _isDrawing;

    protected abstract void PaintPixel(Vector2Int pixel);
    protected virtual Color OutlineColor => new(1f, 1f, 1f, 0.6f);
    protected virtual EditorMode? EyeDropperExitMode => null;

    private bool UpdateEyeDropper()
    {
        if (!Input.IsAltDown(InputScope.All) || _isDrawing)
            return false;

        EditorCursor.SetDropper();
        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            var color = Workspace.ReadPixelAtMouse();
            Input.ConsumeButton(InputCode.MouseLeft);
            if (color.A > 0)
                Editor.BrushColor = color;
            if (EyeDropperExitMode is { } exit)
                Editor.SetMode(exit);
        }

        return true;
    }

    public override void Update()
    {
        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixel(mouseWorld);

        if (UpdateEyeDropper())
            return;

        EditorCursor.SetCrosshair();

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            BeginStroke(pixel);
        }
        else if (_isDrawing)
        {
            if (Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
                ContinueStroke(pixel);
            else
                EndStroke();
        }
    }

    private void BeginStroke(Vector2Int pixel)
    {
        var layer = Editor.ActiveLayer;
        if (layer == null || layer.Pixels == null || layer.Locked || !layer.Visible) return;

        Undo.Record(Editor.Document);
        _isDrawing = true;
        _lastPixel = pixel;
        PaintPixel(pixel);
    }

    private void ContinueStroke(Vector2Int pixel)
    {
        if (pixel == _lastPixel) return;
        DrawLine(_lastPixel, pixel);
        _lastPixel = pixel;
    }

    private void EndStroke()
    {
        _isDrawing = false;
        _lastPixel = new Vector2Int(-1, -1);
        Editor.InvalidateActiveLayerPreview();
    }

    private void DrawLine(Vector2Int from, Vector2Int to)
    {
        // Step-then-paint: `from` was already painted by BeginStroke or the previous
        // ContinueStroke, so re-painting it here double-stamps the origin and accumulates
        // extra alpha around it. Advance one Bresenham step first, then paint.
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        var sx = from.X < to.X ? 1 : -1;
        var sy = from.Y < to.Y ? 1 : -1;
        var err = dx - dy;

        var x = from.X;
        var y = from.Y;

        while (x != to.X || y != to.Y)
        {
            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            PaintPixel(new Vector2Int(x, y));
        }
    }

    public override void Draw()
    {
        if (!UI.IsHovered(Workspace.SceneWidgetId)) return;
        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixel(mouseWorld);
        Editor.DrawBrushOutline(pixel, OutlineColor);
    }
}
