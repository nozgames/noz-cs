# Raster Layers for Sprite Editor

## Context

The sprite editor currently supports only vector layers (`SpriteLayer` containing `SpritePath` nodes). This plan adds raster (bitmap) layers that allow freehand pixel painting with brush tools and pen pressure support. Raster layers composite with existing vector layers during both editor preview and export.

## New Files

| File | Purpose |
|------|---------|
| `editor/src/sprite/SpriteRasterLayer.cs` | New `SpriteNode` subclass storing `PixelData<Color32>` + GPU preview texture |
| `editor/src/sprite/BrushEngine.cs` | Hard/soft brush stamp rendering + stroke interpolation |
| `editor/src/sprite/RasterBrushTool.cs` | Brush/eraser tool (extends `Tool`) |
| `editor/src/sprite/RasterFillTool.cs` | Flood fill tool |
| `editor/src/sprite/RasterEyedropperTool.cs` | Color picker tool |

## Modified Files

| File | Change |
|------|--------|
| `editor/src/sprite/SpriteNode.cs` | Add `CollectVisibleRasterLayers()` + `ForEach(Action<SpriteRasterLayer>)` |
| `editor/src/sprite/SpriteDocument.File.cs` | Parse/save `rasterlayer` blocks (base64 PNG data) |
| `editor/src/sprite/SpriteDocument.Export.cs` | Blit raster layers during atlas rasterization |
| `editor/src/sprite/SpriteEditor.cs` | Draw raster layer textures in viewport, raster toolbar |
| `editor/src/sprite/SpriteEditor.Tools.cs` | Add `SpriteEditMode.Raster`, auto-activate on raster layer selection |
| `editor/src/sprite/SpriteEditor.Outliner.cs` | Raster layer rows with distinct icon, "Add Raster Layer" action |
| `engine/src/platform/PlatformEvent.cs` | Add `float Pressure` field (defaults 1.0) |
| `engine/src/input/Input.cs` | Add `static float Pressure` property |
| `platform/desktop/DesktopPlatform.cs` | Handle `SDL_EVENT_PEN_DOWN/UP/MOTION`, populate pressure |

---

## Phase 1: SpriteRasterLayer Node

**`SpriteRasterLayer.cs`** -- extends `SpriteNode` (leaf node, no children):
- `Vector2Int Size` -- pixel dimensions
- `Vector2Int Offset` -- pixel offset from origin (default: centered like RasterBounds)
- `PixelData<Color32>? Pixels` -- lazily allocated
- `Texture? _previewTexture` -- GPU texture for editor display
- `Clone()` deep-copies pixel data
- `EnsurePixels()` allocates on first access
- `UploadTexture()` / `UploadTexture(RectInt dirtyRect)` -- creates or partially updates GPU texture
- `Dispose()` frees PixelData + Texture

**`SpriteNode.cs`** additions:
```csharp
public void CollectVisibleRasterLayers(List<SpriteRasterLayer> result)
// Same pattern as CollectVisiblePaths but checks `is SpriteRasterLayer`
```

Existing traversal methods (`CollectVisiblePaths`, `ForEachEditablePath`, hit tests) naturally skip raster layers since they check `is SpritePath`.

Animation frame visibility: Raster layers inherit visibility from parent `SpriteLayer`, same as `SpritePath`. No changes to `SpriteAnimFrame` needed.

## Phase 2: Serialization

**Format** in `.sprite` text file:
```
rasterlayer "Paint" {
  size 64 64
  offset -32 -32
  data base64:iVBORw0KGgo...
}
```

Can appear at top level or nested in a `layer { }` block.

**`SpriteDocument.File.cs`** changes:
- `Load()` + `ParseLayer()`: add `else if (tk.ExpectIdentifier("rasterlayer"))` -> `ParseRasterLayer(ref tk, parent)`
- `ParseRasterLayer()`: read name, `{`, then `size w h`, `offset x y`, `data base64:...`, `}`
  - Decode base64 -> PNG bytes -> ImageSharp `Image<Rgba32>` -> copy to `PixelData<Color32>`
- `SaveLayers()` + `SaveLayer()`: add `else if (child is SpriteRasterLayer raster)` -> `SaveRasterLayer()`
  - Encode `PixelData` -> ImageSharp PNG -> base64 string -> write block

## Phase 3: Compositing

### 3a. Editor Preview

**`SpriteRasterLayer.UploadTexture()`**:
- If `_previewTexture == null`: `Texture.Create(Size.X, Size.Y, Pixels.AsByteSpan(), TextureFormat.RGBA8, TextureFilter.Point)`
- If exists: `_previewTexture.Update(Pixels.AsByteSpan())` (full) or `.Update(data, dirtyRect, Size.X)` (partial)

**`SpriteEditor`** -- new `DrawRasterLayers()` method called from `Update()`:
- Collect visible raster layers from `Document.RootLayer`
- For each, draw textured quad at layer bounds using `EditorAssets.Shaders.Texture`
- Bounds in document space: `offset / ppu` to `(offset + size) / ppu`

Initial approach: raster layers draw after all vector mesh (simpler). Layer interleaving is a V2 refinement.

### 3b. Export Rasterization

**`SpriteDocument.Export.cs`** -- modify `RasterizeLayer()`:
- After `SpriteLayerProcessor.ProcessLayer()` fills vector paths, walk children for `SpriteRasterLayer`
- `BlitRasterLayer()`: alpha-composite each raster layer's pixels into the output `PixelData` at the correct offset

