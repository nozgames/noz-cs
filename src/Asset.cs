//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace noz;

public class Asset {
    internal AssetDef Def { get; }
    public string Name { get; private set; }
    private static readonly AssetDef[] Defs = new AssetDef[Constants.AssetTypeCount];

    internal Asset(AssetType type, string name)
    {
        Name = name;
        Def = Defs[(int)type];
        Debug.Assert(Def != null);
    }
    
    static Asset Load(AssetType type, string name)
    {
        return null;
    }

    internal static void RegisterDef(AssetDef def)
    {
        Debug.Assert(Defs[(int)def.Type] == null);
        Defs[(int)def.Type] = def;
    }
}
