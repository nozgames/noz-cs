//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct RenderBatch
{
    public int FirstIndex;
    public int IndexCount;
    public nuint TextureHandle;
    public nuint ShaderHandle;
    public BlendMode Blend;
}
