//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class Atlas : Asset
{
    internal const ushort Version = 1;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public TextureFormat Format { get; private set; }
    public TextureFilter Filter { get; private set; }
    public TextureClamp Clamp { get; private set; }
    public byte[] Data { get; private set; } = [];
    public int Index { get; internal set; }

    private Atlas(string name) : base(AssetType.Atlas, name)
    {
    }

    public Atlas() : base(AssetType.Atlas) { }

    protected override void Load(BinaryReader reader)
    {
        Format = (TextureFormat)reader.ReadByte();
        Filter = (TextureFilter)reader.ReadByte();
        Clamp = (TextureClamp)reader.ReadByte();
        Width = (int)reader.ReadUInt32();
        Height = (int)reader.ReadUInt32();

        var dataSize = Width * Height * GetBytesPerPixel(Format);
        Data = reader.ReadBytes(dataSize);
    }

    private static Atlas? Load(Stream stream, string name)
    {
        var atlas = new Atlas(name);
        using var reader = new BinaryReader(stream);
        atlas.Load(reader);
        return atlas;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Atlas, "Atlas", typeof(Atlas), Load, Version));
    }

    public override void Dispose()
    {
        if (Handle != nuint.Zero)
        {
            Graphics.Driver.DestroyTexture(Handle);
            Handle = nuint.Zero;
        }

        base.Dispose();
    }

    private static int GetBytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.RGBA8 => 4,
        TextureFormat.RGB8 => 3,
        TextureFormat.RG8 => 2,
        TextureFormat.R8 => 1,
        _ => 4
    };
}
