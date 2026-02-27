# NoZ VFX System

Curve-driven 2D particle system with fixed-pool allocation. Effects are composed of one or more emitters, each spawning rectangular particles with configurable physics, color, size, and opacity curves.

## System Limits

| Resource   | Max  |
|------------|------|
| Particles  | 4096 |
| Emitters   | 2024 |
| Instances  | 256  |

## Files

| File | Purpose |
|------|---------|
| `noz/engine/src/vfx/Vfx.cs` | Asset definition, binary load/save (version 4) |
| `noz/engine/src/vfx/VfxSystem.cs` | Runtime: spawn, simulate, render |
| `noz/editor/src/document/VfxDocument.cs` | Text `.vfx` parser, binary export |
| `noz/editor/src/document/VfxEditor.cs` | Editor preview UI |

## Hierarchy

**Instance** (spawned effect) → **Emitter** (particle generator) → **Particle**

- Instance holds transform, depth, layer, loop state, version (for handle validation)
- Emitter holds rate, burst, duration, angle, spawn/direction ranges, worldSpace flag
- Particle holds position, velocity, rotation, lifetime, per-particle curve start/end values

## Runtime API

```csharp
// Play at position (returns handle for control)
VfxHandle handle = VfxSystem.Play(vfx, position);
VfxHandle handle = VfxSystem.Play(vfx, position, depth: 0f, layer: 0);
VfxHandle handle = VfxSystem.Play(vfx, transform, depth: 0f, layer: 0);

// Control
VfxSystem.Stop(handle);              // Stop emitting, let particles fade out
VfxSystem.Kill(handle);              // Immediately remove all particles
bool active = VfxSystem.IsPlaying(handle);

// Move a running effect
VfxSystem.SetTransform(handle, newPosition);
VfxSystem.SetTransform(handle, newTransform);

// Clear everything
VfxSystem.Clear();
```

### Initialization

```csharp
// Set the shader used for all particles (required, do once)
VfxSystem.Shader = GameAssets.Shaders.Texture;

// Load VFX asset
var vfx = Asset.Load(AssetType.Vfx, "effect_name") as Vfx;
```

### Frame Loop

```
Application.RunFrame()
  → VfxSystem.Update()
       1. UpdateEmitters() - advance time, accumulate rate, spawn particles, restart if looping
       2. SimulateParticles() - advance time, evaluate curves, apply gravity/drag, update position

Game.DrawScene()
  → VfxSystem.Render()       // draw all particles
  → VfxSystem.Render(layer)  // draw only particles on a specific layer
```

### Rendering

- All particles use the same shader (`VfxSystem.Shader`) and `Graphics.WhiteTexture`
- Blend mode: Alpha
- Each particle is a unit quad (-0.5 to 0.5), transformed: `Scale(size) * Rotation(angle) * Translation(position) * InstanceTransform`
- Color comes from vertex color via curve evaluation

## .vfx Text Format

```ini
[vfx]
duration = 1.0
loop = false

[emitters]
debris
sparks

[debris]
rate = 0
burst = [3, 5]
duration = 1.0
angle = [0, 360]
spawn = [(-0.3, -0.3), (0.3, 0.3)]
worldSpace = false

[debris.particle]
duration = [0.4, 0.7]
size = [0.15, 0.25]=>[0.0, 0.05]:easeout
speed = [2, 6]=>[0, 1]:easeout
color = rgb(255, 200, 50)
opacity = 1.0=>0.0:linear
gravity = (0, 15)
drag = 2
rotation = [0, 360]
```

### Top-Level `[vfx]`

| Property | Syntax | Default | Description |
|----------|--------|---------|-------------|
| `duration` | `float` or `[min, max]` | `1.0` | Total effect duration in seconds. |
| `loop` | `true`/`false` | `false` | Whether the effect restarts after finishing. |

### Emitter List `[emitters]`

List emitter names, one per line. Each name must have a matching `[name]` and `[name.particle]` section.

### Emitter Section `[name]`

