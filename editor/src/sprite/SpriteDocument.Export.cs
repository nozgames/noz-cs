//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract partial class SpriteDocument
{
    internal void Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        RasterizeCore(image, rect, padding);
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

        writer.Write((ushort)PixelsPerUnit);
        writer.Write((ushort)(Atlas.Index));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((ushort)SortOrder);
        writer.Write((byte)(BoneIndex == -1 ? 255 : BoneIndex));

        var activeEdges = Edges;
        writer.Write((short)activeEdges.T);
        writer.Write((short)activeEdges.L);
        writer.Write((short)activeEdges.B);
        writer.Write((short)activeEdges.R);
        writer.Write(Sprite.CalculateSliceMask(RasterBounds, activeEdges));

        writer.Write(frameCount);
        writer.Write((byte)DefaultFrameRate);
        for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var uv = GetAtlasUV(frameIndex);
            WriteMesh(writer, uv, RasterBounds);
        }

        writer.Write((byte)TextureFilter);
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
}
