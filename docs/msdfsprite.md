# Multi-Channel SDF (MSDF) Sprites and Fonts

NoZ uses multi-channel signed distance fields to render resolution-independent sprites and fonts with sharp corners. The implementation is a faithful port of [msdfgen](https://github.com/Chlumsky/msdfgen) by Viktor Chlumsky, located in `noz/editor/src/msdf/`.

## Why MSDF

Single-channel SDF produces rounded artifacts at sharp corners. At a vertex where two edges meet, the minimum distance forms a circular iso-contour around the vertex, rounding the corner. This is inherent to scalar distance fields and cannot be fixed by increasing resolution.

MSDF solves this by encoding distance information across three channels (R, G, B). At sharp corners, the channels disagree about which edge is closest, and the `median(r, g, b)` reconstruction in the shader preserves the sharp boundary.

## Architecture

### Source Files (`noz/editor/src/msdf/`)

| File | Description |
|------|-------------|
| `Msdf.Math.cs` | Vector math, equation solvers (quadratic/cubic), `GetOrthonormal` |
| `Msdf.SignedDistance.cs` | Distance + dot product for closest-edge comparison |
| `Msdf.EdgeColor.cs` | `EdgeColor` flags enum (RED, GREEN, BLUE, CYAN, MAGENTA, YELLOW, WHITE) |
| `Msdf.EdgeSegments.cs` | `LinearSegment`, `QuadraticSegment`, `CubicSegment` with signed distance, scanline intersection, bounds, split |
| `Msdf.Contour.cs` | Closed edge loop with shoelace winding calculation |
| `Msdf.Shape.cs` | Contour collection with validate, normalize, orient contours |
| `Msdf.EdgeColoring.cs` | `EdgeColoring.ColorSimple` — assigns R/G/B to edges at sharp corners |
| `Msdf.Generator.cs` | `MsdfGenerator.GenerateMSDF` (OverlappingContourCombiner algorithm) and `ErrorCorrection` |
| `Msdf.Sprite.cs` | Bridge: converts NoZ sprite paths to msdf shapes and runs generation |
| `Msdf.Font.cs` | Bridge: converts TTF glyphs to msdf shapes and runs generation |

### Pipeline

1. **Shape conversion** (`MsdfSprite.FromSpritePaths` / `MsdfFont.FromGlyph`): Source geometry (sprite paths or TTF glyph contours) is converted to `Shape`/`Contour`/`EdgeSegment` objects. Linear segments become `LinearSegment`, quadratic curves become `QuadraticSegment`. Each path/glyph contour becomes one msdf contour.

2. **Normalize** (`Shape.Normalize`): Single-edge contours are split into thirds so edge coloring has enough edges to assign distinct colors.

3. **Orient contours** (`Shape.OrientContours`): Ensures all outer contours have consistent winding direction. This is critical for multi-contour shapes — without it, paths drawn with opposite winding would be treated as holes instead of additive shapes by the OverlappingContourCombiner. Uses scanline intersection counting to determine which contours are inside others and reverses any that have incorrect orientation.

4. **Edge coloring** (`EdgeColoring.ColorSimple`): Edges are assigned R/G/B channel colors. At sharp corners (where `dot(dir_a, dir_b) <= 0` or `|cross(dir_a, dir_b)| > sin(3.0)`), adjacent edges get different colors. This is the key step that creates multi-channel differentiation.

5. **MSDF generation** (`MsdfGenerator.GenerateMSDF`): Uses the OverlappingContourCombiner algorithm (ported from msdfgen) to correctly handle multiple disjoint contours. For each pixel, per-contour distances are computed independently, then the combiner uses winding direction and distance sign to resolve which contour "owns" the pixel. Output is a float RGB bitmap with values in [0, 1] where 0.5 = on edge.

6. **Compositing** (sprites only, `MsdfSprite.RasterizeMSDF`): Additive and subtract paths are generated as separate shapes, then composited. Subtract shapes are inverted and intersected (min) with the additive result.

### Integration Points

**Sprites**: `Shape.Rasterize.cs` delegates `RasterizeMSDF()` to `Msdf.MsdfSprite.RasterizeMSDF()`. Sprite atlases use RGBA8 format with RGB encoding distance and A unused.

**Fonts**: `FontDocument.cs` calls `Msdf.MsdfFont.RenderGlyph()` to generate MSDF glyphs. Font atlases use RGBA32 format. The `text.wgsl` shader uses `median(r, g, b)` for reconstruction.

### Coordinate Mapping

The generator maps pixel coordinates to shape-space:
```
shapePos = (pixel + 0.5) / scale - translate
```
Where `scale = (dpi, dpi)` and `translate = sourceOffset / dpi`. The distance range is converted from pixels to shape units: `rangeInShapeUnits = range / dpi * 2.0`.

## Shaders

### Sprite MSDF Shader (`sprite_msdf.wgsl`)

```wgsl
fn median(r: f32, g: f32, b: f32) -> f32 {
    return max(min(r, g), min(max(r, g), b));
}

let dist = median(msd.r, msd.g, msd.b);
let edgeWidth = 0.7 * length(vec2<f32>(dpdx(dist), dpdy(dist)));
let alpha = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, dist);
```

### Text MSDF Shader (`text.wgsl`)

Same median reconstruction, with additional per-vertex outline support (outline color, width, softness).

The adaptive `edgeWidth` from screen-space derivatives ensures clean anti-aliasing at any zoom level.

## SdfMode

The sprite binary format uses `SdfMode` to distinguish rendering modes:
- `None` (0) — normal RGBA color
- `Sdf` (1) — single-channel SDF (R only)
- `Msdf` (2) — multi-channel SDF (RGB)

## Multi-Contour / Multi-Shape Support

The generator uses msdfgen's **OverlappingContourCombiner** algorithm to correctly handle shapes with multiple disjoint contours (e.g., separate sprite paths, or font glyphs like "i" with a dot and body).

### The Problem with Simple Generation

The legacy/simple algorithm finds the globally nearest colored edge across all contours for each channel. This breaks with multiple disjoint contours because:
- Edge coloring from one contour interferes with another contour's distance field
- Pixels between two shapes pick up distance info from the wrong shape's edges
- This causes inversion artifacts and noise around the second shape

### OverlappingContourCombiner Algorithm

The solution computes distances **per-contour**, then resolves which contour "wins" at each pixel:

1. **Per-contour distances**: Each contour's edges only contribute to that contour's R/G/B distances. A global shape-level distance is also tracked as a fallback.
2. **Winding classification**: Contours are pre-classified by winding direction (via `Contour.Winding()` shoelace formula). Positive winding = outer boundary, negative = hole.
3. **Inner/outer grouping**: At each pixel, contours where the point is inside (positive winding, positive median distance) are grouped as "inner"; contours where the point is outside (negative winding, negative median) are grouped as "outer".
4. **Winner selection**: Whichever group (inner vs outer) has a median closer to the boundary wins. The algorithm then refines within that group and checks opposite-winding contours that might be even closer.
5. **Full RGB preservation**: The winning contour's complete RGB triplet is used, preserving the multi-channel edge coloring relationship.

### OrientContours is Required

`Shape.OrientContours()` must be called before edge coloring. Sprite paths drawn by the user may have arbitrary winding direction. Without orientation normalization, a path wound clockwise would be treated as a hole by the OverlappingContourCombiner, causing it to render inverted. `OrientContours` uses scanline intersection analysis to ensure all outer contours have consistent positive winding.

## Error Correction

The port includes a legacy error correction pass (`MsdfGenerator.ErrorCorrection`) that detects "clashing" texels where bilinear interpolation between adjacent pixels would produce incorrect median values. **This is currently disabled** because the legacy `detectClash` algorithm is too aggressive for sprite use — it replaces multi-channel data with the median, destroying the corner sharpness that makes MSDF work.

If artifacts appear at specific edge configurations, the modern error correction from msdfgen (which protects corners and edges via a stencil buffer) would need to be ported.

## Edge Coloring Details

`EdgeColoring.ColorSimple` uses msdfgen's algorithm with seed-based pseudo-random color selection:

- **No corners**: All edges get the same two-channel color (e.g., CYAN)
- **One corner** ("teardrop"): Three color regions assigned via `symmetricalTrichotomy`, with edge splitting if fewer than 3 edges
- **Multiple corners**: Colors switch at each corner using `switchColor` with a seed, and the last spline's color is constrained to differ from the initial color to avoid wrap-around conflicts

The angle threshold of 3.0 radians (~172 degrees) means any junction sharper than ~172 degrees triggers a color change.

## Subtract Path Handling

Subtract paths (holes) are handled by generating a separate MSDF and compositing:
1. Additive paths are combined into one shape (one contour per path) and produce an MSDF where inside > 0.5
2. Subtract paths are combined into a separate shape and produce their own MSDF
3. The subtract MSDF is inverted (1 - value) so its inside becomes outside
4. The two are intersected per-channel via `min(add, inverted_sub)`

## References

- [msdfgen by Viktor Chlumsky](https://github.com/Chlumsky/msdfgen) — the reference C++ implementation this port is based on
- Chlumsky, V. (2015). "Shape Decomposition for Multi-channel Distance Fields" — the original thesis
- [Valve SDF paper](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf) — the original single-channel SDF technique
