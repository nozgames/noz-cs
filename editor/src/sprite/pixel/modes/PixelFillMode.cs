//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelFillMode : EditorMode<PixelSpriteEditor>
{
    // Scanline flood fill: one stack entry per matching horizontal run discovered above or
    // below the current span, instead of one queue entry per pixel. Reused across fills
    // within the same mode instance so repeated clicks don't re-allocate.
    private readonly Stack<(int x, int y)> _seeds = new();

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            var mouseWorld = Workspace.MouseWorldPosition;
            var pixel = Editor.WorldToPixelSnapped(mouseWorld);
            Fill(pixel);
        }
    }

    private void Fill(Vector2Int seed)
    {
        var layer = Editor.ActiveLayer;
        if (layer?.Pixels == null || layer.Locked || !layer.Visible) return;
        if (!Editor.IsPixelInConstraint(seed)) return;
        if (!Editor.IsPixelSelected(seed.X, seed.Y)) return;

        var pixels = layer.Pixels;
        var targetColor = pixels[seed.X, seed.Y];
        var fillColor = Editor.BrushColor;

        if (targetColor == fillColor) return;

        Undo.Record(Editor.Document);

        var r = Editor.EditablePixelRect;
        var xMin = r.X;
        var xMax = r.X + r.Width;
        var yMin = r.Y;
        var yMax = r.Y + r.Height;

        var stack = _seeds;
        stack.Clear();
        stack.Push((seed.X, seed.Y));

        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();

            // A seed may already be filled if an earlier span covered it.
            if (pixels[sx, sy] != targetColor) continue;

            // Extend the span left and right from the seed to the first non-matching pixel
            // (or the constraint edge, or a selection-masked pixel).
            var left = sx;
            while (left - 1 >= xMin
                   && pixels[left - 1, sy] == targetColor
                   && Editor.IsPixelSelected(left - 1, sy))
                left--;

            var right = sx;
            while (right + 1 < xMax
                   && pixels[right + 1, sy] == targetColor
                   && Editor.IsPixelSelected(right + 1, sy))
                right++;

            for (var x = left; x <= right; x++)
                pixels.Set(x, sy, fillColor);

            if (sy - 1 >= yMin) SeedRow(pixels, stack, left, right, sy - 1, targetColor);
            if (sy + 1 < yMax) SeedRow(pixels, stack, left, right, sy + 1, targetColor);
        }

        Editor.InvalidateComposite();
        Editor.InvalidateActiveLayerPreview();
    }

    private void SeedRow(PixelData<Color32> pixels, Stack<(int x, int y)> stack, int xStart, int xEnd, int y, Color32 targetColor)
    {
        var x = xStart;
        while (x <= xEnd)
        {
            while (x <= xEnd && (pixels[x, y] != targetColor || !Editor.IsPixelSelected(x, y)))
                x++;
            if (x > xEnd) break;

            // One seed per contiguous matching sub-span — the pop will re-expand it.
            var spanStart = x;
            while (x <= xEnd
                   && pixels[x, y] == targetColor
                   && Editor.IsPixelSelected(x, y))
                x++;
            stack.Push((spanStart, y));
        }
    }

    public override void Draw()
    {
        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixelSnapped(mouseWorld);
        if (!Editor.IsPixelInBounds(pixel)) return;
        Editor.DrawBrushOutline(pixel, new Color(0.4f, 0.8f, 1f, 0.6f));
    }
}
