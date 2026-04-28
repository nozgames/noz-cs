//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelFillMode : EditorMode<PixelEditor>
{
    private readonly Stack<(int x, int y)> _seeds = new();
    private readonly List<(int left, int right, int y)> _filledSpans = new();

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All) ||
            Input.WasButtonPressed(InputCode.Pen, InputScope.All))
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

        // Soft-edge mode: when the seed is transparent, match neighbors by alpha==0 alone
        // (ignoring RGB so the soft brush's apron ring — alpha=0, RGB=strokeColor — doesn't
        // block propagation) and blend the fill color under partial-alpha boundary pixels
        // so the anti-aliased stroke edge is preserved instead of leaving an inner ring.
        var softEdgeMode = targetColor.A == 0;

        Undo.Record(Editor.Document);

        var r = Editor.EditablePixelRect;
        var xMin = r.X;
        var xMax = r.X + r.Width;
        var yMin = r.Y;
        var yMax = r.Y + r.Height;

        var stack = _seeds;
        stack.Clear();
        var filled = _filledSpans;
        filled.Clear();
        stack.Push((seed.X, seed.Y));

        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();

            // A seed may already be filled if an earlier span covered it.
            if (!IsMatch(pixels[sx, sy], targetColor, softEdgeMode)) continue;

            // Extend the span left and right from the seed to the first non-matching pixel
            // (or the constraint edge, or a selection-masked pixel).
            var left = sx;
            while (left - 1 >= xMin
                   && IsMatch(pixels[left - 1, sy], targetColor, softEdgeMode)
                   && Editor.IsPixelSelected(left - 1, sy))
                left--;

            var right = sx;
            while (right + 1 < xMax
                   && IsMatch(pixels[right + 1, sy], targetColor, softEdgeMode)
                   && Editor.IsPixelSelected(right + 1, sy))
                right++;

            for (var x = left; x <= right; x++)
                pixels.Set(x, sy, fillColor);

            if (softEdgeMode)
                filled.Add((left, right, sy));

            if (sy - 1 >= yMin) SeedRow(pixels, stack, left, right, sy - 1, targetColor, softEdgeMode);
            if (sy + 1 < yMax) SeedRow(pixels, stack, left, right, sy + 1, targetColor, softEdgeMode);
        }

        if (softEdgeMode)
            CompositeSoftEdges(pixels, fillColor, xMin, xMax, yMin, yMax);

        Editor.InvalidateComposite();
        Editor.InvalidateActiveLayerPreview();
    }

    private static bool IsMatch(Color32 pixel, Color32 target, bool softEdgeMode)
        => softEdgeMode ? pixel.A == 0 : pixel == target;

    // Walks each filled span and blends fillColor under every 4-connected neighbor whose
    // alpha is partial (0 < A < 255). A second visit is a no-op because the first blend
    // raises alpha to 255, so no explicit dedupe is needed.
    private void CompositeSoftEdges(PixelData<Color32> pixels, Color32 fillColor, int xMin, int xMax, int yMin, int yMax)
    {
        foreach (var (left, right, y) in _filledSpans)
        {
            if (y - 1 >= yMin)
                for (var x = left; x <= right; x++)
                    TryBlendUnder(pixels, x, y - 1, fillColor);

            if (y + 1 < yMax)
                for (var x = left; x <= right; x++)
                    TryBlendUnder(pixels, x, y + 1, fillColor);

            if (left - 1 >= xMin) TryBlendUnder(pixels, left - 1, y, fillColor);
            if (right + 1 < xMax) TryBlendUnder(pixels, right + 1, y, fillColor);
        }
    }

    private void TryBlendUnder(PixelData<Color32> pixels, int x, int y, Color32 fillColor)
    {
        if (!Editor.IsPixelSelected(x, y)) return;
        var p = pixels[x, y];
        if (p.A == 0 || p.A == 255) return;
        // Composite existing pixel OVER fill color: preserves the anti-aliased stroke edge
        // while replacing the transparent background with fillColor.
        pixels.Set(x, y, Color32.Blend(fillColor, p));
    }

    private void SeedRow(PixelData<Color32> pixels, Stack<(int x, int y)> stack, int xStart, int xEnd, int y, Color32 targetColor, bool softEdgeMode)
    {
        var x = xStart;
        while (x <= xEnd)
        {
            while (x <= xEnd && (!IsMatch(pixels[x, y], targetColor, softEdgeMode) || !Editor.IsPixelSelected(x, y)))
                x++;
            if (x > xEnd) break;

            // One seed per contiguous matching sub-span — the pop will re-expand it.
            var spanStart = x;
            while (x <= xEnd
                   && IsMatch(pixels[x, y], targetColor, softEdgeMode)
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
