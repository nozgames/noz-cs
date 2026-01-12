//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public class RenderBackendConfig
{
    public bool VSync { get; set; } = true;
    public int MaxCommands { get; set; } = 1024;
}

public interface IRender
{
    void Init(RenderBackendConfig config);
    void Shutdown();

    void BeginFrame();
    void EndFrame();

    void Clear(Color color);
    void SetViewport(int x, int y, int width, int height);

    // Future: texture, shader, draw calls
    // uint CreateTexture(int width, int height, ReadOnlySpan<byte> data);
    // void DestroyTexture(uint textureId);
    // void DrawQuad(uint textureId, in Matrix3x2 transform, Color color);
}
