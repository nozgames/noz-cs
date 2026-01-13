//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public class Texture : Asset
{
    internal const byte Version = 1;

    private Texture(string name) : base(AssetType.Texture, name)
    {
    }

    private static Asset Load(Stream stream, string name)
    {
        var texture = new Texture(name);
        return texture;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Texture, Load));
    }
}