| Property | Syntax | Default | Description |
|----------|--------|---------|-------------|
| `rate` | `int` or `[min, max]` | `0` | Particles emitted per second (0 = burst only). |
| `burst` | `int` or `[min, max]` | `0` | Particles spawned immediately at start. |
| `duration` | `float` or `[min, max]` | `1.0` | How long this emitter runs (seconds). |
| `angle` | `float` or `[min, max]` | `[0, 360]` | Emission angle range in degrees. |
| `spawn` | `(x, y)` or `[(v1), (v2)]` | `(0, 0)` | Spawn position offset (randomized within range). |
| `direction` | `(x, y)` or `[(v1), (v2)]` | none | Override emission direction (if set, overrides `angle`). |
| `worldSpace` | `true`/`false` | `true` | If true, particles spawn in world coords; if false, particles stay relative to instance transform. |

### Particle Section `[name.particle]`

| Property | Syntax | Default | Description |
|----------|--------|---------|-------------|
| `duration` | `float` or `[min, max]` | `1.0` | Particle lifetime in seconds. |
| `size` | float curve | `1.0` | Particle size (world units). |
| `speed` | float curve | `0` | Particle speed (world units/sec). |
| `color` | color curve | `white` | Particle color. |
| `opacity` | float curve | `1.0` | Particle opacity (0.0-1.0). |
| `gravity` | `(x, y)` | `(0, 0)` | Gravity acceleration. Y-down: `(0, 15)` = downward, `(0, -3)` = upward. |
| `drag` | `float` or `[min, max]` | `0` | Velocity damping factor. |
| `rotation` | float curve | `0` | Particle rotation in degrees. |

## Value Syntax

### Simple Values

- **Single float**: `0.5`
- **Float range**: `[0.3, 0.8]` — random value between min and max
- **Single int**: `5`
- **Int range**: `[3, 8]`
- **Vector2**: `(x, y)` — e.g. `(0, 15)`
- **Vec2 range**: `[(x1,y1), (x2,y2)]` — e.g. `[(-0.3, -0.3), (0.3, 0.3)]`

**WARNING**: Vec2 properties (`spawn`, `direction`, `gravity`) use `(x, y)` or `[(v1), (v2)]` range syntax only. They do **NOT** support the `=>` curve syntax. Writing `(0,0)=>(1,1)` will silently parse as the constant `(0,0)` — the `=>(1,1)` part is ignored.

### Float Curves

Animate a float value over the particle's lifetime. Works for `size`, `speed`, `opacity`, `rotation`.

```
start=>end:curvetype
```

Both start and end can be single values or ranges:
- `0.5=>[0.0, 0.1]:easeout` — fixed start, random end
- `[0.15, 0.25]=>[0.0, 0.05]:easeout` — random start, random end
- `1.0=>0.0:linear` — linear fade
- `0.5` — constant value (no animation)

### Color Values

```
color                              # constant
color_start=>color_end:curvetype   # transition
[color1, color2]                   # random pick
```

Color formats:
- **Named**: `white`, `black`, `red`, `green`, `blue`, `yellow`, `cyan`, `magenta`, `gray`/`grey`, `orange`, `pink`, `purple`, `brown`, `transparent`
- **RGB function**: `rgb(255, 200, 50)`
- **RGBA function**: `rgba(255, 200, 50, 128)`
- **Hex short**: `#RGB`
- **Hex**: `#RRGGBB`
- **Hex with alpha**: `#RRGGBBAA`

Examples:
- `rgb(255, 200, 50)` — constant gold
- `rgb(255, 160, 30)=>rgb(255, 60, 20):linear` — orange to red transition
- `[rgb(180, 160, 130), rgb(200, 180, 155)]` — random color in range

### Curve Types

| Name | Syntax | Formula |
|------|--------|---------|
| Linear | `linear` | `t` |
| Ease In | `easein` | `t²` |
| Ease Out | `easeout` | `1-(1-t)²` |
| Ease In-Out | `easeinout` | `t<0.5 ? 2t² : 1-2(1-t)²` |
| Quadratic | `quadratic` | `t²` (same as easein) |
| Cubic | `cubic` | `t³` |
| Sine | `sine` | `sin(t * π/2)` |
| Bell | `bell` | `sin(t * π)` — peaks at 1.0 at t=0.5, ideal for fade-in-then-fade-out |
| Bezier | `bezier(y0, y1, y2, y3)` | Cubic bezier with 4 Y control points at X=0, 0.33, 0.66, 1.0 |

**Bezier**: 4 Y control points (NOT CSS-style). Fixed X positions. Parsed as a Vec4 via `ExpectVec4`, so always provide 4 values: `bezier(0, 1, 1, 0)`.

