# SDF Rendering (Sprites & Fonts)

NoZ uses multi-channel signed distance fields (MSDF) for resolution-independent sprite and font rendering with sharp corners. The implementation is a faithful port of [msdfgen](https://github.com/Chlumsky/msdfgen) by Viktor Chlumsky, located in `noz/editor/src/msdf/`.

Conceptually these are "SDF sprites" and "SDF fonts" — the fact that the underlying algorithm is multi-channel (MSDF) is an implementation detail. The codebase uses `IsSDF` as the flag, `sprite_sdf` / `texture_sdf` as shader names, etc.

## Why MSDF

Single-channel SDF encodes distance in one channel (R only). This produces rounded artifacts at sharp corners because the minimum distance forms a circular iso-contour around vertices.

MSDF solves this by encoding distance across three channels (R, G, B). Each edge is assigned a color channel. At sharp corners, the channels disagree about which edge is closest, and `median(r, g, b)` reconstruction in the shader preserves the sharp boundary.

## Source Files (`noz/editor/src/msdf/`)

| File | Description |
|------|-------------|
| `Msdf.Math.cs` | Vector math, equation solvers (quadratic/cubic), `GetOrthonormal` |
| `Msdf.SignedDistance.cs` | Distance + dot product for closest-edge comparison |
| `Msdf.EdgeColor.cs` | `EdgeColor` flags enum (RED, GREEN, BLUE, CYAN, MAGENTA, YELLOW, WHITE) |
| `Msdf.EdgeSegments.cs` | `LinearSegment`, `QuadraticSegment`, `CubicSegment` with signed distance, scanline intersection, bounds, split |
| `Msdf.Contour.cs` | Closed edge loop with shoelace winding calculation |
| `Msdf.Shape.cs` | Contour collection with validate, normalize, orient contours |
| `Msdf.EdgeColoring.cs` | `EdgeColoring.ColorSimple` — assigns R/G/B to edges at sharp corners |
| `Msdf.Generator.cs` | `GenerateMSDF` (OverlappingContourCombiner), `GenerateMSDFSimple`, and `ErrorCorrection` |
| `Msdf.Sprite.cs` | Bridge: converts NoZ sprite paths to msdf shapes and runs generation |
| `Msdf.Font.cs` | Bridge: converts TTF glyph contours to msdf shapes and runs generation |

## Generation Pipeline

### 1. Shape Conversion

**Sprites** (`MsdfSprite.FromSpritePaths`): NoZ sprite paths are converted to `Shape`/`Contour`/`EdgeSegment` objects. Linear segments become `LinearSegment`, quadratic curves become `QuadraticSegment`. Each path becomes one contour. Sprite coordinates are already in screen-space (Y-down).

**Fonts** (`MsdfFont.FromGlyph`): TTF glyph contours are converted similarly. TTF uses Y-up coordinates, so Y values are negated during conversion (`flipY`) to produce screen-space Y-down coordinates. This ensures correct winding direction for the MSDF generator without needing `shape.inverseYAxis`.

### 2. Shape Preparation

- **Normalize** (`Shape.Normalize`): Single-edge contours are split into thirds so edge coloring has enough edges to assign distinct colors.
- **Orient contours** (`Shape.OrientContours`): Ensures all outer contours have consistent winding direction using scanline intersection analysis. Critical for multi-contour shapes — without it, paths with opposite winding would be treated as holes by the OverlappingContourCombiner.
- **Edge coloring** (`EdgeColoring.ColorSimple`): Edges are assigned R/G/B channel colors. At sharp corners (where `dot(dir_a, dir_b) <= 0` or `|cross(dir_a, dir_b)| > sin(3.0)`), adjacent edges get different colors.

### 3. MSDF Generation (`MsdfGenerator.GenerateMSDF`)

Uses the **OverlappingContourCombiner** algorithm (matching msdfgen's default behavior). For each pixel:

1. Per-contour distances are computed for each color channel (R, G, B)
2. A global shape-level distance is tracked as a fallback
3. Contours are pre-classified by winding direction (positive = outer, negative = hole)
4. The combiner classifies each pixel as inner or outer based on which contours contain it
5. The winning contour's complete RGB triplet is used, preserving multi-channel edge coloring

Both sprites and fonts use `GenerateMSDF` (the OverlappingContourCombiner version).

### 4. Error Correction (`MsdfGenerator.ErrorCorrection`)

**Fonts only.** After MSDF generation, a legacy error correction pass detects "clashing" texels where bilinear interpolation between adjacent pixels would produce incorrect median values. These texels are converted to single-channel (all RGB set to median), which eliminates interpolation artifacts at the cost of losing sharp corners at those specific texels.

This matches msdfgen's behavior where `msdfErrorCorrection` is always called after `generateMSDF`.

Sprites do not currently use error correction.

### 5. Subtract Path Handling (Sprites Only)

Subtract paths are handled by generating a separate MSDF and compositing:
1. Additive paths produce an MSDF where inside > 0.5
2. Subtract paths produce their own MSDF
3. The subtract MSDF is inverted (`1 - value`) so its inside becomes outside
4. The two are intersected per-channel via `min(add, inverted_sub)`

## Coordinate Mapping

The generator maps pixel coordinates to shape-space:
```
shapePos = (pixel + 0.5) / scale - translate
```
Where `scale` and `translate` are provided by the caller. The distance range is symmetric around 0 and normalized to [0, 1] in the output.

## Shaders

All SDF content (sprites and fonts) uses MSDF under the hood. There is one set of SDF shaders, not separate SDF/MSDF variants.

### Runtime: `sprite_sdf.wgsl`

Uses `texture_2d_array` (same atlas texture array as normal sprites). Fragment shader:

```wgsl
fn median(r: f32, g: f32, b: f32) -> f32 {
    return max(min(r, g), min(max(r, g), b));
}

let dist = median(msd.r, msd.g, msd.b);
let edgeWidth = 0.7 * length(vec2<f32>(dpdx(dist), dpdy(dist)));
let alpha = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, dist);
output = vec4(vertexColor.rgb, alpha * vertexColor.a);
```

The `dpdx`/`dpdy` derivatives adapt antialiasing width to the screen-space pixel density — sharp at high zoom, wider at low zoom.

### Editor: `texture_sdf.wgsl`

Same fragment logic but uses `texture_2d` instead of `texture_2d_array` for the editor's single-atlas rendering path.

### Text: `text.wgsl`

Same median reconstruction for fonts, with additional per-vertex outline support (outline color, width, softness).

## Runtime

### Binary Format

The sprite binary includes an `IsSDF` byte (`0` = normal, `1` = SDF). When `IsSDF` is true, each mesh includes a `FillColor` field (4 bytes RGBA).

Font binary version 6 uses RGBA8 atlas format (4 bytes per pixel, RGB = MSDF channels, A = 255).

### Sprite.IsSDF

A simple `bool` property on `Sprite`. Both legacy single-channel SDF (value 1) and MSDF (value 2) are treated as SDF=true when loading. There is no `SdfMode` enum — the distinction between single-channel and multi-channel is purely internal to the generator.

### Draw Path

In `Graphics.Draw.cs`, sprite draw checks `sprite.IsSDF`:
1. **Shader**: `GetSpriteShader()` returns `_spriteSdfShader` if `IsSDF`, otherwise `_spriteShader`
2. **Texture filter**: SDF requires `Linear` filtering (smooth distance interpolation). Normal sprites use `Point`.
3. **Per-mesh color**: Sets `Graphics.Color` to `mesh.FillColor` before drawing each mesh quad

### Color-Aware Mesh Slots

SDF encodes distance, not color. Each fill color gets its own mesh slot with its own atlas region. A sprite with 5 colors uses 5x the atlas space.

## Edge Coloring Details

`EdgeColoring.ColorSimple` (ported from msdfgen):

- **No corners**: All edges get the same two-channel color (e.g., CYAN)
- **One corner** ("teardrop"): Three color regions assigned via `symmetricalTrichotomy`, with edge splitting if fewer than 3 edges
- **Multiple corners**: Colors switch at each corner using seed-based `switchColor`, with the last color constrained to differ from the initial to avoid wrap-around conflicts

The angle threshold of 3.0 radians (~172 degrees) means any junction sharper than ~172 degrees triggers a color change.

## Data Flow

```
Sprite paths / TTF glyph contours
  |
  v
Shape conversion (FromSpritePaths / FromGlyph)
  |
  v
Normalize → OrientContours → EdgeColoring.ColorSimple
  |
  v
GenerateMSDF (OverlappingContourCombiner)
  |  (fonts only)
  v
ErrorCorrection (legacy clash detection)
  |  (sprites only)
  v
Subtract compositing (invert + min)
  |
  v
RGBA8 atlas (R=ch0, G=ch1, B=ch2, A=255)
  |
  v
Binary asset (.sprite / .font) with IsSDF flag
  |
  v
Runtime: sprite_sdf.wgsl / text.wgsl — median(r,g,b) reconstruction
  |
  v
Screen: sharp edges at any scale
```

## References

- [msdfgen by Viktor Chlumsky](https://github.com/Chlumsky/msdfgen) — the reference C++ implementation this port is based on
- Chlumsky, V. (2015). "Shape Decomposition for Multi-channel Distance Fields" — the original thesis
- [Valve SDF paper](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf) — the original single-channel SDF technique
