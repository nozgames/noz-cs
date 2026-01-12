//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.OpenGL;
using static SDL.SDL3;

namespace noz;

public unsafe class OpenGLRender : IRender
{
    private RenderBackendConfig _config = null!;
    private GL _gl = null!;

    public void Init(RenderBackendConfig config)
    {
        _config = config;

        // Create GL context using SDL's GetProcAddress
        _gl = GL.GetApi(name => (nint)SDL_GL_GetProcAddress(name));

        // Enable standard blend mode
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public void Shutdown()
    {
        _gl.Dispose();
    }

    public void BeginFrame()
    {
        // Reset state if needed
    }

    public void EndFrame()
    {
        // Note: SwapBuffers is handled by IPlatform, not IRender
    }

    public void Clear(Color color)
    {
        _gl.ClearColor(color.R, color.G, color.B, color.A);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        _gl.Viewport(x, y, (uint)width, (uint)height);
    }
}
