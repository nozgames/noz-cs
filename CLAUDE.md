# NOZ Engine

2D game engine written in C#. Targets .NET 10. Platforms: Desktop (SDL3 + WebGPU), Web (Blazor + WebGPU).

## Project Structure

```
noz/
├── engine/
│   ├── src/                    # Core engine source
│   │   ├── Application.cs      # Entry point, game loop
│   │   ├── graphics/           # Rendering, batching, shaders, meshes, cameras
│   │   ├── ui/                 # Immediate-mode UI framework
│   │   ├── input/              # Input handling, scopes
│   │   ├── audio/              # Audio playback
│   │   ├── physics/            # Collision, raycasts
│   │   ├── vfx/                # Particle system
│   │   ├── collections/        # NativeArray<T>, UnsafeList<T>, UnsafeSpan<T>, UnsafeRef<T>
│   │   ├── math/               # Noise, Rect, MathEx
│   │   ├── network/            # Networking interface
│   │   └── platform/           # IPlatform, IGraphicsDriver, IAudioDriver abstractions
│   └── assets/shader/          # WGSL shaders (sprite, text, ui, texture, composite)
├── editor/
│   ├── src/
│   │   ├── EditorApplication.cs
│   │   ├── document/           # Per-asset-type editors (Sprite, Texture, Font, Animation, Atlas, Vfx, etc.)
│   │   ├── tool/               # Move, Rotate, Scale, Pen, Knife, BoxSelect, etc.
│   │   ├── msdf/               # MSDF generation (sprites and fonts)
│   │   ├── TTF/                # TrueType font parsing
│   │   └── shape/              # Vector shape editing
│   └── assets/                 # Editor UI assets
├── platform/
│   ├── desktop/                # SDL3 window/input/audio (ppy.SDL3-CS)
│   ├── webgpu/                 # WebGPU graphics driver (Silk.NET.WebGPU)
│   └── web/                    # Blazor WebAssembly platform
├── generators/                 # C# source generators
└── docs/                       # Architecture docs
```

## Build & Run

```bash
dotnet build editor/editor.csproj       # Build editor
dotnet build src/noz.csproj             # Build engine only
dotnet run --project editor/editor.csproj  # Run editor
```

Game projects reference noz as a git submodule. Editor `--init --project .` scaffolds a new game.

## Code Style

- **Namespaces**: File-scoped (`namespace NoZ;`). Engine = `NoZ`, Editor = `NoZ.Editor`, Platform = `NoZ.Platform.*`
- **Static partial classes** for systems: `Graphics`, `UI`, `Input`, `Audio`, `Time`, `Application`
- **Primary constructors** on structs: `public readonly struct Color32(byte r, byte g, byte b, byte a = 255)`
- **Expression-bodied members** for simple properties/methods
- **`var`** used liberally
- **Readonly structs** and **ref structs** for value types
- **Unsafe code** with pointers in performance-critical paths (Graphics, collections)
- **Nullable reference types** enabled (`?` suffix)
- **Init-only properties** (`{ get; init; }`) for config objects
- Use `Log.Info`, `Log.Warning`, `Log.Error` for console output, not `Console.WriteLine`

## Trust Zones

Changes to these areas require the indicated plan level. The engine is a shared library — noz-cs is consumed by yesz, DnD, NeuralRTS, and Evolve. A regression here breaks all four games.

| Path | Level | Reason |
|------|-------|--------|
| `engine/src/graphics/` | **L3** | Sort pipeline, batching, and shader binding are the rendering foundation. A subtle sort-key bug or state leak corrupts every frame in every game. Requires explicit approval before any change. |
| `engine/src/ui/` | **L2** | Layout engine has non-obvious semantics (EdgeInsets TLBR order, margin vs. alignment interaction, popup input routing). Easy to introduce `IndexOutOfRangeException` or silent layout regressions. |
| `engine/src/vfx/` | **L2** | Fixed-size pools (4096 particles, 2024 emitters, 256 instances). Exceeding capacity silently drops effects; any new allocation in the update path introduces GC pressure. |
| `engine/src/collections/` | **L2** | `NativeArray<T>`, `UnsafeList<T>`, `UnsafeSpan<T>`, `UnsafeRef<T>` — raw unmanaged memory. Bugs here (resize during iteration, bounds errors) are silent data corruption, not exceptions. |
| `engine/src/audio/` | **L1** | Stable; IAudioDriver abstraction is well-bounded. Changes mostly touch the driver adapter, not the engine core. |
| `engine/src/input/` | **L1** | Scope stack and button states are self-contained. Low blast radius. |
| `engine/src/physics/` | **L1** | Pure math — triangle overlap, raycasts, circle casts. No shared mutable state. |
| `engine/src/math/` | **L1** | Utility math (Noise, Rect, MathEx). No side effects. |
| `engine/src/platform/` | **L1** | Interface definitions only. Implementations live in `platform/`. |
| `platform/` | **L2** | Platform implementations (SDL3, WebGPU, Blazor). Bugs here are platform-specific, but WebGPU context corruption is hard to recover from. |
| `editor/` | **L1** | Editor-only code. Does not ship in games; blast radius is developer workflow only. |

