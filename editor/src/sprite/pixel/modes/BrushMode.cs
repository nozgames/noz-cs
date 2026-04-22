//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BrushMode : PixelStrokeMode
{
    protected override void PaintPixel(Vector2Int pixel)
    {
        Editor.PaintBrushSoft(pixel, Editor.BrushColor, Editor.BrushHardness);
    }
}
