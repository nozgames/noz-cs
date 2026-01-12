//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public static class Render
{
    private static RenderConfig _config = null!;
    private static IRender _backend = null!;
    private static RenderCommand[]? _commands;

    public static IRender Backend => _backend;

    public static void Init(RenderConfig? config, IRender backend)
    {
        _config = config ?? new RenderConfig();
        _backend = backend;
        _commands = new RenderCommand[_config.MaxCommands];

        _backend.Init(new RenderBackendConfig
        {
            VSync = _config.Vsync,
            MaxCommands = _config.MaxCommands
        });
    }

    public static void Shutdown()
    {
        _backend.Shutdown();
    }

    public static void BeginFrame()
    {
        _backend.BeginFrame();
    }

    public static void EndFrame()
    {
        _backend.EndFrame();
    }

    public static void Clear(Color color)
    {
        _backend.Clear(color);
    }

    public static void SetViewport(int x, int y, int width, int height)
    {
        _backend.SetViewport(x, y, width, height);
    }

    public static void BindTransform(in Matrix3x2 transform)
    {
        // TODO: Push transform to command buffer
    }

    public static void Draw(Sprite sprite)
    {
        // TODO: Add draw command to buffer
    }

    public static void Draw(Sprite sprite, in Matrix3x2 transform)
    {
        // TODO: Add draw command with transform to buffer
    }
}
