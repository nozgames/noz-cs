//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public struct ApplicationVtable
{
    public Action? Update;
}

public class ApplicationConfig
{
    public string Title { get; set; } = "Noz Application";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool VSync { get; set; } = true;
    public bool Resizable { get; set; } = true;

    public RenderConfig? Render { get; set; }

    public ApplicationVtable Vtable;

    /// <summary>
    /// Platform implementation. If null, defaults to SDLPlatform.
    /// </summary>
    public IPlatform? Platform { get; set; }

    /// <summary>
    /// Render backend implementation. If null, defaults to OpenGLRender.
    /// </summary>
    public IRender? RenderBackend { get; set; }
}
