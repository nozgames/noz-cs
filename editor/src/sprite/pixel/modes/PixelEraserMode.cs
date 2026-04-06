//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelEraserMode : EditorMode<PixelSpriteEditor>
{
    private Vector2Int _lastPixel = new(-1, -1);
    private bool _isErasing;

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixel(mouseWorld);

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            BeginStroke(pixel);
        }
        else if (_isErasing)
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
        if (layer == null || layer.Pixels == null || layer.Locked) return;

        Undo.Record(Editor.Document);
        _isErasing = true;
        _lastPixel = pixel;
        ErasePixel(pixel);
    }

    private void ContinueStroke(Vector2Int pixel)
    {
        if (pixel == _lastPixel) return;
        DrawLine(_lastPixel, pixel);
        _lastPixel = pixel;
    }

    private void EndStroke()
    {
        _isErasing = false;
        _lastPixel = new Vector2Int(-1, -1);
    }

    private void ErasePixel(Vector2Int pixel)
    {
        if (!Editor.IsPixelInBounds(pixel)) return;
        Editor.PaintBrush(pixel, default);
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
            ErasePixel(new Vector2Int(x, y));
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
        Editor.DrawBrushOutline(pixel, new Color(1f, 0.4f, 0.4f, 0.6f));
    }
}
