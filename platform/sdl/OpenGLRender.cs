//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public class OpenGLRender : IRender
{
    private RenderBackendConfig _config = null!;

    public void Init(RenderBackendConfig config)
    {
        _config = config;
        // TODO: Initialize OpenGL state, create default shader, etc.
    }

    public void Shutdown()
    {
        // TODO: Cleanup OpenGL resources
    }

    public void BeginFrame()
    {
        // TODO: Reset render state, bind default framebuffer
    }

    public void EndFrame()
    {
        // Note: SwapBuffers is handled by IPlatform, not IRender
        // This is intentional - the render backend just submits commands
    }

    public void Clear(Color color)
    {
        // TODO: glClearColor + glClear
        // For now this is a stub - will implement when we add OpenGL bindings
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        // TODO: glViewport
    }
}
