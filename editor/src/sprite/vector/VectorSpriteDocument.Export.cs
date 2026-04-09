//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Clipper2Lib;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public partial class VectorSpriteDocument
{
    private const int RasterizeSize = 1024;

    internal override void RasterizeCore(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        var dpi = PixelsPerUnit;
        var padding2 = padding * 2;
        var targetRect = new RectInt(
            rect.Rect.Position,
            new Vector2Int(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2));
        var sourceOffset = -RasterBounds.Position + new Vector2Int(padding, padding);

        Rect? clipRect = null;
        if (ConstrainedSize.HasValue)
        {
            float invDpi = 1f / dpi;
            clipRect = new Rect(
                RasterBounds.X * invDpi,
                RasterBounds.Y * invDpi,
                RasterBounds.Width * invDpi,
                RasterBounds.Height * invDpi);
        }

        RasterizeLayer(Root, image, targetRect, sourceOffset, dpi, clipRect);

        image.BleedColors(targetRect);
    }

    internal static void RasterizeLayer(
        SpriteGroup layer,
        PixelData<Color32> image,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi,
        Rect? clipRect = null)
    {
        var results = new List<LayerPathResult>();
        SpriteGroupProcessor.ProcessLayer(layer, results);

        PathsD? clipPaths = null;
        if (clipRect.HasValue)
        {
            var cr = clipRect.Value;
            clipPaths = new PathsD { new PathD {
                new PointD(cr.Left, cr.Top),
                new PointD(cr.Right, cr.Top),
                new PointD(cr.Right, cr.Bottom),
                new PointD(cr.Left, cr.Bottom)
            }};
        }

        // Edge buffer for two-pass AA: paths write binary coverage to the primary
        // image and record sub-threshold edge coverage here. After all paths are
        // rasterized, Composite lerps the edge contributions back on top.
        using var edgeBuffer = new PixelData<EdgePixel>(targetRect.Size);

        foreach (var result in results)
        {
            var contours = result.Contours;
            if (clipPaths != null && contours.Count > 0)
            {
                contours = Clipper.BooleanOp(ClipType.Intersection,
                    contours, clipPaths, FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }

            Rasterizer.Fill(contours, image, edgeBuffer, targetRect, sourceOffset, dpi, result.Color);
        }

        Rasterizer.Composite(image, edgeBuffer, targetRect);
    }

    public byte[] RasterizeColorToPng()
    {
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        if (w <= 0 || h <= 0) return [];

        if (Root.Children.Count == 0)
            return [];

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var scale = (float)RasterizeSize / MathF.Max(w, h);
        var scaledDpi = (int)MathF.Round(dpi * scale);
        var outW = (int)MathF.Round(w * scale);
        var outH = (int)MathF.Round(h * scale);

        using var pixels = new PixelData<Color32>(outW, outH);

        var targetRect = new RectInt(0, 0, outW, outH);
        var sourceOffset = new Vector2Int(
            (int)MathF.Round(-RasterBounds.X * scale),
            (int)MathF.Round(-RasterBounds.Y * scale));

        Rect? clipRect = null;
        if (ConstrainedSize.HasValue)
        {
            float invDpi = 1f / dpi;
            clipRect = new Rect(
                RasterBounds.X * invDpi,
                RasterBounds.Y * invDpi,
                RasterBounds.Width * invDpi,
                RasterBounds.Height * invDpi);
        }

        RasterizeLayer(Root, pixels, targetRect, sourceOffset, scaledDpi, clipRect);

        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(pixels.AsByteSpan(), outW, outH);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
