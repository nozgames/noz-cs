//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class Texture : Asset, IImage
{
    float IImage.ImageWidth => Width;
    float IImage.ImageHeight => Height;

    internal const ushort Version = 1;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public TextureFormat Format { get; private set; }
    public TextureFilter Filter { get; private set; }
    public TextureClamp Clamp { get; private set; }
    public byte[] Data { get; private set; } = [];
    public bool IsArray { get; private set; }

    private bool _ownsRenderTexture;

    private Texture(string name, bool isArray) : base(AssetType.Texture, name)
    {
        IsArray = isArray;
    }

    public Texture() : base(AssetType.Texture) { }

    public static Texture Create(
        int width,
        int height,
        ReadOnlySpan<byte> data,
        TextureFormat format = TextureFormat.RGBA8,
        TextureFilter filter = TextureFilter.Linear,
        string name = "")
    {
        var texture = new Texture(name, false)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = TextureClamp.Clamp,
            Data = data.ToArray()
        };
        texture.Upload();
        texture.Register();
        return texture;
    }

    public static Texture CreateFromRenderTexture(RenderTexture rt, string name = "")
    {
        var texture = new Texture(name, false)
        {
            Width = rt.Width,
            Height = rt.Height,
        };
        texture.Handle = rt.Handle;
        texture._ownsRenderTexture = true;
        texture.Register();
        return texture;
    }

    protected override void Load(BinaryReader reader)
    {
        Format = (TextureFormat)reader.ReadByte();
        Filter = (TextureFilter)reader.ReadByte();
        Clamp = (TextureClamp)reader.ReadByte();
        Width = (int)reader.ReadUInt32();
        Height = (int)reader.ReadUInt32();

        var dataSize = Width * Height * GetBytesPerPixel(Format);
        Data = reader.ReadBytes(dataSize);
        Upload();
    }

    private static Texture? Load(Stream stream, string name)
    {
        var texture = new Texture(name, false);
        using var reader = new BinaryReader(stream);
        texture.Load(reader);
        return texture;
    }

    public void Upload()
    {
        if (Handle != nuint.Zero)
            Graphics.Driver.DestroyTexture(Handle);
        Handle = Graphics.Driver.CreateTexture(Width, Height, Data, Format, Filter, name: Name);
    }

    public static Texture? CreateArray(string name, params Atlas?[] atlases)
    {
        var validAtlases = atlases.Where(a => a != null).ToArray();
        if (validAtlases.Length == 0)
            return null;

        var first = validAtlases[0]!;
        var width = first.Width;
        var height = first.Height;
        var format = first.Format;
        var filter = first.Filter;

        var layerData = validAtlases.Select(a => a!.Data).ToArray();
        var handle = Graphics.Driver.CreateTextureArray(width, height, layerData, format, filter, name);

        var texture = new Texture(name, true)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = TextureClamp.Clamp,
            Handle = handle
        };

        texture.Register();
        return texture;
    }

    public static Texture? CreateArray(string name, int width, int height, byte[][] layerData,
        TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear)
    {
        if (layerData.Length == 0)
            return null;

        var handle = Graphics.Driver.CreateTextureArray(width, height, layerData, format, filter, name);

        var texture = new Texture(name, true)
        {
            Width = width,
            Height = height,
            Format = format,
            Filter = filter,
            Clamp = TextureClamp.Clamp,
            Handle = handle
        };

        texture.Register();
        return texture;
    }

    public void Update(ReadOnlySpan<byte> data)
    {
        if (Handle == nuint.Zero)
            return;
        Graphics.Driver.UpdateTexture(Handle, new Vector2Int(Width, Height), data);
    }

    public void Update(ReadOnlySpan<byte> data, in RectInt region, int srcWidth = -1)
    {
        if (Handle == nuint.Zero)
            return;
        Graphics.Driver.UpdateTextureRegion(Handle, region, data, srcWidth);
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Texture, "Texture", typeof(Texture), Load, Version));
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
        Unregister();

        if (Handle != nuint.Zero)
        {
            if (_ownsRenderTexture)
                Graphics.Driver.DestroyRenderTexture(Handle);
            else
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
