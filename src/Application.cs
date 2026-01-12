//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public static class Application
{
    private static IPlatform _platform = null!;
    private static IRender _renderBackend = null!;
    private static bool _running;

    public static ApplicationConfig Config { get; private set; } = null!;
    public static IPlatform Platform => _platform;
    public static IRender RenderBackend => _renderBackend;
    public static Vector2 WindowSize => _platform.WindowSize;

    public static void Init(ApplicationConfig config)
    {
        Config = config;

        // Platform and render backend must be provided
        _platform = config.Platform ?? throw new ArgumentNullException(nameof(config.Platform),
            "Platform must be provided. Use SDLPlatform for desktop or WebPlatform for web.");

        _renderBackend = config.RenderBackend ?? throw new ArgumentNullException(nameof(config.RenderBackend),
            "RenderBackend must be provided. Use OpenGLRender for desktop or WebGLRender for web.");

        // Initialize platform
        _platform.Init(new PlatformConfig
        {
            Title = config.Title,
            Width = config.Width,
            Height = config.Height,
            VSync = config.VSync,
            Resizable = config.Resizable
        });

        // Subscribe to platform events
        _platform.OnEvent += Input.ProcessEvent;

        // Initialize subsystems
        Input.Init();
        Render.Init(config.Render, _renderBackend);

        _running = true;
    }

    public static void Run()
    {
        while (_running)
        {
            if (!_platform.PollEvents())
            {
                _running = false;
                continue;
            }

            Input.Update();

            Render.BeginFrame();
            Config.Vtable.Update?.Invoke();
            Render.EndFrame();

            _platform.SwapBuffers();
        }
    }

    public static void Shutdown()
    {
        Render.Shutdown();
        Input.Shutdown();

        _platform.OnEvent -= Input.ProcessEvent;
        _platform.Shutdown();

        _running = false;
    }

    public static void Quit()
    {
        _running = false;
    }
}
