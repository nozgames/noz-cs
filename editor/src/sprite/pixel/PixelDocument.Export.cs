//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public partial class PixelDocument
{
    internal override void RasterizeCore(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        var fi = GetFrameAtTimeSlot(rect.FrameIndex);

        var srcX = RasterBounds.X + CanvasSize.X / 2;
        var srcY = RasterBounds.Y + CanvasSize.Y / 2;
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        var padding2 = padding * 2;

        var rasterRect = new RectInt(
            rect.Rect.Position + new Vector2Int(padding, padding),
            new Vector2Int(w, h));

        RasterizePixelLayersRecursive(Root, image, rasterRect, srcX, srcY, w, h);

        image.BleedColors(rasterRect);
        for (var p = padding - 1; p >= 0; p--)
        {
            var padRect = new RectInt(
                rect.Rect.Position + new Vector2Int(p, p),
                new Vector2Int(w + padding2, h + padding2) - new Vector2Int(p * 2, p * 2));
            image.ExtrudeEdges(padRect);
        }
    }

    public byte[] RasterizeColorToPng()
    {
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        if (w <= 0 || h <= 0) return [];

        if (Root.Children.Count == 0)
            return [];

        var srcX = RasterBounds.X + CanvasSize.X / 2;
        var srcY = RasterBounds.Y + CanvasSize.Y / 2;

        using var pixels = new PixelData<Color32>(w, h);
        var rasterRect = new RectInt(0, 0, w, h);
        RasterizePixelLayersRecursive(Root, pixels, rasterRect, srcX, srcY, w, h);

        using var image = Image.LoadPixelData<Rgba32>(pixels.AsReadonlySpan(), w, h);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static void RasterizePixelLayersRecursive(
        SpriteNode parent, PixelData<Color32> image, RectInt rasterRect,
        int srcX, int srcY, int w, int h)
    {
        foreach (var child in parent.Children)
        {
            if (!child.Visible) continue;

            if (child.IsExpandable)
            {
                RasterizePixelLayersRecursive(child, image, rasterRect, srcX, srcY, w, h);
                continue;
            }

            if (child is not PixelLayer layer || layer.Pixels == null)
                continue;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var src = layer.Pixels[srcX + x, srcY + y];
                    if (src.A == 0) continue;

                    var dx = rasterRect.X + x;
                    var dy = rasterRect.Y + y;
                    ref var dst = ref image[dx, dy];

                    if (dst.A == 0)
                    {
                        dst = src;
                    }
                    else
                    {
                        var sa = src.A / 255f;
                        var da = dst.A / 255f;
                        var outA = sa + da * (1f - sa);
                        if (outA > 0f)
                        {
                            var invOutA = 1f / outA;
                            dst = new Color32(
                                (byte)((src.R * sa + dst.R * da * (1f - sa)) * invOutA),
                                (byte)((src.G * sa + dst.G * da * (1f - sa)) * invOutA),
                                (byte)((src.B * sa + dst.B * da * (1f - sa)) * invOutA),
                                (byte)(outA * 255f));
                        }
                    }
                }
            }
        }
    }
}
