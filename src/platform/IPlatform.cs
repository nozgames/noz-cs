//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public class PlatformConfig
{
    public string Title { get; set; } = "Noz Application";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool VSync { get; set; } = true;
    public bool Resizable { get; set; } = true;
}

public interface IPlatform
{
    void Init(PlatformConfig config);
    void Shutdown();

    /// <summary>
    /// Poll platform events and dispatch to handlers.
    /// Returns false if quit was requested.
    /// </summary>
    bool PollEvents();

    void SwapBuffers();

    Vector2 WindowSize { get; }

    /// <summary>
    /// Called when an input event occurs.
    /// </summary>
    event Action<PlatformEvent>? OnEvent;
}
