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

Update the camera for the scene's render size first. The current implementation needs single-sample depth (`SceneStyle.Depth = true`, `SampleCount = 1`). It returns false without starting any passes when disabled, outside a scene, without sampleable depth, or with missing shaders. Settings preserve the original algorithm and default tuning, including half-resolution AO and the grayscale debug view.

The effect captures `PostProcess.CurrentColorTextureHandle` before its first blit and composites onto that input, preserving earlier effects. Depth still comes from the original scene. Core PostProcess only exposes the generic borrowed color handle; it has no dependency on SSAO or this extension. No separate pipeline, render-target pool or shader cache is introduced.

Current renderer limitation: named custom uniforms are not snapshotted per draw. Settings instances are independent, but rendering different SSAO cameras/settings in the same deferred frame is not supported yet; later calls would overwrite the earlier call's uniforms. Use one SSAO view per frame until SSAO's named uniforms are migrated to draw-local parameters. Cozy's existing single world view is unchanged.

Editor integration is provided separately by [NoZ.Editor.Graphics3D](../../../editor/ext/graphics3d/README.md). Referencing this runtime assembly does not register any editor documents or require an editor dependency.
