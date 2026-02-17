# VFX System

Curve-driven 2D particle system with fixed-pool allocation.

## Limits

| Resource   | Max  |
|------------|------|
| Particles  | 4096 |
| Emitters   | 2024 |
| Instances  | 256  |

## Files

| File | Purpose |
|------|---------|
| `noz/engine/src/vfx/Vfx.cs` | Asset definition, binary load/save |
| `noz/engine/src/vfx/VfxSystem.cs` | Runtime: spawn, simulate, render |
| `noz/editor/src/document/VfxDocument.cs` | Text `.vfx` parser, binary export |
| `noz/editor/src/document/VfxEditor.cs` | Editor preview UI |

## Hierarchy

**Instance** (spawned effect) -> **Emitter** (particle generator) -> **Particle**

- Instance holds transform, depth, loop state, version (for handle validation)
- Emitter holds rate, burst, duration, angle, spawn/direction ranges
- Particle holds position, velocity, rotation, lifetime, per-particle curve values

## Handle API

```csharp
VfxHandle handle = VfxSystem.Play(vfx, position, depth);
VfxHandle handle = VfxSystem.Play(vfx, transform, depth);
VfxSystem.Stop(handle);       // stop emitters, let particles finish
VfxSystem.Kill(handle);       // kill immediately
bool playing = VfxSystem.IsPlaying(handle);
VfxSystem.Clear();            // kill everything
```

## Frame Loop

```
Application.RunFrame()
  -> VfxSystem.Update()
       1. UpdateEmitters() - advance time, accumulate rate, spawn particles, restart if looping
       2. SimulateParticles() - advance time, evaluate curves, apply gravity/drag, update position

Game.Update()
  -> VfxSystem.Render()  - evaluate size/opacity/color/rotation curves, build transforms, draw quads
```

## Curve Types

`Linear`, `EaseIn`, `EaseOut`, `EaseInOut`, `Quadratic`, `Cubic`, `Sine`

All particle properties (size, speed, color, opacity, rotation) interpolate from randomized start/end values over normalized lifetime using these curves.

## Physics

```
velocity += gravity * dt
velocity *= (1 - drag * dt)
position += velocity * dt
```

## Rendering

- Renders on `GameLayer.Vfx` (layer 11, highest)
- All particles use the same shader (`GameAssets.Shaders.Texture`) and `Graphics.WhiteTexture`
- Color comes from vertex color via curve evaluation
- Blend mode: Alpha
- Transform: `Scale(size) * Rotation(angle) * Translation(particlePos) * InstanceTransform`
- Each particle is a unit quad (-0.5 to 0.5)

## .vfx Text Format

```ini
[vfx]
duration = [1.0, 3.0]        # effect duration range
loop = true

[emitters]
red                           # emitter names (one per line)
orange

[red]
rate = 10                     # particles/sec
burst = [12, 18]              # initial burst range
duration = 1.0
angle = [60, 120]             # degrees
spawn = [(-8, -6), (8, -4)]  # position range
direction = [(0, -1), (0, -1)]

[red.particle]
duration = [1.0, 2.0]
size = [0.04, 0.12]                        # constant range
speed = [1.0, 5.0] => [0.5, 1.5] : ease_out  # curve with easing
color = #D94A4AFF                          # hex color (or range)
opacity = 1.0 => 0 : ease_in              # curve
gravity = (0, 6.0)
drag = [0.5, 2.0]
rotation = [0, 360]
```

### Value syntax

- Single: `1.0`
- Range: `[min, max]`
- Curve: `start => end : curve_type` (start/end can be single or range)
- Vec2: `(x, y)` or `[(x1,y1), (x2,y2)]`
- Color: `#RRGGBBAA` or `[#color1, #color2]`

## Memory Management

- Fixed-size arrays, boolean validity tracking, linear scan for free slots
- No dynamic allocation at runtime
- Version counter on instances prevents stale handle usage
- Cascading cleanup: particle freed -> emitter particle count decrements -> instance emitter count decrements -> instance freed when zero emitters

## Limitations

- Flat quads only (mesh field unused)
- Single shader for all particles per frame
- No sub-emitters, no collision, no rotation velocity (rotation is interpolated)
- One global pool shared across all effect types
