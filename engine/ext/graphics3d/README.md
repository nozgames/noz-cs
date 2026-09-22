# Optional 3D runtime

Reference `NoZ.Graphics3D.csproj` to opt in. It references only `NoZ.Engine`; the engine does not reference this project and excludes `ext/**` from its default items.

Types use the shared `NoZ` namespace but live in the optional `NoZ.Graphics3D` assembly. The extension provides:

- `Graphics3DModule.RegisterAssetTypes()` — call before loading mesh assets.
- `Camera3D` and `Ray3D` — perspective projection and screen picking using `System.Numerics` row-vector matrices, with depth in the 0–1 clip range.
- `Mesh`, `MeshVertex3D`, and `MeshPrimitive` — CPU/GPU mesh data and binary import/export. `MESH` version 1 is unchanged.
- `Graphics3D.Draw(mesh, model, camera.ViewProjectionMatrix)` — indexed submission using the caller's shader, textures, blend mode and layer, with a per-object inverse-transpose normal matrix. The two-matrix form keeps lighting in world space when objects rotate or scale. `Draw(mesh, viewProjection)` uses an identity model for previews or world-space geometry.
- `PostProcess3D.Ssao(camera, settings)` and `SsaoSettings` — depth-only ambient occlusion, blur and composition on the existing `NoZ.PostProcess` pipeline.

The host owns materials, render passes and scene/gameplay organization. Enable depth on the pass and in the mesh shader, and restore the 2D camera before submitting UI. Mesh vertices use positions/normals/tangents/UV0/colors at shader locations 0–4. Dispose mesh assets before graphics shutdown.

Generic GPU facilities (vertex layouts, buffers, depth attachments, matrix submission, texture sampling, and post-process blits) remain in core. They do not require camera, mesh-asset, scene, lighting, or prefab concepts.

Lighting models, shadow generation, light ownership and their shaders belong to
the host game, not this extension. Cozy documents its implementation in
`docs/lighting.md`.

## Per-object transforms

For raw indexed geometry, call `Graphics3D.SetTransform(model, viewProjection)` before submitting it. Lit shaders use this globals layout:

```wgsl
struct Globals {
    projection: mat4x4<f32>,
    time: f32,
    normal_to_world: mat4x4<f32>, // Aligned to byte 80.
}
// In the vertex shader; normalize after interpolation in the fragment shader:
// output.normal = (globals.normal_to_world * vec4<f32>(input.normal, 0.0)).xyz;
```

The extension computes the normal matrix, including nonuniform scale. Core only copies opaque bytes via `Graphics.SetDrawParameters`, appending them to the existing 80-byte globals prefix. Parameters are snapshotted with each projection, deduplicated, and preserved through sorting and internal flushes. PushState/PopState also saves/restores them. Call `Graphics.ClearDrawParameters()` to stop supplying them. Blocks must be 16-byte aligned in size and at most 256 bytes; callers own the matching shader layout. `MaxGlobalSnapshots` bounds the snapshot count. Ordinary 2D shaders keep their existing layout and 80-byte uploads; driver buffers grow lazily only when extended data is used.

## SSAO

Add this extension's `assets` directory to the host editor's `[source]` list (Cozy uses `noz/engine/ext/graphics3d/assets`). Its three WGSL shaders (`ssao`, `ssao_blur`, `ssao_composite`) use the ordinary Shader importer and keep their existing runtime names. Import them and load/reload/unload them through the host's usual asset manifest, just like the core post-process shaders. Do not keep duplicate copies of these shaders in the game's source assets.

```csharp
// One settings instance per view; it can be tuned independently.
var settings = new SsaoSettings { Radius = 1.25f, Strength = 4.25f };

// Inside the depth-enabled UI.Scene callback, after world geometry:
PostProcess3D.Ssao(camera, settings);
```

Update the camera for the scene's render size first and enable `SceneStyle.Depth`. Both single-sample and 4× MSAA scenes are supported on the native and browser WebGPU backends: when a multisampled scene pass ends, the backend resolves its nearest depth sample into the single-sample depth texture consumed by post-processing. Color resolves separately using normal MSAA coverage. No depth-resolve pass is needed for single-sample or depth-disabled rendering. SSAO returns false without starting any passes when disabled, outside a scene, without sampleable depth, or with missing shaders. Settings preserve the original algorithm and default tuning, including half-resolution AO and the grayscale debug view.

