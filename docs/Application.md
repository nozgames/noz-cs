# Application Lifecycle

## Overview

`Application` is the engine entry point. It initializes all subsystems, runs the game loop, and shuts everything down. Games implement `IApplication` (the "Vtable") to hook into the lifecycle.

## IApplication Interface

```csharp
public interface IApplication
{
    void Init() { }              // Game-specific init (after all engine subsystems ready)
    void Shutdown() { }          // Game-specific cleanup (before engine subsystems shut down)
    void Update();               // Per-frame update
    void FixedUpdate() { }       // Fixed timestep (physics)
    void UpdateUI() { }          // UI layout pass
    void LateUpdate() { }        // After UI, before VFX/render
    void LoadConfig(ApplicationConfig config) { }  // Load saved settings (before platform init)
    void SaveConfig() { }        // Persist settings on shutdown
    void LoadAssets() { }        // Load game assets (after engine assets registered)
    void UnloadAssets() { }      // Release game assets
    void ReloadAssets() { }      // Hot-reload assets (editor)
    bool WantsToQuit() => true;  // Can block quit
    void BeforeQuit() { }        // Last-chance cleanup
}
```

## Init Sequence

`Application.Init(config)` runs this sequence:

```
1. Store config, extract Platform/Graphics/Audio/Vtable
2. Vtable.LoadConfig(config)          ← game reads saved window size, etc.
3. Platform.Init(...)                 ← create window
4. Subscribe to platform events
5. Time.Init()
6. Input.Init()
7. Audio.Init(driver)
8. Graphics.Init(config)
9. RegisterAssetTypes()               ← Animation, Skeleton, Texture, Sprite, Sound, Shader, Font, Vfx, etc.
10. Vtable.LoadAssets()               ← game loads its assets (GameAssets.LoadAssets())
11. Graphics.ResolveAssets()
12. VfxSystem.Init()
13. TextRender.Init()
14. UI.Init()
15. Platform.SetResizeCallback(...)
16. Vtable.Init()                     ← game-specific init (camera, game systems, etc.)
```

## Frame Loop

`Application.Run()` loops calling `RunFrame()` until quit:

```
Time.Update()
Input.BeginFrame() / PollEvents() / Input.Update()
Graphics.BeginFrame()

── Fixed timestep loop ──
    Time.DeltaTime = FixedDeltaTime
    while (Time.ConsumeFixedStep()):
        Vtable.FixedUpdate()       ← physics, player movement, game logic
    Time.DeltaTime = savedDt
── End fixed loop ──

Vtable.Update()                    ← per-frame update
UI.Begin()
    Vtable.UpdateUI()              ← immediate-mode UI layout
UI.End()
Vtable.LateUpdate()                ← post-UI work
VfxSystem.Update()
Cursor.Update()
Graphics.EndFrame()
Platform.SwapBuffers()
```

## Shutdown Sequence

`Application.Shutdown()` tears down in reverse:

```
1. Vtable.Shutdown()         ← game cleanup
2. Vtable.SaveConfig()       ← persist settings
3. Vtable.UnloadAssets()     ← release game assets
4. VfxSystem.Shutdown()
5. UI.Shutdown()
6. TextRender.Shutdown()
7. Graphics.Shutdown()
8. Audio.Shutdown()
9. Input.Shutdown()
10. Time.Shutdown()
11. Platform.Shutdown()      ← destroy window
```

## Platform Variants

### Desktop

```csharp
Application.Init(new ApplicationConfig
{
    Platform = new SDLPlatform(),
    AudioBackend = new SDLAudioDriver(),
    Vtable = new GameVtable(),
    ResourceAssembly = typeof(GameVtable).Assembly,
    Graphics = new GraphicsConfig { Driver = new WebGPUGraphicsDriver() },
    UI = new UIConfig { ... },
});
Application.Run();
Application.Shutdown();
```

Real platform, real graphics (WebGPU), real audio (SDL). Full game loop runs synchronously until quit.

### Web (Blazor)

```razor
<WebApplication Width="1280" Height="720" Vtable="@_vtable" UI="@_uiConfig" />
```

`WebApplication` calls `Application.Init(...)` internally with a WebPlatform and WebAudio driver. Instead of `Application.Run()`, the game loop is driven by the browser's `requestAnimationFrame` — each tick calls `Application.RunFrame()` via JS interop.

### CLI

```csharp
return CommandLineApplication.Run(new CommandLineConfig
{
    Name = "stope",
    Vtable = new GameVtable(),
    Commands = { ... }
}, args);
```

`CommandLineApplication.Run` calls `Application.Init(...)` with null drivers:
- `NullPlatform` — no window, no events
- `NullGraphicsDriver` — all draw calls are no-ops, returns valid handles
- `NullAudioDriver` — all sound calls are no-ops

This means **all engine subsystems initialize normally**: assets load, VfxSystem allocates pools, UI initializes, `Vtable.Init()` runs. Game code works identically — animations play, VFX spawn, sounds fire — they just produce no output.

After the command completes, `Application.Shutdown()` tears everything down.

The `ResourceAssembly` is inferred from `Vtable.GetType().Assembly` so embedded assets (sprites, skeletons, sounds baked into the game DLL) load correctly.

## Asset Loading

Assets are loaded from two sources:
1. **File system**: `Application.AssetPath` (defaults to `cwd/library/`)
2. **Embedded resources**: `Application.ResourceAssembly` — assets baked into the game assembly via MSBuild

`Vtable.LoadAssets()` is called after all asset type handlers are registered, so `Asset.Load(AssetType.X, "name")` works for any engine asset type.

## Null Drivers

All three null drivers live in `noz/platform/cli/`:

| Driver | Behavior |
|--------|----------|
| `NullPlatform` | Returns fixed window size, ignores all calls. `PollEvents()` returns true until `RequestQuit()`. |
| `NullGraphicsDriver` | Returns incrementing handles for textures/meshes/shaders. All draw calls are no-ops. |
| `NullAudioDriver` | `CreateSound` returns a valid handle. `Play` returns 0. `IsPlaying` returns false. Volumes are stored but unused. |
