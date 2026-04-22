//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelEraserMode : PixelStrokeMode
{
    protected override Color OutlineColor => new(1f, 0.4f, 0.4f, 0.6f);
    protected override EditorMode? EyeDropperExitMode => new PencilMode();

    protected override void PaintPixel(Vector2Int pixel)
    {
        Editor.PaintBrush(pixel, default, blend: false);
    }
}
