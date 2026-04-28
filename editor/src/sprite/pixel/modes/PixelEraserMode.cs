//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelEraserMode : PixelStrokeMode
{
    protected override PixelBrushType BrushType => Editor.Document.EraserType;
    protected override Color OutlineColor => new(1f, 0.4f, 0.4f, 0.6f);
    protected override EditorMode? EyeDropperExitMode => new BrushMode();

    protected override void PaintPixel(Vector2Int pixel)
    {
        if (BrushType == PixelBrushType.Pencil)
            Editor.PaintBrush(pixel, default, blend: false, erase: true);
        else
            Editor.PaintBrushSoft(new Vector2(pixel.X + 0.5f, pixel.Y + 0.5f),
                default, Editor.BrushHardness, erase: true);
    }

    protected override void OnSoftStrokeBegin(Vector2 worldPixel)
    {
        Editor.PaintBrushSoft(worldPixel, default, Editor.BrushHardness, erase: true);
    }

    protected override void OnSoftStrokeSegment(Vector2 from, Vector2 to)
    {
        var spacing = MathF.Max(0.25f, Editor.BrushSize * 0.1f);
        var delta = to - from;
        var dist = delta.Length();
        if (dist <= 0f) return;

        var steps = (int)MathF.Ceiling(dist / spacing);
        var hardness = Editor.BrushHardness;
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            Editor.PaintBrushSoft(from + delta * t, default, hardness, erase: true);
        }
    }
}
