# Optional 3D runtime

Reference `NoZ.Graphics3D.csproj` to opt in. It references only `NoZ.Engine`; the engine does not reference this project and excludes `ext/**` from its default items.

Types use the shared `NoZ` namespace but live in the optional `NoZ.Graphics3D` assembly. The extension provides:

- `Graphics3DModule.RegisterAssetTypes()` — call before loading mesh assets.
- `Camera3D` and `Ray3D` — perspective projection and screen picking using `System.Numerics` row-vector matrices, with depth in the 0–1 clip range.
- `Mesh`, `MeshVertex3D`, and `MeshPrimitive` — CPU/GPU mesh data and binary import/export. `MESH` version 1 is unchanged.
- `Graphics3D.Draw(mesh, model, camera.ViewProjectionMatrix)` — indexed submission using the caller's shader, textures, blend mode and layer, with a per-object inverse-transpose normal matrix. The two-matrix form keeps lighting in world space when objects rotate or scale. `Draw(mesh, viewProjection)` uses an identity model for previews or world-space geometry.
- `PostProcess3D.Ssao(camera, settings)` and `SsaoSettings` — depth-only ambient occlusion, blur and composition on the existing `NoZ.PostProcess` pipeline.
- `Lighting3D`, `PointLight3D`, `ShadowCaster3D`, and `SkylightVolume3D` — cached direct shadows and voxel sky visibility; materials and light ownership stay with the host.

The host owns materials, render passes and scene/gameplay organization. Enable depth on the pass and in the mesh shader, and restore the 2D camera before submitting UI. Mesh vertices use positions/normals/tangents/UV0/colors at shader locations 0–4. Dispose mesh assets before graphics shutdown.

Generic GPU facilities (vertex layouts, buffers, depth attachments, matrix submission, texture sampling, and post-process blits) remain in core. They do not require camera, mesh-asset, scene, lighting, or prefab concepts.

## Lighting

Import this extension's `lighting_shadow` and `lighting_shadow_copy` shaders through the normal asset pipeline. Own one disposable `Lighting3D` per view. Call `Prepare` **before** entering the scene pass, supplying visible shadow geometry, point lights, world bounds, a sky-geometry revision, a voxelization callback, and a focus position. Inside the scene, call `Bind` before drawing with the host's lit material. Dispose before graphics shutdown.

`ShadowCaster3D.Transform` is the model-to-world matrix; bounds are world-space. Supply CPU vertices/indices to batch geometry into one draw per shadow face. GPU-only casters work but require individual draws. Increment the caster revision after in-place mesh edits, and the sky revision after any terrain or occluder change. The voxelization callback can call `AddHeightField`, `AddMesh`, or `AddTriangle`; use visible geometry, not broad collision boxes, so openings remain open.

The current binding contract is:

| Slot | Resource |
| --- | --- |
| 0 | Reserved for the host material/palette |
| 1 | Sun depth texture, 2048² |
| 2 | RGB24 radial-distance point-shadow atlas; six 256² faces per light |
| 3 | R8 skylight volume packed into a 1024-wide 2D texture; byte 0 is solid, bytes 1..255 encode air visibility 0..1 |
| 4 | RGBA32F lighting parameters; layout is documented by `Lighting3D.Prepare` and Cozy's `world_lit.wgsl` consumer |

Sun direction points **toward** the sun. Intensity and color changes do not rebuild shadows. A moved occluder rebuilds the sun, affected point shadows, and sky visibility; stationary frames reuse them. The nearest 16 distinct point-light IDs are active, with stable ID ordering. Additional lights are omitted, not drawn unshadowed. Point range is limited to 64 world units. `ActivePointLights`, `OmittedPointLights`, `ShadowUpdatesLastFrame`, and skylight build timing expose this budget.

`LightingSettings3D.ShadowSoftness` is a 0..1 host-shader setting; changing it does not rebuild the cached maps. Parameter texel 45 contains sun world-units-per-texel (XY), its linear depth range (Z), and softness (W). Cozy uses these for bounded, contact-dependent comparison filtering of sun and point shadows. Point samples remap across cube faces and keep receiver-plane bias; the filter does not blur packed depth values. This is an artistic soft-shadow approximation, not full area-light visibility.

Skylight uses conservative 0.5-unit surface voxels (coarsened if necessary to stay under 2,097,152 cells), full visibility from above, and attenuated propagation through openings. It is an artistic indoor/outdoor ambient approximation, **not** colored bounce lighting or full GI. Direct colored light is wall-blocked by shadow maps. Thin walls are preserved by conservative voxelization, but small openings and shadow details are limited by resolution. The current mesh convention excludes triangles with all vertex alphas below 0.5 from shadow/sky occlusion; Cozy uses these for luminous lantern inserts.

When sampling skylight, exclude solid texels and renormalize the trilinear weights over the remaining air texels. Do not interpolate solids as zero light: that creates voxel-height bands on terrain. Encoded byte 1 represents genuinely dark air and must still participate in interpolation. Return zero if no valid air sample is available.

Rebuilds are synchronous. Large edits or activating many lights can spike frame time. Cozy updates shadows and sky occupancy in the same frame as visible terrain edits and door changes; throttling lighting alone leaves stale geometry shadowing freshly lowered ground. Unchanged frames reuse the caches. Core render-target submission now stores pass order separately from material/layer sorting to support these additional passes without reducing the existing 2D layer/group ranges. The generic 64-pass budget remains unchanged.

Cozy integrates this through `WorldLighting`: palette/vertex colors remain the albedo, with diffuse sun, sky ambient, and colored point lights. Its current SSAO multiplies the composed opaque scene (not only ambient); water and the diorama background still use their own shaders and do not receive these lights yet.

Cozy's sun-shadow consumer uses a texel-center-aligned, continuously weighted 4x4 comparison filter with receiver-plane depth correction. Equal-weight nearest-texel PCF produces visible repeated steps on door trim at walking distances. Point shadows use bilinear comparison filtering; neither path interpolates packed depth bytes or disables occlusion to hide aliasing.

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
