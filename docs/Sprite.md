# NoZ Sprite Format (.sprite)

The `.sprite` format is a text-based vector path format used by the NoZ engine for resolution-independent 2D sprites. Sprites are defined as collections of filled/stroked bezier paths that get rasterized at runtime into texture atlases.

## File Structure

```
[global properties]

[frame]
[hold N]

path
  [path properties]
  anchor X Y [CURVE]
  anchor X Y [CURVE]
  ...

path
  ...
```

## Global Properties

| Property | Syntax | Description |
|----------|--------|-------------|
| `skeleton` | `skeleton "name"` | Binds the sprite to a named skeleton for skeletal animation. |

Note: The `palette` keyword is legacy and ignored by the parser. Colors are stored directly.

## Frames

For animated sprites, use `frame` to define multiple frames:

```
frame
hold 3

path
...

frame
hold 2

path
...
```

| Property | Syntax | Description |
|----------|--------|-------------|
| `frame` | `frame` | Starts a new animation frame. |
| `hold` | `hold N` | Number of extra ticks to hold this frame (integer). Appears on its own line after `frame`. |

For single-frame sprites, `frame` can be omitted entirely. Paths are implicitly part of frame 0.

**Runtime animation:** `Graphics.DrawAnimated(sprite, time, loop, bone)` computes frame index as `(int)(time * sprite.FrameRate)`. Default frame rate is 12 fps. Max 64 frames per sprite.

## Paths

Each `path` keyword starts a new vector shape. Paths are rendered in order (later paths draw on top).

### Path Properties

| Property | Syntax | Description |
|----------|--------|-------------|
| `fill` | `fill COLOR [OPACITY]` | Fill color. See Color Formats below. |
| `stroke` | `stroke COLOR [WIDTH]` | Stroke outline with width in stroke units (1 unit = 0.005 world units). |
| `subtract` | `subtract true` | Makes this path a cutout, subtracting from previous geometry in the same slot. |
| `clip` | `clip true` | Intersects this path with accumulated geometry below (same slot). |
| `layer` | `layer "name"` | Assigns to a named sprite layer (for multi-mesh sprites). |
| `bone` | `bone "name"` | Binds this path to a skeleton bone for animation. |

### Color Formats

Colors can be specified in several formats:

| Format | Example | Description |
|--------|---------|-------------|
| `#RRGGBB` | `fill #FAF4E8` | Hex RGB (opaque) |
| `#RRGGBBAA` | `fill #FAF4E880` | Hex RGBA |
| `#RGB` | `fill #FFF` | Short hex RGB |
| `rgba(R,G,B,A)` | `fill rgba(255,251,251,0.44)` | RGBA function (R,G,B: 0-255, A: 0.0-1.0) |
| `rgb(R,G,B)` | `fill rgb(66,165,245)` | RGB function (0-255) |
| Named | `fill black` | Named colors: `black`, `white`, `red`, `green`, `blue`, `yellow`, `cyan`, `magenta`, `gray`/`grey`, `orange`, `pink`, `purple`, `brown`, `transparent` |

**Legacy format** (still loadable): `fill INDEX [OPACITY]` where INDEX is an integer palette index (0-255) and OPACITY is an optional float 0.0-1.0. New files should use direct colors.

## Anchors

Anchor points define the vertices of a closed bezier path (the last anchor automatically connects back to the first).

