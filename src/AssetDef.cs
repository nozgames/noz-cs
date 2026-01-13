//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

internal delegate Asset LoadAssetDelegate(Stream stream, string name);

internal class AssetDef(AssetType type, LoadAssetDelegate load)
{
    public AssetType Type { get; } = type;
    public LoadAssetDelegate Load { get; } = load;
}