## Performance Budget

These are non-negotiable constraints for shipping quality on reference hardware (desktop RTX-class GPU). Any new code that violates these budgets is a regression, not a trade-off.

| Metric | Target | Notes |
|--------|--------|-------|
| Frame time (total) | **< 11ms** | 90 FPS on reference hardware. Profile before and after any change to the hot path. |
| `Graphics.BeginFrame` | **< 2ms** | Includes state reset, render-texture pool flush, and time update. Must not allocate. |
| UI layout pass (`UI.End`) | **< 3ms** | Covers measure + layout + draw command generation for all elements. 4096-element cap enforces this. |
| VFX system update | **< 2ms** | 4096 particles × emitter tick. Fixed-size pools prevent allocation; keep it that way. |
| Main loop hot path | **Zero allocations after warmup** | The render loop must not trigger the GC. Any `new` in `Update()`, `UpdateUI()`, `LateUpdate()`, or any Graphics/UI draw call is a regression. |
| Sort+batch creation | **Object pooling only** | `NativeArray<DrawCommand>` and `NativeArray<Batch>` are pre-allocated. Do not introduce `List<T>`, LINQ, or `new DrawCommand()` here. |
| Vertex/index buffers | **Pre-allocated, no resize** | Graphics: 65,536 vertices / 196,608 indices allocated at `Graphics.Init`. UI: 16,384 vertices / 32,768 indices allocated at `UI.Init`. Hitting either cap drops draws silently — budget your geometry. |

To verify: run with `NOZ_GRAPHICS_DEBUG` defined and check frame timing with `Application.FrameTime`. For GC, attach a profiler and confirm Gen0 collections stop after the first few frames.

## DO NOT

- Add XML documentation headers (`/// <summary>`, etc.) to functions or types
- Use `Console.WriteLine` — use `Log.*` instead
- Use `Dictionary<K,V>`, `HashSet<T>`, `List<T>`, LINQ, or any managed collection in `engine/` or `editor/` code — use `NativeArray<T>`, `UnsafeSpan<T>`, flat arrays with `const int Max___`, and index-based references instead
- Design data structures from scratch — find the nearest existing analog in the codebase (VfxSystem for pools, Shape for sidecar buffers, Graphics for sort keys) and match its idiom exactly
- Introduce `new` allocations in any code path that runs per-frame — pre-allocate at Init, reuse forever, silent-drop on overflow

## Core Systems

### Application & Game Loop (`engine/src/Application.cs`)

Games implement `IApplication` with virtual methods: `Update()`, `UpdateUI()`, `LateUpdate()`, `LoadConfig()`, `LoadAssets()`, `WantsToQuit()`, etc.

Frame loop: `Time.Update → Input.BeginFrame → PollEvents → Input.Update → PreFrame → Graphics.BeginFrame → BeginFrame → Update() → UI.Begin → UpdateUI() → UI.End → LateUpdate() → VfxSystem.Update → Cursor.Update → Graphics.EndFrame`

### Graphics (`engine/src/graphics/`)

Sort-based batched rendering. Key files: `Graphics.cs`, `Graphics.State.cs`, `Graphics.Draw.cs`.

- **Sort key** (64-bit): `Pass(4) | Layer(12) | Group(16) | Order(16) | Index(16)` — built via `MakeSortKey()`
- Commands sorted via `_commands.AsSpan().Sort()`, then `CreateBatches()` merges adjacent commands with matching state
- `SortKeyMergeMask = 0x7FFFFFFFFFFF0000` masks out Index bits for merge checks
- **Render passes**: `RenderTexture = 0` (offscreen), `Scene = 1` (main framebuffer)
- **State management**: Single authority (driver `CachedState`), no redundant diffing. Driver resets state on every pass boundary.
- **Vertex types**: `MeshVertex` (standard), `UIVertex` (UI-specific format)
- **Bone texture**: 128 width x 1024 rows, 64 bones x 2 texels per bone (3x2 affine transform)
- **Limits**: 65536 vertices, 196608 indices, 8 texture slots, 3 max render passes
- **Blend modes**: None, Alpha, Additive, Multiply, Premultiplied
- **Default draw order**: containers=0, images=1 (`ImageStyle`), labels=2 (`LabelStyle`) — groups same-shader draws
- Viewport clamping uses current RT dimensions, not always surface size

### UI System (`engine/src/ui/`)

