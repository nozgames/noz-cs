//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Sprite document rasterization and binary export.
//

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class SpriteDocument
{
    internal void Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        // Static image or companion image: blit from file
        if (ImageFilePath != null && File.Exists(ImageFilePath) && (!IsMutable || Generation is { HasImageData: true }))
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
        Dictionary<SpriteLayer, bool>? savedVisibility = null;
        if (fi < AnimFrames.Count)
        {
            savedVisibility = new Dictionary<SpriteLayer, bool>();
            RootLayer.ForEach(layer =>
            {
                if (layer != RootLayer)
                    savedVisibility[layer] = layer.Visible;
            });
            AnimFrames[fi].ApplyVisibility(RootLayer);
        }

        var targetRect = new RectInt(
            rect.Rect.Position,
            new Vector2Int(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2));
        var sourceOffset = -RasterBounds.Position + new Vector2Int(padding, padding);

        RasterizeLayer(RootLayer, image, targetRect, sourceOffset, dpi);

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

        using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageFilePath);
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
        SpriteLayer layer,
        PixelData<Color32> image,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi)
    {
        var results = new List<LayerPathResult>();
        SpriteLayerProcessor.ProcessLayer(layer, results);
        foreach (var result in results)
            Rasterizer.Fill(result.Contours, image, targetRect, sourceOffset, dpi, result.Color);
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        Skeleton.Resolve();
        UpdateBounds();

        var totalSlots = (ushort)TotalTimeSlots;
        var isStandalone = !ShouldAtlas || Atlas == null;
        var boneIndex = (short)BoneIndex;

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(totalSlots);
        writer.Write(isStandalone ? (ushort)0xFFFF : (ushort)(Atlas?.Index ?? 0));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((float)EditorApplication.Config.PixelsPerUnit);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write(boneIndex);
        writer.Write(totalSlots); // totalMeshes = 1 per frame = totalSlots
        writer.Write(DefaultFrameRate);

        // 9-slice edges
        var activeEdges = ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero;
        writer.Write((short)activeEdges.T);
        writer.Write((short)activeEdges.L);
        writer.Write((short)activeEdges.B);
        writer.Write((short)activeEdges.R);
        writer.Write(Sprite.CalculateSliceMask(RasterBounds, activeEdges));

        // Write one mesh per frame
        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            Rect uv;
            if (isStandalone)
            {
                // Standalone sprites pack frames in a vertical strip
                var frameH = 1.0f / totalSlots;
                uv = new Rect(0, frameIndex * frameH, 1, frameH);
            }
            else
            {
                uv = GetAtlasUV(frameIndex);
            }
            WriteMesh(writer, uv, sortOrder: 0, boneIndex: boneIndex, RasterBounds);
        }

        // Frame table
        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            writer.Write(frameIndex);   // meshStart
            writer.Write((ushort)1);    // meshCount
        }

        // Embedded texture for standalone sprites
        if (isStandalone)
            ExportEmbeddedTexture(writer);
    }

    private void ExportEmbeddedTexture(BinaryWriter writer)
    {
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        var totalSlots = (ushort)TotalTimeSlots;
        if (w <= 0 || h <= 0) return;

        // Pack all frames into a vertical strip
        var totalH = h * totalSlots;
        using var image = new PixelData<Color32>(w, totalH);

        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var rect = new AtlasSpriteRect
            {
                Name = Name,
                Source = this,
                Rect = new RectInt(0, frameIndex * h, w, h),
                FrameIndex = frameIndex
            };
            Rasterize(image, rect, padding: 0);
        }

        // Write texture header (same format as Atlas/Texture binary)
        writer.Write((byte)TextureFormat.RGBA8);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((byte)TextureClamp.Clamp);
        writer.Write((uint)w);
        writer.Write((uint)totalH);
        writer.Write(image.AsByteSpan());
    }

    private static void WriteMesh(BinaryWriter writer, Rect uv, short sortOrder, short boneIndex, RectInt bounds)
    {
        writer.Write(uv.Left);
        writer.Write(uv.Top);
        writer.Write(uv.Right);
        writer.Write(uv.Bottom);
        writer.Write(sortOrder);
        writer.Write(boneIndex);
        writer.Write((short)bounds.X);
        writer.Write((short)bounds.Y);
        writer.Write((short)bounds.Width);
        writer.Write((short)bounds.Height);
    }
}
