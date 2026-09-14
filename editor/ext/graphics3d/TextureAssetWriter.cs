//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor.Graphics3D;

/// <summary>Shared runtime texture export for palette authoring and host image importers.</summary>
public static class TextureAssetWriter
{
    /// <summary>Writes a clamped RGBA8 texture using the registered runtime Texture format.</summary>
    public static void WriteRgba8(BinaryWriter writer, int width, int height, TextureFilter filter, ReadOnlySpan<byte> pixels)
    {
        var def = Asset.GetDef(AssetType.Texture) ??
                  throw new InvalidOperationException("The runtime Texture asset type must be registered before export.");
        writer.WriteAssetHeader(AssetType.Texture, def.Version);
        writer.Write((byte)TextureFormat.RGBA8);
        writer.Write((byte)filter);
        writer.Write((byte)TextureClamp.Clamp);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write(pixels);
    }
}
