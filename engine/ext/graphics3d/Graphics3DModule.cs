//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

/// <summary>Explicit opt-in; referencing the assembly alone does not register asset types.</summary>
public static class Graphics3DModule
{
    public static void RegisterAssetTypes() => Mesh.RegisterDef();
}