## Physics

```
velocity += gravity * dt
velocity *= (1 - drag * dt)
position += velocity * dt
```

Speed curve controls the magnitude of velocity — direction is preserved. Gravity and drag modify velocity directly each frame.

## World Space vs Local Space

Controlled by the `worldSpace` emitter property (default: `true`).

| | `worldSpace = true` | `worldSpace = false` |
|---|---|---|
| Spawn | `Vector2.Transform(offset, instanceTransform)` — full transform applied | `Vector2.TransformNormal(offset, instanceTransform)` — rotation/scale only |
| Movement | Particles move independently in world coords | Particles move relative to instance |
| Render | Particle transform used directly | `particleTransform * instanceTransform` applied |
| Use case | Explosions, impacts (particles stay where spawned) | Trails, auras (particles follow the source) |

Use `VfxSystem.SetTransform(handle, position)` to move the instance transform for local-space effects.

## Memory & Lifecycle

- Fixed-size arrays, boolean validity tracking, linear scan for free slots
- No dynamic allocation at runtime — all pools pre-allocated
- Version counter on instances prevents stale handle usage
- Cascading cleanup: particle freed → emitter particle count decrements → instance emitter count decrements → instance freed when all emitters done

## Limitations

- Flat colored quads only (no textures, no meshes)
- Single shader for all particles
- No sub-emitters, no collision, no angular velocity (rotation is curve-interpolated, not physics-driven)
- One global pool shared across all effect types

## Common Pitfalls

- **Vec2 properties don't support curves**: `gravity`, `spawn`, `direction` only accept `(x,y)` or `[(v1),(v2)]`. The `=>` operator is silently ignored.
- **Tokenizer `(` is not a delimiter**: NoZ `Tokenizer` treats `(...)` as vector syntax. When parsing `bezier(0,1,1,0)`, the parser uses `ExpectIdentifier` then `ExpectVec4`, not `ExpectDelimiter('(')`.
- **Binary reimport required**: After changing `.vfx` text files, reimport in the editor to regenerate binary in `library/vfx/`. The game loads the binary format, not the text.
- **Curve type names are lowercase, no underscores**: `easeout` not `ease_out`, `easein` not `ease_in`.

## Examples

### Burst Explosion
```ini
[vfx]
duration = 1
loop = false

[emitters]
debris

[debris]
rate = 0
burst = [3, 5]
duration = 1
angle = [0, 360]

[debris.particle]
duration = [0.4, 0.7]
size = [0.15, 0.25]=>[0.0, 0.05]:easeout
speed = [2, 6]=>[0, 1]:easeout
color = rgb(255, 200, 50)
opacity = 1.0=>0.0:linear
gravity = (0, 15)
drag = 2
```

### Ambient Dust (looping, world-space)
```ini
[vfx]
duration = 10
loop = true

[emitters]
dust

[dust]
rate = 3
burst = 0
duration = 10
angle = [0, 360]
spawn = [(-5, -8), (5, 8)]
worldSpace = true

[dust.particle]
duration = [3, 6]
size = [0.02, 0.06]
speed = [0.02, 0.08]
color = [rgb(180, 160, 130), rgb(200, 180, 155)]
opacity = 0=>1:bell
gravity = (0, -0.2)
drag = 1
```

### Rocket Trail (local-space, follows source)
```ini
[vfx]
duration = 2
loop = true

[emitters]
fire
smoke

[fire]
rate = 40
burst = 0
duration = 2
direction = [(0, 1), (0, 1)]
worldSpace = false

[fire.particle]
duration = [0.1, 0.3]
size = [0.08, 0.15]=>[0.02, 0.05]:easeout
speed = [3, 6]=>[1, 2]:easeout
color = rgb(255, 160, 30)=>rgb(255, 60, 20):linear
opacity = 1.0=>0.0:easeout

[smoke]
rate = 15
burst = 0
duration = 2
direction = [(0, 1), (0, 1)]
worldSpace = false

[smoke.particle]
duration = [0.5, 1.0]
size = [0.05, 0.1]=>[0.15, 0.25]:easeout
speed = [1, 3]=>[0.2, 0.5]:easeout
color = rgb(100, 100, 100)
opacity = 0.6=>0.0:easeout
```