Immediate-mode hierarchical UI built on `ElementTree`. Facade: `UI.cs`. Core: `ElementTree.cs` (partial class split across files).

**Key files:**
| File | Contents |
|------|----------|
| `ElementTree.cs` | Enums, structs, fields, Init/Shutdown, Begin/End, element allocation |
| `ElementTree.API.cs` | Public element creation (Size, Padding, Fill, Row, Column, Label, Image, Scene, etc.) |
| `ElementTree.Widgets.cs` | Widget state, focus/capture, Button, Toggle, Slider |
| `ElementTree.Layout.cs` | FitAxis, LayoutAxis, GetElementSize, UpdateTransforms |
| `ElementTree.Input.cs` | HandleInput, popup auto-close, scrollable input, cursor |
| `ElementTree.Draw.cs` | Draw, DrawElement, DrawLabel, DrawImage, DrawScene, DrawScrollbar, debug dump |
| `ElementTree.EditableText.cs` | TextBox/TextArea editable text widgets |
| `UI.cs` | Public facade — delegates to ElementTree, manages camera, text buffer |
| `UI.Draw.cs` | `DrawTexturedRect` helper, mesh flush (used by ElementTree) |

- **Element types**: Widget, Size, Padding, Fill, Border, Margin, Row, Column, Flex, Align, Clip, Spacer, Opacity, Label, Image, EditableText, Popup, Cursor, Transform, Grid, Scene, Scrollable
- **Layout**: Flexbox-inspired axis-independent layout. Size modes: Default, Percent, Fixed, Fit. Alignment: Min, Center, Max.
- **Building pattern**: `ElementTree.BeginSize(...)` ... `ElementTree.EndSize()`. UI facade wraps with style structs.
- **Input**: `UI.IsHovered()`, `UI.WasPressed()`, `UI.HasFocus()`, `UI.SetFocus()` — all delegate to ElementTree
- **Input inside popups**: Use `UI.IsDown()` / `UI.WasPressed()` instead of `Input.IsButtonDown()`. When popups are open, the UI system consumes the mouse button via `Input.ConsumeButton()`, making raw `Input.IsButtonDown()` return false. `UI.IsDown()` checks the pre-consumption state.
- **Element positioning**: During the UI build phase, the current frame's elements have uninitialized `WorldToLocal` matrices (lazy-computed on first access for input). Use `UI.GetElementWorldRect(id)` to get the previous frame's laid-out rect.
- **EdgeInsets constructor**: `EdgeInsets(top, left, bottom, right)` — note the TLBR order, **not** TRBL. `L` is the 2nd parameter, `R` is the 4th.
- **Rendering**: Custom `UIVertex` mesh, renders on layer 12. Commands with custom meshes need contiguous index merging.
- **Memory**: 64KB element buffer (rebuilt each frame), 128KB double-buffered state pools (widget persistence), 375KB widget state array (32000 IDs)
- **Limits**: 65535 bytes element tree, 64-depth stack, 4 popups, 32000 widget IDs
- **Scaling**: `UIScaleMode` with reference resolution for responsive design

### Asset System (`engine/src/Asset.cs`, `AssetDef.cs`)

- **Types**: Texture, Sprite, Animation, Skeleton, Font, Shader, Sound, Atlas, Vfx, Lua, Event, Bin
- **Loading**: `Asset.Load<T>(type, name)` — platform stream → embedded resource fallback → per-type deserialization → registry cache
- **Binary format**: Header (4-byte signature + type + version + flags) then type-specific data
- **Key asset classes**: `Texture`, `Shader`, `Font`, `Sprite`, `Atlas`, `Sound`, `Animation`, `Skeleton`, `Vfx`

### Input System (`engine/src/input/Input.cs`)

- **Button states**: Physical, Logical, Pressed, Released, Repeat, Consumed
- **Scoped input**: Stack-based scopes for popup priority (prevents input leakage)
- **Repeat timing**: 0.4s initial delay, 0.05s repeat interval
- **Input codes**: `InputCode` enum covers keyboard, mouse, gamepad

### Audio (`engine/src/audio/Audio.cs`)

- `IAudioDriver` abstraction. `Audio.Play()` with volume, pitch, loop. `SoundHandle` for instance control.
- Music track with cross-fade. Volume hierarchy: Master, Sound, Music.

### VFX System (`engine/src/vfx/`)

- **Pools**: 4096 particles, 2024 emitters, 256 instances (fixed-size, no GC)
- **Hierarchy**: Instance → Emitter → Particle
- **Curves**: Linear, EaseIn/Out, Quadratic, Cubic, Sine
- **Text format**: `.vfx` files with curve syntax (`start => end : ease_type`), parsed by editor, exported as binary
- **API**: `VfxSystem.Play/Stop/Kill/IsPlaying/Clear`, returns `VfxHandle`
- Renders on GameLayer.Vfx (layer 11) using single shader + WhiteTexture + vertex color
- Docs: `noz/docs/VfxSystem.md`