```csharp
static void BlitRasterLayer(SpriteRasterLayer raster, PixelData<Color32> image,
    RectInt targetRect, Vector2Int sourceOffset)
{
    // For each pixel in raster layer, alpha-blend into image at targetRect + sourceOffset + raster.Offset
}
```

## Phase 4: Pen/Stylus Input

### 4a. PlatformEvent + Input

**`PlatformEvent.cs`**: Add `public float Pressure;` field (non-breaking, defaults to 0).
Update `MouseDown/MouseMove` factories to set `Pressure = 1.0f` (mouse always full pressure).

**`Input.cs`**: Add `public static float Pressure { get; private set; } = 1.0f;`
Update in event processing: when `MouseMove` or `MouseButtonDown` arrives with `Pressure > 0`, set `Input.Pressure`.

### 4b. SDL3 Desktop Platform

**`DesktopPlatform.cs`** `ProcessEvent()` -- add pen event cases:
- `SDL_EVENT_PEN_DOWN` -> emit `MouseButtonDown` with pressure from pen event
- `SDL_EVENT_PEN_UP` -> emit `MouseButtonUp`
- `SDL_EVENT_PEN_MOTION` -> emit `MouseMove` with pressure from pen event

This maps pen to mouse events (backward compatible) while carrying pressure data. Need to verify `ppy.SDL3-CS` binding struct names for pen events.

## Phase 5: Raster Tools

### 5a. Edit Mode

Add `Raster` to `SpriteEditMode` enum. Auto-activates when a `SpriteRasterLayer` is selected. Switching to Transform/Anchor mode deselects raster context.

### 5b. Floating Toolbar (Raster mode)

In `FloatingToolbarUI()`, when `CurrentMode == SpriteEditMode.Raster`:
- Brush button (hard), Soft brush button, Eraser button, Eyedropper button, Fill button
- Brush size control (slider or +/- buttons)
- Current color (reuse `Document.CurrentFillColor`)

New `WidgetId` entries: `RasterBrushButton`, `RasterSoftBrushButton`, `RasterEraserButton`, `RasterEyedropperButton`, `RasterFillButton`, `RasterBrushSize`

### 5c. BrushEngine (static utility)

**`BrushEngine.cs`**:

```csharp
static void StampHard(PixelData<Color32> target, Vector2 center, float radius, Color32 color, ref RectInt dirty)
// Circle stamp: for each pixel in bbox, if dist <= radius, alpha-composite color

static void StampSoft(PixelData<Color32> target, Vector2 center, float radius, Color32 color, ref RectInt dirty)
// Gaussian falloff: alpha *= exp(-dist^2 / (sigma^2)), sigma = radius * 0.5

static void StrokeBetween(PixelData<Color32> target, Vector2 from, Vector2 to,
    float radiusFrom, float radiusTo, Color32 color, bool soft, ref RectInt dirty)
// Interpolate stamps along line, spacing = 0.25 * radius, lerp radius for pressure variation
```

### 5d. RasterBrushTool

**`RasterBrushTool.cs`**:
- Constructor takes `SpriteRasterLayer`, `bool isEraser`, `bool isSoft`
- `Begin()`: `Undo.Record(Document)`, store initial mouse position
- `Update()`: convert mouse pos to pixel coords, call `BrushEngine.StrokeBetween()` from prev to current pos
  - Effective radius = `BrushSize * Input.Pressure`
  - Color = eraser ? `Color32(0,0,0,0)` : `Document.CurrentFillColor`
  - After stroke: `rasterLayer.UploadTexture(dirtyRect)`
- Coordinate conversion: screen -> world (inverse camera) -> document space (inverse `Document.Transform`) -> pixel space (`* ppu - raster.Offset`)

### 5e. RasterFillTool

**`RasterFillTool.cs`**:
- Scanline flood fill on `PixelData<Color32>`
- On click: `Undo.Record(Document)`, sample pixel at click pos, fill connected region with `CurrentFillColor`, upload texture

### 5f. RasterEyedropperTool

**`RasterEyedropperTool.cs`**:
- On click: sample pixel at click pos, set `Document.CurrentFillColor`
- Tool completes immediately

## Phase 6: Outliner

Modify `SpriteEditor.Outliner.cs`:
- Detect `node is SpriteRasterLayer`, show with bitmap icon
- Selection activates `SpriteEditMode.Raster`
- Add "New Raster Layer" button/command -- creates `SpriteRasterLayer` with `Document.ConstrainedSize` (or 64x64 default)

## Undo

Use existing `Undo.Record(Document)` which clones the entire document including pixel data. Memory-heavy for large rasters but correct and simple. Delta-based undo is a V2 optimization.

## Implementation Order

1. `SpriteRasterLayer` class + `SpriteNode` traversal methods
2. Serialization (load/save with base64 PNG)
3. Export compositing (raster layers in atlas output)
4. Editor preview (draw textured quads)
5. Outliner integration (create/select raster layers)
6. Pen/stylus input (PlatformEvent + SDL3 + Input.Pressure)
7. BrushEngine + RasterBrushTool
8. Eraser, eyedropper, fill tools
9. Floating toolbar for raster mode

Each step is independently testable.

## Verification

- Create a sprite with `ConstrainedSize`, add a raster layer, paint on it, save, reload -- pixels should round-trip
- Export sprite to atlas -- raster layer pixels should appear in the output PNG
- Use pen tablet -- brush strokes should vary in width with pressure
- Multiple raster layers should composite correctly (alpha blending)
- Undo/redo should restore raster layer pixel state
- Animation frames with raster layers inside layer groups should toggle visibility correctly
