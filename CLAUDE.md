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

Immediate-mode hierarchical UI. Key files: `UI.cs`, `UI.Layout.cs`, `UI.Draw.cs`, `UI.Input.cs`, `UI.Measure.cs`.

#### Element Types (16)

| Type | Purpose | Style Struct |
|------|---------|-------------|
| Container | Base layout, overlapping children via Align+Margin | `ContainerStyle` |
| Column | Vertical sequential layout | `ContainerStyle` |
| Row | Horizontal sequential layout | `ContainerStyle` |
| Flex | Distributes remaining space in Row/Column (weight-based) | float weight |
| Spacer | Fixed-size gap in Row/Column | float size |
| Grid | Multi-column grid with virtualization | `GridStyle` |
| Label | Text rendering with overflow modes | `LabelStyle` |
| Image | Sprite/texture display with stretch modes | `ImageStyle` |
| TextBox | Single-line text input | `TextBoxStyle` |
| TextArea | Multi-line text input | `TextAreaStyle` |
| Scrollable | Scrolling container with scrollbar | `ScrollableStyle` |
| Popup | Floating overlay with anchor positioning | `PopupStyle` |
| Scene | Embedded game viewport via RenderTexture | `SceneStyle` |
| Transform | 2D affine transform wrapper (translate/rotate/scale) | `TransformStyle` |
| Opacity | Opacity multiplier for subtree | float opacity |
| Cursor | Context-sensitive cursor override | Sprite or SystemCursor |

#### Layout

- **Size modes**: `Default`, `Percent`, `Fixed`, `Fit`. `Default` resolves to Fit for the stacking axis of Row/Column, Percent(1.0) for everything else.
- **Alignment**: `Align.Min` (0), `Align.Center` (0.5), `Align.Max` (1). `Align2` for independent X/Y.
- **Building pattern**: `UI.BeginContainer(id, style)` ... `UI.EndContainer()`. Supports `using` auto-dispose.
- **EdgeInsets constructor**: `EdgeInsets(top, left, bottom, right)` — TLBR order, **not** TRBL.
- **Margin-based positioning**: In Container, children overlap via `Align + Margin`. In Row/Column, margin is consumed by sequential layout.
- **Element positioning**: During build phase, current frame has uninitialized `WorldToLocal`. Use `UI.GetElementWorldRect(id)` for previous frame's rect. Use `UI.MouseWorldPosition` for mouse (not `UI.ScreenToUI()`).

#### Style Struct Reference

**ContainerStyle** (used by Container, Column, Row): Size, MinWidth, MinHeight, MaxWidth, MaxHeight, Align, Margin, Padding, Color, BorderRadius, BorderWidth, BorderColor, Spacing, Clip, Order. Shorthand: `Border` (get/set BorderStyle), `Width`/`Height`, `AlignX`/`AlignY`.

**LabelStyle**: FontSize (default 16), Color, Align (default Min,Center), Font, Order (default 2), Overflow (`TextOverflow`: Overflow, Ellipsis, Scale, Wrap).

**ImageStyle**: Size, Stretch (`ImageStretch`: None, Fill, Uniform — default Uniform), Align, Scale, Color, BorderRadius, Order (default 1).

**GridStyle**: Spacing, Columns (default 3), CellWidth (default 100), CellHeight (default 100), CellMinWidth (0 = fixed columns, >0 = responsive), CellHeightOffset, VirtualCount (0 = no virtualization), StartIndex.

**PopupStyle**: Anchor, PopupAlign, Spacing, ClampToScreen, AnchorRect, MinWidth, AutoClose (default true), Interactive (default true).

**ScrollableStyle**: ScrollSpeed (default 30), Scrollbar (`ScrollbarVisibility`: Auto, Always, Never), ScrollbarWidth (default 8), ScrollbarMinThumbHeight (default 20), ScrollbarTrackColor, ScrollbarThumbColor, ScrollbarThumbHoverColor, ScrollbarPadding (default 2), ScrollbarBorderRadius (default 4).

**TextBoxStyle**: Height, FontSize (default 16), Font, BackgroundColor, TextColor, PlaceholderColor, SelectionColor, BorderRadius, BorderWidth, BorderColor, FocusBorderRadius, FocusBorderWidth, FocusBorderColor, Padding, IsPassword, Scope (`InputScope`). Shorthand: `Border`, `FocusBorder`.

**TextAreaStyle**: Same as TextBoxStyle except no IsPassword field. Multi-line with line wrapping.

**SceneStyle**: Size, Color, SampleCount (MSAA, default 1).

**TransformStyle**: Origin (pivot, default 0.5,0.5), Translate, Rotate (degrees), Scale.

#### Input

- `UI.IsHovered()`, `UI.WasPressed()`, `UI.HasFocus()`, `UI.SetFocus()`
- **Input inside popups**: Use `UI.IsDown()` / `UI.WasPressed()` instead of `Input.IsButtonDown()`. Popups consume mouse buttons via `Input.ConsumeButton()`.

#### Rendering

- Custom `UIVertex` mesh with 7 attributes: Position (vec2), UV (vec2), Normal/RectSize (vec2), Color (vec4), BorderRatio (float), BorderColor (vec4), CornerRadii (vec4)
- Renders on `UIConfig.UILayer` (default: **1000**). Popups use `Graphics.SetSortGroup(i+1)` for z-ordering.
- SDF-based border radius and antialiased borders computed in `ui.wgsl` fragment shader
- **Gradients and drop shadows are NOT implemented.** Plan exists at `docs/plan-gradient-fill.md`.

#### Scaling

- `UIScaleMode`: ConstantPixelSize, ScaleWithScreenSize
- `ScreenMatchMode`: MatchWidthOrHeight (default, blend factor 0.5), Expand, Shrink
- Reference resolution: `UIConfig.ReferenceResolution` (default 1920×1080)

#### Limits

| Resource | Limit | Source |
|----------|-------|--------|
| Elements per frame | 4096 | `UI.cs` MaxElements |
| Element stack depth | 128 | `UI.cs` MaxElementStack |
| Simultaneous popups | 4 | `UI.cs` MaxPopups |
| Text buffer | 64 KB per frame | `UI.cs` MaxTextBuffer |
| Unique element IDs | 32,767 | `UI.cs` MaxElementId |
| UI vertices | 16,384 | `UI.Draw.cs` MaxUIVertices |
| UI indices | 32,768 | `UI.Draw.cs` MaxUIIndices |

Note: UI vertex/index limits are separate from Graphics limits (65,536 / 196,608).

See `.claude/ui-layout-cookbook.md` for detailed usage patterns for each element type.

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
