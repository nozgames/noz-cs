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

        using var writer = new BinaryWriter(File.OpenWrite(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);

        writer.Write((ushort)PixelsPerUnit);
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((ushort)SortOrder);
        writer.Write((byte)(BoneIndex == -1 ? 255 : BoneIndex));

        var ppu = PixelsPerUnit;
        var et = (short)MathF.Round(Edges.T * ppu);
        var el = (short)MathF.Round(Edges.L * ppu);
        var eb = (short)MathF.Round(Edges.B * ppu);
        var er = (short)MathF.Round(Edges.R * ppu);
        writer.Write(et);
        writer.Write(el);
        writer.Write(eb);
        writer.Write(er);
        writer.Write(Sprite.CalculateSliceMask(RasterBounds, new EdgeInsets(et, el, eb, er)));

        writer.Write(frameCount);
        writer.Write((byte)DefaultFrameRate);
        for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            // Per-frame offset is sprite metadata; UV/size live in the atlas.
            // RasterBounds is currently the same for all frames, so use its X/Y as offset.
            writer.Write((short)RasterBounds.X);
            writer.Write((short)RasterBounds.Y);
        }

        writer.Write((byte)TextureFilter);
    }
}
