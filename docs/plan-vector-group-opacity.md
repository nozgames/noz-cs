# Vector Editor — Group Opacity

Status: **planned, not implemented**

Companion to [plan-layer-opacity.md](plan-layer-opacity.md), which covers the same idea for the **pixel** editor's CPU compositor. This doc is specifically about the **vector** editor's GPU mesh pipeline, which is a different problem with a different solution.

## Goal

Let the user set an `Opacity` value on a `SpriteGroup` in the vector editor and have the whole subtree composite as a unit. Visually: two overlapping half-transparent red `SpritePath`s inside a 50%-opacity group should look like a single 50% red shape — **not** the darker doubly-blended overlap you get from multiplying alpha per-path.

## Why naive multiplication is wrong

The obvious implementation — "multiply every child's alpha by the group's opacity" — fails where paths inside the group overlap. Two 50%-alpha paths stacked on top of each other produce ~75% coverage (`1 - (1-0.5)*(1-0.5)`), not 50%. That is not what users expect when they drag an opacity slider on a group in Illustrator / Figma / Inkscape: they expect the group's contents to be flattened first, *then* faded.

Correct semantics require rendering the group's contents at full alpha into an offscreen buffer, then compositing that buffer over the scene with the group's alpha.

## Can the GPU renderer support it?

**Yes.** NoZ already has every primitive needed:

- `RenderTexture` — offscreen color targets (BGRA8 / RGBA16F, optional MSAA)
- `Graphics.BeginPass` / `Graphics.EndPass` — switches the render target for a pass
- `RenderTexturePool` — pooled acquire/release of up to 24 targets
- Bloom / post-process already uses the exact pattern we need: `BeginPass → draw → EndPass → blit with alpha`

So the engine side is ready. The work is in the vector editor's mesh pipeline.

## Why this isn't a small change

The vector editor's rendering is aggressively flattened:

1. [`SpriteGroupProcessor.ProcessLayer`](../editor/src/sprite/vector/SpriteGroupProcessor.cs) recursively walks the `ActiveRoot` group and emits a **single flat list** of `LayerPathResult { Contours, Color, IsStroke }`. Parent-scoped boolean ops (subtract / clip) are resolved during this walk. Child groups are inlined into their parent's results.
2. [`VectorSpriteEditor.UpdateMeshFromLayers`](../editor/src/sprite/vector/VectorSpriteEditor.Mesh.cs) (≈ lines 142-155) tessellates that list into one batched `_meshVertices` / `_meshIndices` buffer with per-slot `MeshSlotData { VertexOffset, VertexCount, IndexOffset, IndexCount, FillColor }`.
3. `DrawMesh` (≈ lines 99-120) issues a draw per slot against one transform + shader.

By the time we reach `DrawMesh`, the group hierarchy is gone. We can't wrap "a group" in an offscreen pass because there is no record of where a group starts or ends in the slot stream. And you can't just re-run `ProcessLayer` on a subtree because a subtree's final geometry depends on subtract/clip paths that may live elsewhere in the parent.

## Proposed approach

Data + serialization + inspector are small. The renderer is where the real work is.

### 1. Data model

Add to [`SpriteNode`](../editor/src/sprite/SpriteNode.cs) (line 11, after `Locked`):

```csharp
public float Opacity { get; set; } = 1f;
```