### Physics (`engine/src/physics/`)

Triangle overlap, line intersection, point-in-polygon, raycast, circle cast, collision bounds.

### Animation & Skeleton (`engine/src/graphics/Animation.cs`, `Skeleton.cs`, `Animator.cs`)

- Keyframe-based bone animation with easing/interpolation
- `Animator`: play/pause/stop, speed multiplier, event callbacks
- `Skeleton`: bone hierarchy with parent-child transforms
- GPU bone lookup via bone texture

## Shaders

WGSL (WebGPU Shading Language). Located in `engine/assets/shader/`.

| Shader | Purpose |
|--------|---------|
| `sprite.wgsl` | Standard sprite rendering with bone transforms, UV animation, texture array, vertex color |
| `sprite_sdf.wgsl` | SDF sprite rendering — median(r,g,b) reconstruction with adaptive antialiasing |
| `text.wgsl` | SDF text rendering with outline support |
| `texture.wgsl` | Simple texture display |
| `texture_sdf.wgsl` | SDF texture display (editor single-atlas path) |
| `ui.wgsl` | UI elements with border radius, shadows, gradients |
| `composite.wgsl` | Post-processing / screen-space effects |

Globals uniform: `projection: mat4x4<f32>`, `time: f32`.

## Platform Layer

- **IPlatform** (`engine/src/platform/IPlatform.cs`): Window, events, display scale, clipboard, cursor, persistent data, URL open
- **IGraphicsDriver**: GPU resources, render passes, state management, shader compilation
- **Desktop** (`platform/desktop/`): SDL3 via `ppy.SDL3-CS`. Windows networking via Winsock.
- **WebGPU** (`platform/webgpu/`): `Silk.NET.WebGPU` + Dawn extensions + wgpu native
- **Web** (`platform/web/`): Blazor, WebAudio, LocalStorage, JS interop for WebGPU

## Editor (`editor/src/`)

- **Document editors**: Texture, Sprite, Font, Skeleton, Animation, Atlas, Shader, Sound, Vfx
- **Tools**: Move, Rotate, Scale, Pen, Knife, BoxSelect, SpriteSelect, BoneSelect, Rename, Curve
- **Font import**: TTF parsing (`TTF/TrueTypeFont.Reader.cs`) → SDF generation (`msdf/Msdf.Font.cs`) → binary font asset (RGBA8 atlas)
- **SDF generation**: Multi-channel signed distance fields (MSDF algorithm) for sprites and fonts. Port of msdfgen by Viktor Chlumsky. See `docs/sdf.md`.
- **Asset pipeline**: Source files → Importer → binary assets. AtlasManager for texture packing. AssetManifest for registry.
- **Project init**: `--init --project .` scaffolds game project from embedded templates
- **Style**: `EditorStyle.cs` for editor UI theme

## Dependencies

| Package | Project | Purpose |
|---------|---------|---------|
| `SixLabors.ImageSharp` | Editor | Image processing |
| `ppy.SDL3-CS` | Desktop | Window, input, audio |
| `Silk.NET.WebGPU` | WebGPU | GPU bindings |
| `Silk.NET.WebGPU.Extensions.Dawn` | WebGPU | Dawn backend |
| `Silk.NET.WebGPU.Native.WGPU` | WebGPU | wgpu native lib |
| `Microsoft.AspNetCore.Components.Web` | Web | Blazor framework |

Engine itself has zero external dependencies.

## HecateMCP

Before modifying any C# file, call `decide` to get constraints, patterns, and anti-patterns for that file. Follow the verdict's constraints and avoid its anti-patterns. Use discovered patterns as templates for new code.

- **project**: `C:/Users/kmgsp/Documents/git/noz-cs/noz.sln`
- **intent**: describe what you're about to change and why
- **file_path**: relative path from project root (e.g., `engine/src/graphics/Graphics.Draw.cs`)

The verdict includes:
- **Role**: resolved from file path (runtime, editor, platform, general) — determines which rules apply
- **Constraints**: rules you must follow (e.g., zero allocations on hot paths)
- **Anti-patterns**: things you must not do (e.g., managed collections in hot paths)
- **Patterns**: idioms found in the file and similar files — match them in new code
- **Trust level**: L1 (standard), L2 (caution), L3 (zero tolerance) — match to plan level
- **Nearest analogs**: similar files to use as templates

Config lives in `.hecate.json` at project root. Roles are scoped by path — `engine/src/**` = runtime (zero-alloc, hot-path detection), `editor/**` = editor (relaxed), `platform/**` = platform (thread-safe).
