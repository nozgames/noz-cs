# NoZ Engine - Claude Context

This file provides context for Claude Code when working with the NoZ game engine.

## Architecture Overview

NoZ is a C# game engine with a platform-agnostic core and pluggable backends.

```
nozcs/
  src/                          # Core engine (noz.dll) - NO platform dependencies
    platform/
      IPlatform.cs              # Window, events, timing interface
      IRender.cs                # Render backend interface
      PlatformEvent.cs          # Abstract input events
    render/
      Render.cs                 # Static render API
      Color.cs                  # Color, Color32, Color24 structs
      RenderConfig.cs
      Sprite.cs
    input/
      Input.cs                  # Static input API
      InputCode.cs              # Key/button enums
      InputSet.cs
    math/
      MathEx.cs                 # Game math utilities (Lerp, SmoothDamp, etc.)
    Application.cs              # Main app lifecycle
    ApplicationConfig.cs

  platform/
    sdl/                        # Desktop platform (noz.platform.sdl.dll)
      SDLPlatform.cs            # IPlatform implementation using SDL3
      OpenGLRender.cs           # IRender implementation using OpenGL
    web/                        # Web platform (Blazor WebAssembly)
      WebPlatform.cs            # IPlatform implementation using JS interop
      WebGLRender.cs            # IRender implementation using WebGL
      Game.razor                # Blazor component for game loop
      wwwroot/js/*.js           # JavaScript interop modules

  editor/                       # Editor application
```

## Key Design Decisions

1. **Platform Abstraction**: Core `noz.dll` has zero SDL/OpenGL/browser dependencies
2. **Required Injection**: `Application.Init()` requires `Platform` and `RenderBackend` to be provided
3. **Static APIs**: `Application`, `Render`, `Input` are static classes for easy game code access
4. **Readonly Structs**: `Color`, `Color32`, `Color24` are immutable value types

## Creating a Game Project

Games using NoZ should have this structure:

```
mygame/
  nozcs/                  # This repo as a git submodule

  src/                    # Game library (platform-agnostic)
    mygame.csproj         # References: nozcs/src/noz.csproj
    Game.cs               # Game logic

  desktop/                # Desktop entry point
    mygame.desktop.csproj # References: src + nozcs/platform/sdl
    Program.cs

  web/                    # Web entry point
    mygame.web.csproj     # References: src + nozcs/platform/web (merge into)
    App.razor
```

### Desktop Program.cs Example
```csharp
using noz;

var game = new MyGame();

Application.Init(new ApplicationConfig
{
    Title = "My Game",
    Width = 1280,
    Height = 720,
    Platform = new SDLPlatform(),
    RenderBackend = new OpenGLRender(),
    Vtable = new ApplicationVtable
    {
        Update = game.Update
    }
});

game.Init();
Application.Run();
game.Shutdown();
Application.Shutdown();
```

### Web App.razor Example
```razor
@using noz

<Game Width="1280" Height="720" OnUpdate="@OnGameUpdate" OnInitialize="@OnGameInitialize" />

@code {
    private MyGame _game = new();

    private Task OnGameInitialize()
    {
        _game.Init();
        return Task.CompletedTask;
    }

    private void OnGameUpdate()
    {
        _game.Update();
    }
}
```

### Game Code Example (platform-agnostic)
```csharp
namespace mygame;

public class MyGame
{
    public void Init()
    {
        // Load assets, setup game state
    }

    public void Update()
    {
        // Handle input
        if (Input.WasButtonPressed(InputCode.KeyEscape))
            Application.Quit();

        // Update game logic
        // ...

        // Render
        Render.Clear(Color.Black);
        // Render.Draw(...);
    }

    public void Shutdown()
    {
        // Cleanup
    }
}
```

## Project References

### Game library (mygame/src/mygame.csproj)
```xml
<ItemGroup>
  <ProjectReference Include="..\nozcs\src\noz.csproj" />
</ItemGroup>
```

### Desktop (mygame/desktop/mygame.desktop.csproj)
```xml
<ItemGroup>
  <ProjectReference Include="..\src\mygame.csproj" />
  <ProjectReference Include="..\nozcs\platform\sdl\noz.platform.sdl.csproj" />
</ItemGroup>
```

### Web (mygame/web/mygame.web.csproj)
```xml
<ItemGroup>
  <ProjectReference Include="..\src\mygame.csproj" />
  <ProjectReference Include="..\nozcs\src\noz.csproj" />
</ItemGroup>
```
Note: Web project is Blazor WebAssembly and includes WebPlatform/WebGLRender directly or references them.

## APIs

### Application
- `Application.Init(config)` - Initialize with platform/render backend
- `Application.Run()` - Main loop (desktop only, web uses requestAnimationFrame)
- `Application.Shutdown()` - Cleanup
- `Application.Quit()` - Request exit
- `Application.WindowSize` - Current window size

### Render
- `Render.Clear(color)` - Clear screen
- `Render.BeginFrame()` / `EndFrame()` - Frame boundaries
- `Render.SetViewport(x, y, w, h)` - Set viewport
- `Render.Draw(sprite)` - Draw sprite (TODO)

### Input
- `Input.IsButtonDown(code)` - Check if held
- `Input.WasButtonPressed(code)` - Check if just pressed
- `Input.WasButtonReleased(code)` - Check if just released
- `Input.MousePosition` - Current mouse position
- `Input.IsShiftDown()`, `IsCtrlDown()`, `IsAltDown()` - Modifier helpers

### MathEx
- `MathEx.Lerp(a, b, t)` - Linear interpolation
- `MathEx.InverseLerp(a, b, value)` - Reverse lerp
- `MathEx.Remap(value, fromMin, fromMax, toMin, toMax)` - Range remapping
- `MathEx.SmoothDamp(...)` - Smooth following
- `MathEx.DeltaAngle(current, target)` - Shortest angle difference
- `MathEx.Repeat(t, length)` - Modulo that works with negatives
- `MathEx.Approximately(a, b)` - Float comparison with epsilon

### Color
- `new Color(r, g, b, a)` - Float components (0-1)
- `new Color32(r, g, b, a)` - Byte components (0-255)
- `Color.Black`, `Color.White`, `Color.Red`, etc. - Predefined colors
- `color.WithAlpha(a)` - Return copy with new alpha
- `Color.Mix(a, b, t)` - Interpolate colors

## Build Commands

```bash
# Build core engine
dotnet build src/noz.csproj

# Build SDL platform
dotnet build platform/sdl/noz.platform.sdl.csproj

# Build web platform
dotnet build platform/web/platform.web.csproj

# Run editor
dotnet run --project editor

# Run web (in platform/web)
dotnet run --project platform/web
```
