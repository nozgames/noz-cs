# Pixel-Art AA Filter in lit_sprite Shader

## Context

Pixel-art sprites currently use `TextureFilter.Point` which produces sharp texels but also visible stair-stepping under non-integer scales / rotations / sub-pixel camera movement. We want the "best of both worlds" from Cole Cecil's technique (https://colececil.dev/blog/2017/scaling-pixel-art-without-destroying-it/): sample the texture with **linear** filtering, but adjust the UV so that within each texel the sample snaps to the texel center, and only transitions linearly across the ~1-screen-pixel-wide region straddling texel boundaries. Result: crisp pixels everywhere except at texel edges, where a ~1 screen pixel wide ramp provides anti-aliasing.

All scene sprites in Stope are drawn with `lit_sprite.wgsl` (bound once in `Game.DrawScene()` at [game/Game.cs:284](game/Game.cs#L284)), so baking this into that shader covers the full cast (Mine blocks, Player, Pickups, VFX).

## The Technique

Standard "Klems" formulation — one linear sample, no branching:

```wgsl
fn pixel_art_uv(uv: vec2<f32>, tex_size: vec2<f32>) -> vec2<f32> {
    let texel = uv * tex_size;                // fragment position in texel space
    let texel_center = floor(texel) + 0.5;    // nearest texel center
    let diff = (texel - texel_center) / fwidth(texel); // offset in screen-pixels-per-texel
    let clamped = clamp(diff, vec2<f32>(-0.5), vec2<f32>(0.5));
    return (texel_center + clamped) / tex_size;
}
```

- `fwidth(texel)` gives the screen-space footprint of a texel (in texels). When >> 1 (minified / zoomed out), the ramp collapses to pure linear; when << 1 (magnified), it collapses to pure point. Between, we get a ~1 screen-pixel ramp — exactly the AA we want.
- Works correctly for atlas sprites because UVs and `textureDimensions(texture_array).xy` are both in the same atlas-UV/atlas-texel space — the technique is agnostic to whether the sampled region is a sub-rect of the atlas.
- Prior art in the engine: [noz/engine/assets/shader/text.wgsl:57-59](noz/engine/assets/shader/text.wgsl#L57-L59) and [noz/engine/assets/shader/ui.wgsl:77-80](noz/engine/assets/shader/ui.wgsl#L77-L80) both use `dpdx/dpdy/fwidth`, so derivatives are confirmed supported in this pipeline.

## Required Linear Sampler

The NoZ driver picks `_nearestSampler` vs `_linearSampler` per texture slot based on `Graphics.State.TextureFilters[slot]` ([noz/platform/webgpu/WebGPUGraphicsDriver.State.cs:301-312](noz/platform/webgpu/WebGPUGraphicsDriver.State.cs#L301-L312)). That value is written per-sprite by `Graphics.Draw(Sprite)` at [noz/engine/src/graphics/Graphics.Draw.cs:147](noz/engine/src/graphics/Graphics.Draw.cs#L147) via `SetTextureFilter(sprite.Filter)`, so pixel-art sprites (e.g. `human_body_pixel.sprite`, stored with `Filter = Point`) would otherwise still resolve to the nearest sampler even with the new shader code.

Fix: add a small "filter override" state to `Graphics`. When set, `SetTextureFilter()` ignores the per-sprite value and uses the override. `Game.DrawScene()` sets the override to `Linear` around the lit_sprite draw block, then clears it. This is a minimal, localized change that keeps the existing per-sprite `Filter` field intact (it still serves as metadata and still applies when the sprite is drawn with other shaders, e.g. `Shaders.Sprite` in the menu).

## Files to Modify

### 1. [assets/shader/lit_sprite.wgsl](assets/shader/lit_sprite.wgsl)

Add `pixel_art_uv` helper before `fs_main`, and replace line 100:

```wgsl
// Before:
let natural = textureSample(texture_array, texture_sampler, input.uv, input.atlas) * input.color;

// After:
let tex_size = vec2<f32>(textureDimensions(texture_array).xy);
let aa_uv = pixel_art_uv(input.uv, tex_size);
let natural = textureSample(texture_array, texture_sampler, aa_uv, input.atlas) * input.color;
```

No new bindings or uniforms — the texture size comes from `textureDimensions()` at runtime. No changes needed to `ShaderDocument.ParseWgslBindings`.

### 2. [noz/engine/src/graphics/Graphics.State.cs](noz/engine/src/graphics/Graphics.State.cs)

Add a filter-override field and modify `SetTextureFilter`:

```csharp
private static TextureFilter? _textureFilterOverride;

public static void SetTextureFilterOverride(TextureFilter? filter)
{
    _textureFilterOverride = filter;
}

public static void SetTextureFilter(TextureFilter filter, int slot = 0)
{
    Debug.Assert(slot is >= 0 and < MaxTextures);
    var effective = _textureFilterOverride ?? filter;
    var filterByte = (byte)effective;
    if (CurrentState.TextureFilters[slot] == filterByte) return;
    CurrentState.TextureFilters[slot] = filterByte;
    _batchStateDirty = true;
}
```

Also clear the override in `ResetState()` alongside the other state resets (~line 78-114) so it doesn't leak across frames.

### 3. [game/Game.cs](game/Game.cs) — `DrawScene()` around line 284

Wrap the lit_sprite block:

```csharp
Graphics.SetShader(GameAssets.Shaders.LitSprite);
Graphics.SetTextureFilterOverride(TextureFilter.Linear);  // NEW
Graphics.SetLayer(GameConstants.DrawLayers.Blocks);
Mine.Draw();
Player.Draw();
PickupManager.Draw();
VfxSystem.Render();
Graphics.SetTextureFilterOverride(null);                  // NEW
```

(Adjust exact placement to match the `PushState()`/`PopState()` scope that's already there — read the current shape of `DrawScene()` before editing.)

## Non-Goals / Out of Scope

- Do **not** change any `.sprite` asset files. Their stored `Filter = Point` remains correct as metadata ("this is pixel art"); the override handles the runtime-sampling nuance.
- Do **not** modify `noz/engine/assets/shader/sprite.wgsl` (the unlit base shader used by menu/UI). Pixel-art AA is specifically for in-game scene sprites.
- No new uniform buffer or shader binding — keep the shader's binding layout byte-identical so no driver/asset-format changes are needed.

## Verification

1. **Compile check**: Build from `D:\git\Stope` with `dotnet build game/Stope.csproj` — just need the game lib to compile (desktop copy may fail since the game is running).
2. **Shader rebuild**: lit_sprite.wgsl is re-parsed on asset export. Since bindings are unchanged, this should be transparent; confirm by checking that `ShaderDocument.ParseWgslBindings` still produces the same binding list.
3. **Visual checks** (run game):
   - Pixel-art sprites (player body, pickaxes, blocks) remain sharp at integer zoom (no blur).
   - At non-integer camera positions / during smooth camera motion, texel edges should show a ~1 screen pixel AA ramp instead of stair-stepping.
   - When rotated (e.g. swinging pickaxe, rocket-boot VFX), diagonal edges are anti-aliased rather than jagged.
   - Light-map tinting and darkness behavior should be unchanged (we only touched the `natural` sample line).
4. **Regression check**: Menu/UI sprites (drawn with `Shaders.Sprite`, not lit_sprite) should look identical to before — the override is only set inside `DrawScene()`.
5. **Filter override sanity**: Set a breakpoint or log in `SetTextureFilter` and confirm the override path only engages inside `DrawScene()`, and that `_batchStateDirty` still flips correctly so batches break when the effective filter changes.