Update `ClonePropertiesTo` (line 31) to copy it. Putting it on the base class is consistent with [plan-layer-opacity.md](plan-layer-opacity.md) — the pixel compositor and vector renderer then both honor the same property. `SpritePath` with `Opacity < 1` is just a multiply into its fill/stroke alpha at tessellation time (safe — a tessellated path doesn't self-overlap).

### 2. Serialization

[`VectorSpriteDocument.File.cs`](../editor/src/sprite/vector/VectorSpriteDocument.File.cs):

- `ParseGroup` and `ParsePath` learn to read `opacity <float>`.
- `SaveGroup` and `SavePath` (or whatever the symmetric save methods are named in this file) emit `opacity <f>` only when `!= 1`.
- Missing token → default `1.0f` (back-compat).

Note: the pixel-layer plan already modifies `SpriteDocument.File.cs` shared paths — make sure the vector format reader/writer aligns on token name and semantics.

### 3. Inspector UI

Mirror the pattern from [plan-layer-opacity.md](plan-layer-opacity.md) step 4 but in the vector editor's inspector panel. Single slider bound to `SelectedNode.Opacity`, wrapped in `Undo.Record(Document)`, with `MarkDirty()` (or the vector-editor equivalent) so the mesh rebuilds. Goes in a `GROUP` / `PATH` inspector section.

### 4. Renderer — the real change

Two tiers based on node type.

**Tier A: path-level opacity (cheap, safe)**

In `SpriteGroupProcessor.ProcessLayer`, when emitting a `LayerPathResult` for a path, multiply the path's `Opacity` into the fill/stroke alpha before it lands in `results`. Alternatively, apply in `DrawMesh` via `slot.FillColor.WithAlpha(...)`. Either works because a single tessellated path has single coverage.

**Tier B: group-level opacity (offscreen composite, correct)**

This is the architectural change. Recommended design:

**Per-group sub-mesh markers.** `SpriteGroupProcessor` keeps a stack during recursion. When it enters a `SpriteGroup` with `Opacity < 1`, it opens a new sub-result list; on exit, it emits a `GroupBegin(opacity) … GroupEnd` marker pair into the parent list. This turns the flat `List<LayerPathResult>` into a slot + marker stream.

`UpdateMeshFromLayers` tessellates slots into the vertex/index buffers as today, but preserves marker order in `_meshSlots` (e.g., slot kind: `Draw | GroupBegin | GroupEnd`).

`DrawMesh` walks the stream with an RT stack:
- `GroupBegin(α)` → `var rt = RenderTexturePool.Acquire(...)`, `Graphics.BeginPass(rt)`, clear transparent, push `rt` + `α`.
- `Draw` slot → same as today, but against the current pass (which may be the top-of-stack RT).
- `GroupEnd` → `Graphics.EndPass()`, pop `rt` + `α`, blit `rt` into the parent pass at `Color.White.WithAlpha(α)` using premultiplied-alpha semantics. `RenderTexturePool.Release(rt)`.

Nested groups nest pool slots (24 is plenty for editor use).

**Fast path:** when every node has `Opacity == 1`, the marker stream is empty — same single-batched-mesh draw as today. Important because the editor redraws every frame.

**Alternative considered — recursive re-tessellation per opacity boundary.** Simpler data flow but breaks today's boolean-op semantics: a subtract/clip path outside the group can no longer cut into it. Rejected.

### 5. Files to touch

| File | Change |
|------|--------|
| [`SpriteNode.cs`](../editor/src/sprite/SpriteNode.cs) | Add `Opacity` + update `ClonePropertiesTo` |
| [`VectorSpriteDocument.File.cs`](../editor/src/sprite/vector/VectorSpriteDocument.File.cs) | Parse + save `opacity` for groups and paths |
| [`SpriteGroupProcessor.cs`](../editor/src/sprite/vector/SpriteGroupProcessor.cs) | Emit `GroupBegin` / `GroupEnd` markers around `Opacity < 1` groups; multiply path `Opacity` into emitted colors |
| [`VectorSpriteEditor.Mesh.cs`](../editor/src/sprite/vector/VectorSpriteEditor.Mesh.cs) | `MeshSlotData` gets a kind discriminator (or a parallel marker list); `DrawMesh` runs an RT stack |
| Vector editor inspector | New `GROUP` section with opacity slider |

### 6. Primitives to reuse (do not rewrite)

- `RenderTexturePool.Acquire` / `Release`
- `Graphics.BeginPass(RenderTexture)` / `Graphics.EndPass()`
- Existing post-process / bloom blit template
- `Undo.Record(Document)` + `UI.WasChanged()` for the inspector slider

## Trade-offs

- **Perf:** each group with `Opacity < 1` costs 1 RT acquire + 1 extra pass + 1 blit per frame. Fine for an editor. Not currently a concern for runtime (runtime sprites come out of baked atlases via `VectorSpriteDocument.Export` and rasterization — see *Out of scope* below).
- **MSAA / format:** RT format should match the main vector pass (likely BGRA8). Pool supports it.
- **Nested groups:** work by stacking pool slots. Deepest realistic nesting is far under 24.
- **Path opacity vs group opacity:** intentionally two different code paths. Path opacity is a free multiply and can't double-blend itself. Group opacity has to be a real offscreen composite or it's wrong.
- **Subtract / clip interaction:** the marker approach preserves today's semantics — subtract/clip paths still cut into the group's contents *before* the group is composited. Need a test that verifies this doesn't accidentally short-circuit when a group boundary interrupts the processor's accumulation state.

## Verification plan

1. Build: `dotnet build noz/editor/editor.csproj`
2. Open a vector sprite. Create a group with two overlapping half-transparent red paths. Set group `Opacity = 0.5`.
   - **Expected:** overlap region is the same red as the non-overlap region (flattened then faded).
   - **Bug signal:** overlap is darker red — Tier B isn't running, or naive multiply leaked through.
3. `Opacity = 0` → group disappears. `Opacity = 1` → identical to no-group case (confirms zero-cost fast path).
4. Nest group inside group, both `< 1` → visually correct, no RT pool warnings.
5. Save & reload the `.sprite` → opacity round-trips.
6. Undo / redo an opacity change restores previous value.
7. Subtract path above an opacity group still cuts into the group's geometry.
8. If the pixel-layer plan has also landed: verify both honor `Opacity` consistently on shared nodes.

## Out of scope for this doc

- **Runtime sprite rendering.** Shipped sprites come from [`VectorSpriteDocument.Export`](../editor/src/sprite/vector/VectorSpriteDocument.Export.cs) / rasterization into atlas textures — they don't run the live mesh pipeline. Group opacity at runtime would have to be baked during export or handled at atlas-gen time. Not addressed here.
- **Blend modes other than alpha** (multiply, screen, etc.) on groups. Same offscreen-composite infrastructure could host them later.
- **Pixel-layer opacity.** Covered by the existing [plan-layer-opacity.md](plan-layer-opacity.md).
