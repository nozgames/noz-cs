//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Sprite document rasterization and binary export.
//

using Clipper2Lib;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class SpriteDocument
{
    internal void Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        // Raster sprites: composite layers directly
        if (SpriteMode == SpriteType.Raster)
        {
            RasterizePixelLayers(image, rect, padding);
            return;
        }

        // Static image or companion image: blit from file
        if (ImageFilePath != null && EditorApplication.Store.FileExists(ImageFilePath) && (!IsMutable || Generation is { HasImageData: true }))
        {
            RasterizeImageFile(image, rect, padding);
            return;
        }

        // Generated sprites: blit texture pixels instead of rasterizing paths
        if (Generation is { HasImageData: true })
        {
            var w = RasterBounds.Size.X;
            var h = RasterBounds.Size.Y;

            using var ms = new MemoryStream(Generation.Job.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);
            if (srcImage.Width != w || srcImage.Height != h)
                srcImage.Mutate(x => x.Resize(w, h));

            var rasterRect = new RectInt(
                rect.Rect.Position + new Vector2Int(padding, padding),
                new Vector2Int(w, h));

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var pixel = srcImage[x, y];
                    image[rasterRect.X + x, rasterRect.Y + y] = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
                }

            image.BleedColors(rasterRect);
            return;
        }

        var frameIndex = rect.FrameIndex;
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var padding2 = padding * 2;

        var fi = GetFrameAtTimeSlot(frameIndex);

        // Apply frame visibility if animated, saving/restoring to avoid side effects
        Dictionary<SpriteNode, bool>? savedVisibility = null;
        if (fi < AnimFrames.Count)
        {
            savedVisibility = new Dictionary<SpriteNode, bool>();
            Root.ForEach(layer =>
            {
                if (layer != Root)
                    savedVisibility[layer] = layer.Visible;
            });
            AnimFrames[fi].ApplyVisibility(Root);
        }

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

        // Restore original visibility
        if (savedVisibility != null)
        {
            foreach (var (layer, visible) in savedVisibility)
                layer.Visible = visible;
        }

        image.BleedColors(targetRect);
    }

    private void RasterizeImageFile(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        if (ImageFilePath == null) return;

        using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(EditorApplication.Store.OpenRead(ImageFilePath));
        var srcW = srcImage.Width;
        var srcH = srcImage.Height;
        var dstW = RasterBounds.Width;
        var dstH = RasterBounds.Height;
        var padding2 = padding * 2;

        // Compute source region (center crop if constraint is smaller)
        var srcX = Math.Max(0, (srcW - dstW) / 2);
        var srcY = Math.Max(0, (srcH - dstH) / 2);
        var copyW = Math.Min(srcW, dstW);
        var copyH = Math.Min(srcH, dstH);

        // Compute destination offset (center pad if constraint is larger)
        var dstOffX = Math.Max(0, (dstW - srcW) / 2);
        var dstOffY = Math.Max(0, (dstH - srcH) / 2);

        var rasterRect = new RectInt(
            rect.Rect.Position + new Vector2Int(padding, padding),
            new Vector2Int(dstW, dstH));

        for (int y = 0; y < copyH; y++)
            for (int x = 0; x < copyW; x++)
            {
                var pixel = srcImage[srcX + x, srcY + y];
                image[rasterRect.X + dstOffX + x, rasterRect.Y + dstOffY + y] = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
            }

        var outerRect = new RectInt(rect.Rect.Position, new Vector2Int(dstW + padding2, dstH + padding2));
        image.BleedColors(rasterRect);
        for (int p = padding - 1; p >= 0; p--)
        {
            var padRect = new RectInt(
                outerRect.Position + new Vector2Int(p, p),
                outerRect.Size - new Vector2Int(p * 2, p * 2));
            image.ExtrudeEdges(padRect);
        }
    }

    private static void RasterizeLayer(
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

        foreach (var result in results)
        {
            var contours = result.Contours;
            if (clipPaths != null && contours.Count > 0)
            {
                contours = Clipper.BooleanOp(ClipType.Intersection,
                    contours, clipPaths, FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }

            if (result.FillType == SpriteFillType.Linear)
                Rasterizer.Fill(contours, image, targetRect, sourceOffset, dpi,
                    result.FillType, result.Color, result.Gradient, result.GradientTransform);
            else
                Rasterizer.Fill(contours, image, targetRect, sourceOffset, dpi, result.Color);
        }
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        Skeleton.Resolve();
        ResolveBone();
        UpdateBounds();

        var frameCount = (ushort)TotalTimeSlots;

        if (Atlas == null)
        {
            Log.Error($"Sprite '{Name}' has no atlas — cannot export");
            return;
        }

        using var writer = new BinaryWriter(EditorApplication.Store.OpenWrite(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);

        writer.Write((ushort)(SpriteMode == SpriteType.Raster ? 32 : EditorApplication.Config.PixelsPerUnit));
        writer.Write((ushort)(Atlas.Index));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((ushort)SortOrder);
        writer.Write((byte)(BoneIndex == -1 ? 255 : BoneIndex));

        // 9-slice edges
        var activeEdges = Edges;
        writer.Write((short)activeEdges.T);
        writer.Write((short)activeEdges.L);
        writer.Write((short)activeEdges.B);
        writer.Write((short)activeEdges.R);
        writer.Write(Sprite.CalculateSliceMask(RasterBounds, activeEdges));

        // Write frames
        writer.Write(frameCount);
        writer.Write((byte)DefaultFrameRate);
        for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var uv = GetAtlasUV(frameIndex);
            WriteMesh(writer, uv, RasterBounds);
        }

        // Texture filter (v12)
        writer.Write((byte)(SpriteMode == SpriteType.Raster ? TextureFilter.Point : TextureFilter.Linear));
    }

    private static void WriteMesh(BinaryWriter writer, Rect uv, RectInt bounds)
    {
        writer.Write(uv.Left);
        writer.Write(uv.Top);
        writer.Write(uv.Right);
        writer.Write(uv.Bottom);
        writer.Write((short)bounds.X);
        writer.Write((short)bounds.Y);
        writer.Write((short)bounds.Width);
        writer.Write((short)bounds.Height);
    }

    private void RasterizePixelLayers(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        // Use trimmed RasterBounds for atlas packing
        var srcX = RasterBounds.X + CanvasSize.X / 2;
        var srcY = RasterBounds.Y + CanvasSize.Y / 2;
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        var padding2 = padding * 2;

        var rasterRect = new RectInt(
            rect.Rect.Position + new Vector2Int(padding, padding),
            new Vector2Int(w, h));

        foreach (var child in Root.Children)
        {
            if (child is not PixelLayer layer || !layer.Visible || layer.Pixels == null)
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

        image.BleedColors(rasterRect);
        for (var p = padding - 1; p >= 0; p--)
        {
            var padRect = new RectInt(
                rect.Rect.Position + new Vector2Int(p, p),
                new Vector2Int(w + padding2, h + padding2) - new Vector2Int(p * 2, p * 2));
            image.ExtrudeEdges(padRect);
        }
    }
}
