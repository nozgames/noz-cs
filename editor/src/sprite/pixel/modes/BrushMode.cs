//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BrushMode : PixelStrokeMode
{
    protected override PixelBrushType BrushType => Editor.Document.BrushType;

    protected override void PaintPixel(Vector2Int pixel)
    {
        if (BrushType == PixelBrushType.Pencil)
            Editor.PaintBrush(pixel, Editor.BrushColor);
        else
            Editor.PaintBrushSoft(new Vector2(pixel.X + 0.5f, pixel.Y + 0.5f),
                Editor.BrushColor, Editor.BrushHardness);
    }

    protected override void OnSoftStrokeBegin(Vector2 worldPixel)
    {
        Editor.PaintBrushSoft(worldPixel, Editor.BrushColor, Editor.BrushHardness);
    }

    protected override void OnSoftStrokeSegment(Vector2 from, Vector2 to)
    {
        // Sub-pixel stamp spacing. Dense enough for continuity at size 1 (radius 0.5, the
        // AA ramp ends at 1 px from center, so ≤ 0.5-px spacing avoids gaps); scales with
        // brush size to keep the total stamp count manageable at larger sizes.
        var spacing = MathF.Max(0.25f, Editor.BrushSize * 0.1f);
        var delta = to - from;
        var dist = delta.Length();
        if (dist <= 0f) return;

        var steps = (int)MathF.Ceiling(dist / spacing);
        var color = Editor.BrushColor;
        var hardness = Editor.BrushHardness;
        // Skip i=0 (= `from`, already stamped by the previous segment or OnSoftStrokeBegin).
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            Editor.PaintBrushSoft(from + delta * t, color, hardness);
        }
    }
}