The effect captures `PostProcess.CurrentColorTextureHandle` before its first blit and composites onto that input, preserving earlier effects. Depth still comes from the original scene. Core PostProcess only exposes the generic borrowed color handle; it has no dependency on SSAO or this extension. No separate pipeline, render-target pool or shader cache is introduced.

Current renderer limitation: named custom uniforms are not snapshotted per draw. Settings instances are independent, but rendering different SSAO cameras/settings in the same deferred frame is not supported yet; later calls would overwrite the earlier call's uniforms. Use one SSAO view per frame until SSAO's named uniforms are migrated to draw-local parameters. Cozy's existing single world view is unchanged.

Editor integration is provided separately by [NoZ.Editor.Graphics3D](../../../editor/ext/graphics3d/README.md). Referencing this runtime assembly does not register any editor documents or require an editor dependency.

## 3D VFX

`Graphics3DModule.RegisterAssetTypes()` also registers `Vfx3D` (`VFX3`, version 1).
Include the extension's assets directory in the editor sources to import the
`vfx3d` shader. The core 2D VFX asset format and API stay compatible; both runtimes
share ranges, curve LUTs, color curves, and `VfxMath` evaluation.

Create one `VfxSystem3D` per scene, and dispose it before graphics shutdown:

```csharp
// After registering asset types and loading the host's asset manifest:
var effects = new VfxSystem3D(maxParticles: 4096, maxInstances: 256);
var handle = effects.Play(effectAsset, new Vector3(0, 1, 0));

// Once per simulation frame:
effects.Update(Time.DeltaTime);

// In the scene's depth-enabled pass, after opaque geometry:
effects.Draw(camera, particleShader); // imported "vfx3d" shader

effects.SetTransform(handle, emitterTransform);
effects.Stop(handle); // Stop emitting; existing particles finish.
effects.Kill(handle); // Remove this effect immediately.
effects.Clear();      // World switch, pause/menu transition, etc.
effects.Dispose();
```

Effects support multiple emitters, immediate bursts, continuous rates, looping,
point/sphere/box spawning, and a direction cone with a 0–180 degree half-angle.
Sphere shells use uniform volume sampling. Emitter duration controls emission;
particle lifetime controls the remaining tail. Looping repeats each emitter's
cycle independently. Rate curves are sampled at each update segment's midpoint;
births receive the elapsed time since their spawn within that update. Emission
work and catch-up cycles are bounded, and excess emissions are dropped when
capacity is exhausted. `Play` returns an invalid handle when no instance slot
is available. Handles are specific to their owning system and survive slot reuse
safely. `Update` and `Play` work without a graphics device.

World-space particles inherit the emitter transform at birth; local particles
follow its current transform. Gravity defaults to negative Y and is applied in
the particle's simulation space. Billboard sizes are in world units, with roll
and angular velocity in degrees. Lifetime curves control size, speed, gravity,
color, opacity, and roll speed. Each particle samples its random endpoints once.

Rendering sorts all particles in the system by camera-space depth, then submits
consecutive texture/blend groups through the existing instancing stream. Each
draw copies its instance data, allowing multiple cameras in the same frame.
The supplied shader tests scene depth without writing it, using the generic
`[shader] depth_read_only = true` flag supported by native and browser WebGPU.
The host still owns pass ordering, shaders, lighting, and post-processing.

Particle textures are standalone `Texture` assets. Empty names use a white quad;
texture filtering follows the asset. `Columns` and `Rows` define a flipbook grid,
with lifetime animation or a randomly selected fixed frame. Textures are borrowed
from the asset registry, or from the optional `TextureResolver` used by previews.
Assets and the shader must remain alive until queued draws execute.

This version renders unlit billboards. Mesh particles, trails, collisions, and
soft intersections are future extensions. Different VFX systems and other
transparent scene objects are not jointly depth-sorted; keep related effects
in the same system and choose their scene submission point accordingly.