```
anchor X Y [CURVE]
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `X` | float | Horizontal position in world-space coordinates. |
| `Y` | float | Vertical position in world-space coordinates. |
| `CURVE` | float (optional) | Quadratic bezier curve tension. Default `0` = straight line. |

### Coordinate System

- Origin `(0, 0)` is the center of the sprite.
- **Y-down**: negative Y = up, positive Y = down.
- Coordinates are world-space floats. At 64 PPU (default), the range `-0.5` to `0.5` covers 64 pixels. Typical icon coordinates are roughly `-0.25` to `0.25`.
- `PixelsPerUnit` is configurable per project (default 64).

### How Curves Work

Curves use a **quadratic bezier** constructed from a perpendicular offset at the segment midpoint:

```
midpoint = (anchor_current + anchor_next) / 2
perpendicular = normalize(rotate90(anchor_next - anchor_current))
control_point = midpoint + perpendicular * curve_value
```

The curve value is a signed perpendicular offset from the midpoint of the line segment between consecutive anchors. Each curved segment is sampled at 8 points for rasterization.

| Value | Effect |
|-------|--------|
| `0` (or omitted) | Sharp corner, straight line segments |
| `-0.01` to `-0.02` | Subtle rounding |
| `-0.02` to `-0.05` | Moderate curve |
| `-0.05` to `-0.07` | Strong, smooth curve |

The sign determines which side of the segment the curve bulges toward. For typical convex shapes (vertices listed clockwise), negative values curve outward.

## Rendering

### Drawing Sprites

```csharp
Graphics.Draw(sprite)                          // frame 0, default bone
Graphics.Draw(sprite, bone, frame)             // specific frame and bone
Graphics.DrawAnimated(sprite, time, loop, bone) // time-based frame selection
```

Sprite rendering converts pixel bounds to world units by dividing by `PixelsPerUnit`. For world-space rendering, use `CreateTranslation(x, y)` — no extra scale needed. Use `CreateScale(sprite.PixelsPerUnit)` only for screen-space rendering (e.g., cursors).

### Pipeline

1. `.sprite` text file edited in NoZ editor
2. `SpriteDocument.Load()` parses text into `Shape` objects (paths + anchors)
3. Anchors are converted to bezier contours and boolean-clipped (Clipper2)
4. Anti-aliased scanline rasterization into `PixelData<Color32>` bitmap
5. Rasterized bitmaps packed into texture atlases
6. Binary sprite format written (mesh UV rects + frame table)
7. Runtime `Sprite.Load()` reads binary format for rendering

## Limits

| Constant | Value |
|----------|-------|
| `Sprite.MaxFrames` | 64 |
| `Shape.MaxAnchors` | 1024 |
| `Shape.MaxPaths` | 256 |
| `Shape.MaxAnchorsPerPath` | 128 |
| `Shape.MaxSegmentSamples` | 8 |
| `Shape.MinCurve` | 0.0001 |
| `Shape.StrokeScale` | 0.005 |
| Default `PixelsPerUnit` | 64 |
| Default `FrameRate` | 12 fps |

## Examples

### Simple Triangle (play icon)

```
path
fill #FFFFFF
anchor -0.0625 -0.1484375
anchor 0.140625 0
anchor -0.0625 0.15625
```

### Rounded Rectangle

Use 4 anchors with small negative curve values:

```
path
fill #4040FF
anchor -0.15 -0.10 -0.018
anchor  0.15 -0.10 -0.018
anchor  0.15  0.10 -0.018
anchor -0.15  0.10 -0.018
```

### Circle/Oval

Use 4 anchors in a diamond with larger curve values:

```
path
fill #FF4040
anchor  0.00 -0.20 -0.065
anchor  0.20  0.00 -0.065
anchor  0.00  0.20 -0.065
anchor -0.20  0.00 -0.065
```

### Shape with Cutout (subtract)

```
path
fill #FFFFFF
anchor -0.2 -0.1 -0.045
anchor -0.05 -0.22 -0.046
anchor 0.17 -0.17 -0.052
anchor 0.22 0.02 -0.07
anchor -0.11 0.20 -0.025
anchor -0.21 0.09 -0.035

path
fill #FFFFFF
subtract true
anchor -0.09 -0.03 -0.023
anchor -0.14 0.01 -0.023
anchor -0.18 -0.03 -0.023
anchor -0.14 -0.07 -0.023
```

### Multi-color Sprite (dynamite)

```
path
fill #F54842
anchor -0.07 -0.10 -0.016
anchor 0.07 -0.10 -0.016
anchor 0.07 0.28 -0.018
anchor -0.07 0.28 -0.018

path
fill #404049
anchor -0.075 -0.10
anchor 0.075 -0.10
anchor 0.075 -0.155 -0.006
anchor -0.075 -0.155 -0.006

path
fill #000000
anchor -0.005 -0.155
anchor 0.005 -0.155
anchor 0.04 -0.265
anchor 0.03 -0.265
```

## Tips

- **Path order matters**: Later paths render on top. Draw base shapes first, then details.
- **Subtract paths**: Use `subtract true` to punch holes (donut shapes, cutout details).
- **Clip paths**: Use `clip true` to intersect with geometry below (masking effects).
- **Rounded rectangles**: 4 anchors with curve values around `-0.015` to `-0.02`.
- **Circles/ovals**: 4 anchors in a diamond with curve values around `-0.05` to `-0.07`.
- **Thin lines**: Create narrow filled shapes rather than using strokes for better control.
- **Sprites are authored** in the NoZ editor which provides a visual canvas. The `.sprite` text format is the serialized output.
- **GameAssets.cs** is auto-generated by the editor. Manually add entries following the existing pattern when the editor hasn't republished.

## Source Reference

- Text format parser: `noz/editor/src/document/SpriteDocument.cs` — `Load()` and `ParsePath()` methods
- Runtime sprite class: `noz/engine/src/graphics/Sprite.cs`
- Sprite rendering: `noz/engine/src/graphics/Graphics.Draw.cs`
- Rasterizer: `noz/editor/src/Rasterizer.cs`
- Shape geometry: `noz/editor/src/Shape.cs`
