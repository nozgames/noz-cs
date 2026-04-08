//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PencilMode : EditorMode<PixelSpriteEditor>
{
    private Vector2Int _lastPixel = new(-1, -1);
    private bool _isDrawing;

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

        // Bresenham line from _lastPixel to pixel
        DrawLine(_lastPixel, pixel);
        _lastPixel = pixel;
    }

    private void EndStroke()
    {
        _isDrawing = false;
        _lastPixel = new Vector2Int(-1, -1);
        Editor.InvalidateActiveLayerPreview();
    }

    private void PaintPixel(Vector2Int pixel)
    {
        if (!Editor.IsPixelInBounds(pixel)) return;
        Editor.PaintBrush(pixel, Editor.BrushColor);
    }

    private void DrawLine(Vector2Int from, Vector2Int to)
    {
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        var sx = from.X < to.X ? 1 : -1;
        var sy = from.Y < to.Y ? 1 : -1;
        var err = dx - dy;

        var x = from.X;
        var y = from.Y;

        while (true)
        {
            PaintPixel(new Vector2Int(x, y));

            if (x == to.X && y == to.Y) break;

            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    public override void Draw()
    {
        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixel(mouseWorld);
        if (!Editor.IsPixelInBounds(pixel)) return;
        if (!UI.IsHovered(Workspace.SceneWidgetId)) return;
        Editor.DrawBrushOutline(pixel, new Color(1f, 1f, 1f, 0.6f));
    }
}
