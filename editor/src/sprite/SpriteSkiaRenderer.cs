//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Unified SkiaSharp renderer for sprite vector paths.
//  Replaces both the custom scanline Rasterizer (export) and
//  LibTessDotNet mesh tessellation (real-time preview).
//

using System.Numerics;
using Clipper2Lib;
using SkiaSharp;

namespace NoZ.Editor;

internal static class SpriteSkiaRenderer
{
    private static SKPath PathsDToSKPath(PathsD paths)
    {
        var skPath = new SKPath { FillType = SKPathFillType.Winding };
        foreach (var path in paths)
        {
            if (path.Count < 3) continue;
            skPath.MoveTo((float)path[0].x, (float)path[0].y);
            for (int i = 1; i < path.Count; i++)
                skPath.LineTo((float)path[i].x, (float)path[i].y);
            skPath.Close();
        }
        return skPath;
    }

    private static SKColor ToSKColor(Color32 c) => new(c.R, c.G, c.B, c.A);

    private static void RenderResults(SKCanvas canvas, List<LayerPathResult> results)
    {
        foreach (var result in results)
        {
            if (result.Contours.Count == 0) continue;

            using var skPath = PathsDToSKPath(result.Contours);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            if (result.FillType == SpriteFillType.Linear)
            {
                var gs = Vector2.Transform(result.Gradient.Start, result.GradientTransform);
                var ge = Vector2.Transform(result.Gradient.End, result.GradientTransform);
                paint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(gs.X, gs.Y),
                    new SKPoint(ge.X, ge.Y),
                    [ToSKColor(result.Gradient.StartColor), ToSKColor(result.Gradient.EndColor)],
                    [0f, 1f],
                    SKShaderTileMode.Clamp);
            }
            else
            {
                paint.Color = ToSKColor(result.Color);
            }

            canvas.DrawPath(skPath, paint);
        }
    }

    // Renders all paths to a single Skia surface and copies pixels to target.
    // premul=true: copies premul pixels directly (for editor preview with BlendMode.Premultiplied)
    // premul=false: unpremultiplies for straight alpha (for atlas export with BlendMode.Alpha)
    public static unsafe void FillPixelData(
        List<LayerPathResult> results,
        PixelData<Color32> target,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi,
        Rect? clipRect = null,
        bool premul = false)
    {
        int w = targetRect.Width;
        int h = targetRect.Height;
        if (w <= 0 || h <= 0 || results.Count == 0) return;

        // F32 for export (unpremul needs float precision), 8-bit for preview (stays premul)
        var colorType = premul ? SKColorType.Rgba8888 : SKColorType.RgbaF32;
        var info = new SKImageInfo(w, h, colorType, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Clip in pixel space before applying transform so it lands on exact pixel boundaries
        if (clipRect.HasValue)
        {
            var cr = clipRect.Value;
            canvas.ClipRect(new SKRect(
                cr.Left * dpi + sourceOffset.X,
                cr.Top * dpi + sourceOffset.Y,
                cr.Right * dpi + sourceOffset.X,
                cr.Bottom * dpi + sourceOffset.Y), antialias: false);
        }

        canvas.Translate(sourceOffset.X, sourceOffset.Y);
        canvas.Scale(dpi, dpi);

        RenderResults(canvas, results);

        var readAlpha = premul ? SKAlphaType.Premul : SKAlphaType.Unpremul;
        var readInfo = new SKImageInfo(w, h, SKColorType.Rgba8888, readAlpha);
        var stride = w * 4;
        var buf = new byte[stride * h];
        fixed (byte* ptr = buf)
        {
            surface.ReadPixels(readInfo, (nint)ptr, stride, 0, 0);
            var srcSpan = new ReadOnlySpan<Color32>(ptr, w * h);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var src = srcSpan[y * w + x];
                    if (src.A == 0) continue;
                    target[targetRect.X + x, targetRect.Y + y] = src;
                }
        }
    }

    // Preview path: renders each path individually to a clean surface,
    // then composites into the output buffer in premultiplied alpha with
    // additive alpha for opaque paths (matches the export path logic).
    public static unsafe void RenderToPixelsPremul(
        List<LayerPathResult> results,
        int width, int height,
        Color32* outPixels,
        float scaleX = 1f, float scaleY = 1f,
        float translateX = 0f, float translateY = 0f)
    {
        if (width <= 0 || height <= 0 || results.Count == 0) return;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return;

        var canvas = surface.Canvas;
        var count = width * height;

        foreach (var result in results)
        {
            if (result.Contours.Count == 0) continue;

            canvas.Clear(SKColors.Transparent);
            canvas.Save();
            canvas.Translate(translateX, translateY);
            canvas.Scale(scaleX, scaleY);

            using var skPath = PathsDToSKPath(result.Contours);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            if (result.FillType == SpriteFillType.Linear)
            {
                var gs = Vector2.Transform(result.Gradient.Start, result.GradientTransform);
                var ge = Vector2.Transform(result.Gradient.End, result.GradientTransform);
                paint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(gs.X, gs.Y),
                    new SKPoint(ge.X, ge.Y),
                    [ToSKColor(result.Gradient.StartColor), ToSKColor(result.Gradient.EndColor)],
                    [0f, 1f],
                    SKShaderTileMode.Clamp);
            }
            else
            {
                paint.Color = ToSKColor(result.Color);
            }

            canvas.DrawPath(skPath, paint);
            canvas.Restore();

            bool isOpaque = result.Color.A == 255 && result.FillType == SpriteFillType.Solid;

            using var pixmap = surface.PeekPixels();
            var src = (Color32*)pixmap.GetPixels();

            for (int i = 0; i < count; i++)
            {
                var s = src[i];
                if (s.A == 0) continue;

                if (outPixels[i].A == 0)
                {
                    outPixels[i] = s;
                }
                else
                {
                    // Premul blend: out = src + dst * (1 - srcA)
                    var d = outPixels[i];
                    float invA = (255 - s.A) / 255f;
                    var blended = new Color32(
                        (byte)Math.Min(s.R + (int)(d.R * invA + 0.5f), 255),
                        (byte)Math.Min(s.G + (int)(d.G * invA + 0.5f), 255),
                        (byte)Math.Min(s.B + (int)(d.B * invA + 0.5f), 255),
                        (byte)Math.Min(s.A + (int)(d.A * invA + 0.5f), 255));
                    // Additive alpha for opaque paths: complementary coverages sum to full
                    if (isOpaque)
                        blended.A = (byte)Math.Min(s.A + d.A, 255);
                    outPixels[i] = blended;
                }
            }
        }
    }

    public static unsafe void RenderToPixelsTintedPremul(
        List<LayerPathResult> results,
        int width, int height,
        Color32* outPixels,
        Color tint,
        float scaleX = 1f, float scaleY = 1f,
        float translateX = 0f, float translateY = 0f)
    {
        if (width <= 0 || height <= 0 || results.Count == 0) return;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(translateX, translateY);
        canvas.Scale(scaleX, scaleY);

        foreach (var result in results)
        {
            if (result.Contours.Count == 0) continue;
            using var skPath = PathsDToSKPath(result.Contours);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(
                    (byte)(tint.R * 255),
                    (byte)(tint.G * 255),
                    (byte)(tint.B * 255),
                    (byte)(tint.A * 255)),
            };
            canvas.DrawPath(skPath, paint);
        }

        // Premul blend into existing pixel buffer
        using var pixmap = surface.PeekPixels();
        var src = (Color32*)pixmap.GetPixels();
        var count = width * height;

        for (int i = 0; i < count; i++)
        {
            if (src[i].A == 0) continue;
            if (outPixels[i].A == 0)
            {
                outPixels[i] = src[i];
            }
            else
            {
                // Premultiplied blend: out = src + dst * (1 - srcA)
                var s = src[i];
                var d = outPixels[i];
                float invA = (255 - s.A) / 255f;
                outPixels[i] = new Color32(
                    (byte)Math.Min(s.R + (int)(d.R * invA + 0.5f), 255),
                    (byte)Math.Min(s.G + (int)(d.G * invA + 0.5f), 255),
                    (byte)Math.Min(s.B + (int)(d.B * invA + 0.5f), 255),
                    (byte)Math.Min(s.A + (int)(d.A * invA + 0.5f), 255));
            }
        }
    }

    public static byte[] RenderToPng(
        List<LayerPathResult> results,
        int width, int height,
        float scaleX = 1f, float scaleY = 1f,
        float translateX = 0f, float translateY = 0f,
        Rect? clipRect = null)
    {
        if (width <= 0 || height <= 0 || results.Count == 0) return [];

        // PNG expects straight alpha; Skia's PNG encoder handles the conversion
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return [];

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(translateX, translateY);
        canvas.Scale(scaleX, scaleY);

        if (clipRect.HasValue)
        {
            var cr = clipRect.Value;
            canvas.ClipRect(new SKRect(cr.Left, cr.Top, cr.Right, cr.Bottom), antialias: false);
        }

        RenderResults(canvas, results);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
