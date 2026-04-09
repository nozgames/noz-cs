# Two-pass post-process AA for the vector rasterizer

## Context

The clip-path bleed bug returned for the user when the clip path has even slightly non-opaque color. We disabled AA entirely as a temporary experiment ([Rasterizer.cs:115](noz/editor/src/Rasterizer.cs#L115) is currently `MathF.Abs(sum) >= 0.5f ? 1f : 0f`), which proves the bleed is purely an edge-pixel artifact. The user wants to keep the bleed-free property of binary fills but bring AA back as a post-process — render every path aliased into a primary buffer, separately remember "this was an edge pixel", then smooth those edges in a final pass.

## Precedent

This idea has solid web/literature precedent:

- **MLAA** (Reshetov, 2009) — the original morphological anti-aliasing. Operates as a *post-process*: take an aliased image, detect borders, find specific border patterns, blend pixels along those borders. Spawned a whole family of techniques.
- **FXAA** (Lottes, NVIDIA) — pixel-shader post-process; "sees pixels that create an artificial edge" in an aliased image and smooths them. Edge detection from luminance only.
- **SMAA** (Jiménez et al., Crytek) — improved MLAA with explicit edge detection texture (binary edge mask in N/E/S/W directions per pixel) feeding pattern-based blending. The *edge texture* is the closest existing analog to what the user is describing.
- **A-buffer** (Carpenter, 1984) — the classical "store coverage as side-channel data, composite later" approach.
- **AGG / Cairo** — separate "rasterize to coverage" from "blend using coverage", which is conceptually adjacent but does both per-path rather than deferring.

The user's twist on this family is that **the rasterizer already knows the true sub-pixel coverage** (it's the `alpha` value at [Rasterizer.cs:115](noz/editor/src/Rasterizer.cs#L115) before our recent binarization). Instead of detecting edges from color discontinuities like FXAA/SMAA do, we keep the rasterizer's exact coverage in a side buffer and use it for the post-process blend. This gives geometry-accurate smoothing rather than heuristic smoothing.

## Why this fixes the bleed

The bleed comes from `Color32.Blend` ([Color.cs:28-39](noz/engine/src/graphics/Color.cs#L28-L39)) mixing colors when both src and dst have fractional alpha along a shared boundary. With this design, the bottom path has *no fractional alpha at all* in the primary buffer — its edge contribution went to the side buffer. By the time the top path's edge is composited, the underlying primary-buffer pixel is the **final** color from whichever path "won" that pixel binary-wise, not a half-painted intermediate. The lerp at the very end produces a clean transition with no leak from intermediate states.

## Design

### Buffers

Two parallel pixel buffers, both sized to the rasterization target rect:

1. **Primary buffer** — already exists: `PixelData<Color32> image` parameter to `Rasterizer.Fill`. Holds binary-fill colors (no fractional alpha across path boundaries).
2. **Edge buffer** — new: `PixelData<EdgePixel>` allocated by the caller (`VectorSpriteDocument.RasterizeLayer`) and passed into `Rasterizer.Fill`. One entry per pixel: `struct EdgePixel { Color32 Color; byte Coverage; }` — 5 bytes per pixel. Coverage = 0 means "no edge contribution at this pixel".

[PixelData<T>](noz/editor/src/utils/PixelData.cs#L11) is already generic over any unmanaged struct, so `PixelData<EdgePixel>` works without modification.

### Per-path rasterization (modified `Rasterizer.Fill`)

For each pixel walked in the coverage loop ([Rasterizer.cs:109-141](noz/editor/src/Rasterizer.cs#L109-L141)):

```
c = abs(sum)            // true sub-pixel coverage from signed-area calc (the value we threw away)
if c >= 0.5:
    primary[x, y] = path_color blended over primary[x, y] using path_color.A   // binary-area write, alpha-blended for non-opaque paths
    edge[x, y] = empty                                                          // any prior edge here is now hidden by this opaque interior
else if c > 1/255:
    edge[x, y] = (path_color, c * 2 * path_color.A / 255)                       // overwrite — topmost path's edge wins
```

Key points:
- For opaque path colors, the primary write is a straight overwrite (the current `additive alpha` special case at [Rasterizer.cs:135-136](noz/editor/src/Rasterizer.cs#L135-L136) becomes unnecessary because there are no fractional-alpha collisions to add).
- For semi-transparent path colors, the primary write uses normal Porter-Duff over with `srcA = path_color.A` (no coverage modulation, since coverage in the binary region is treated as 1.0).
- The edge buffer stores `coverage * 2` so the `(0, 0.5)` range gets remapped to `(0, 1)` — convenient for the composite step.
- "Last writer wins" on the edge buffer naturally implements "topmost path's silhouette wins" because we iterate paths bottom-to-top.
- Clearing the edge entry on a binary write is essential — otherwise a hidden bottom-path edge could leak through where a top-path interior covers it.

### Final composite (new step in `RasterizeLayer`)

After `Rasterizer.Fill` has been called for every path, walk the target rect once:

```
for each pixel (x, y) where edge[x, y].Coverage > 0:
    primary[x, y] = lerp(primary[x, y], edge[x, y].Color, edge[x, y].Coverage / 255)
```

Pseudocode in the actual implementation will operate on `Color32` channels and an integer-weighted lerp.

### Where to allocate the edge buffer

Two reasonable placements:

- **Per-`RasterizeLayer` call**: allocate a `PixelData<EdgePixel>` of `targetRect.Size` at the top of [VectorSpriteDocument.RasterizeLayer](noz/editor/src/sprite/vector/VectorSpriteDocument.Export.cs#L59), pass it to `Rasterizer.Fill` for each path, then run the composite, then dispose. Simplest.
- **Thread-static reusable pool**: like the existing `_edgePool` and `_coveragePool` in [Rasterizer.cs:20-21](noz/editor/src/Rasterizer.cs#L20-L21). Faster on repeated calls (atlas re-bakes touch many sprites), but more plumbing.

Recommend starting with per-call allocation, then promoting to a thread-static pool only if profiling shows it matters.

## Changes

### 1. [noz/editor/src/Rasterizer.cs](noz/editor/src/Rasterizer.cs)

- Define `internal struct EdgePixel { public Color32 Color; public byte Coverage; }` near the top of the file (or in a sibling file if preferred).
- Add an `edge` parameter to `Fill`:
  ```csharp
  public static void Fill(
      PathsD paths,
      PixelData<Color32> target,
      PixelData<EdgePixel> edge,
      RectInt targetRect,
      Vector2Int sourceOffset,
      int dpi,
      Color32 color)
  ```
- Restore the true coverage value: change [line 115](noz/editor/src/Rasterizer.cs#L115) back to `float alpha = MathF.Abs(sum); if (alpha > 1f) alpha = 1f;` (undoing the experimental binarization).
- Replace the per-pixel write block at [lines 118-139](noz/editor/src/Rasterizer.cs#L118-L139) with the binary-write + edge-record logic described above.
- Remove the additive-alpha override at [lines 131-136](noz/editor/src/Rasterizer.cs#L131-L136); it's no longer needed because adjacent paths no longer collide on fractional pixels.
- Add a public `Composite(PixelData<Color32> target, PixelData<EdgePixel> edge, RectInt targetRect)` method that performs the final lerp pass.

### 2. [noz/editor/src/sprite/vector/VectorSpriteDocument.Export.cs](noz/editor/src/sprite/vector/VectorSpriteDocument.Export.cs)

In `RasterizeLayer` at [lines 59-94](noz/editor/src/sprite/vector/VectorSpriteDocument.Export.cs#L59-L94):

- Allocate a `PixelData<EdgePixel>` sized to `targetRect.Size` (the edge buffer is local-rect, not full image).
- Pass it to each `Rasterizer.Fill` call.
- After the loop over `results`, call `Rasterizer.Composite(image, edge, targetRect)`.
- Dispose the edge buffer.

The same pattern needs to be applied in `RasterizeColorToPng` ([lines 96-135](noz/editor/src/sprite/vector/VectorSpriteDocument.Export.cs#L96-L135)) which is the parallel rasterization path for thumbnails — both call `RasterizeLayer`, so once `RasterizeLayer` is updated, both work.

### 3. Edge buffer note for `targetRect` offsetting

`Rasterizer.Fill` writes to `target[targetRect.X + px, targetRect.Y + py]` — the primary buffer is full-image-sized, the rect is a window. The edge buffer should be sized to `targetRect.Size` and indexed with local `(px, py)` coordinates (no offset). This keeps it small and avoids touching memory outside the active sprite frame. The `Composite` method walks the local edge buffer and writes back to the full primary buffer using the same `targetRect` offset.

## What is intentionally NOT changed

- `SpriteGroupProcessor.cs` — `TrimOverlaps` stays defined but uncalled. The two-pass approach makes it unnecessary for the bleed case.
- The mesh-based editor preview path (`VectorSpriteEditor.Mesh.cs`) — uses GPU rendering, not this rasterizer.
- `Color32.Blend` — still used for the binary write of semi-transparent path colors (no coverage modulation).

## Verification

1. Build: `dotnet build noz/editor/NoZ.Editor.csproj` from the main repo root.
2. Open `assets/sprite/vfx_smoke_pixel.sprite` in the vector sprite editor — confirm the previous bleed at the red clip boundary is gone *and* the silhouette is now anti-aliased again.
3. Open a sprite that has clip paths with a non-fully-opaque clip color (the case that broke `TrimOverlaps`) — confirm there is still no bleed.
4. Inspect the silhouette of a curved vector sprite at the boundary between the sprite and transparent background — confirm AA is still smooth, not jaggy.
5. Trigger thumbnail rasterization (`RasterizeColorToPng`, used for outliner thumbnails) — verify it still produces clean output at scaled DPI.
6. Edge cases:
   - Two stacked clip paths
   - Subtract path on the same layer as a clip
   - Stroked path with both stroke and fill (the ring + interior pair already produced by `SpriteGroupProcessor` at [lines 163-197](noz/editor/src/sprite/vector/SpriteGroupProcessor.cs#L163-L197))
   - Very thin paths (1 pixel wide) where every pixel has coverage < 0.5 — these now exist *only* in the edge buffer and need to render correctly via the composite step

## Critical files

- [noz/editor/src/Rasterizer.cs](noz/editor/src/Rasterizer.cs) — add edge struct, edge buffer parameter, restore real coverage, add `Composite` method
- [noz/editor/src/sprite/vector/VectorSpriteDocument.Export.cs](noz/editor/src/sprite/vector/VectorSpriteDocument.Export.cs) — allocate edge buffer, call `Composite` after all paths
- [noz/editor/src/utils/PixelData.cs](noz/editor/src/utils/PixelData.cs) — read-only; already generic, no changes

## Sources

- [Morphological antialiasing — Wikipedia](https://en.wikipedia.org/wiki/Morphological_antialiasing)
- [SMAA: Enhanced Subpixel Morphological Antialiasing — iryoku.com](https://www.iryoku.com/smaa/)
- [Fast Approximate Anti-Aliasing (FXAA) — Coding Horror](https://blog.codinghorror.com/fast-approximate-anti-aliasing-fxaa/)
- [How the stb_truetype Anti-Aliased Software Rasterizer v2 Works — nothings.org](https://nothings.org/gamedev/rasterize/) (the algorithm the existing rasterizer is based on)
