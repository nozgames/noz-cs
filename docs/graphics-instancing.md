# Indexed instancing

`Graphics.DrawElementsInstanced<T>` submits a span of unmanaged instance records
for the currently selected indexed mesh. It is independent of scenes, entities,
materials, transforms, visibility, and asset types. Applications own those policies.

```csharp
// InstanceColor implements IVertex and describes a Vector4 at shader location 5.
Graphics.SetShader(shader);
Graphics.SetMesh(mesh);
Graphics.SetViewProjection(cameraMatrix);
Graphics.DrawElementsInstanced<InstanceColor>(indexCount, colors);
```

The active shader provides `vs_instanced` and `fs_main`. Ordinary draws use
`vs_main` and `fs_main`. Mesh attributes occupy vertex-buffer slot 0; instance
attributes occupy slot 1 with instance step mode. Attribute locations must be
unique across both layouts. The descriptor stride must match the unmanaged CLR
size of `T` and be a multiple of four bytes. Matrix conventions and per-instance
data interpretation belong to the shader, not the graphics API.

## Ownership and ordering

- Submission immediately copies the span into engine-owned native memory. Stack
  storage and scratch arenas can be reused as soon as the call returns.
- Meshes, shaders, and textures must remain alive until deferred commands execute.
- Each submission has the same order/group/layer behavior as `DrawElements`.
  Instances within a submission keep their input order; they are not individually
  sorted or culled. Applications must sort transparent instances when needed.
- Adjacent submissions with identical state, geometry, and contiguous instance
  ranges can be combined into one driver draw without changing their order.
- CPU/GPU pages keep distinct ranges across views and flushes. Uploads append only
  new bytes; frame boundaries reset allocation cursors. A failed submission rolls
  back its reserved range so it does not consume the frame's instance budget.

## Budgets and memory

`GraphicsConfig.MaxInstancesPerFrame` limits submitted instances **per record
type, across all views and flushes in one frame**. Its default is 65,536. Exceeding
it throws; instances are not silently dropped. Empty submissions do nothing.

Pages grow geometrically from at least 256 records, capped by the configured
limit, and reuse the largest page first. Existing buffers are never resized while
pending commands refer to them. Retained capacity per type is less than twice a
power-of-two limit, or less than three times an arbitrary limit. Each retained
record costs `sizeof(T)` bytes of native staging memory and the same requested
GPU buffer capacity. Driver overhead and alignment are additional. Each page
uses one `MaxMeshes` resource slot. No instance pages are allocated until used.

Pages persist until `Graphics.Shutdown`, which releases their native and GPU
resources. This trades high-water memory retention for allocation-free warm
submission. Draw-command, batch, and global-snapshot limits remain independent.
The graphics API is used on the render thread; these arenas are not concurrent.

Snapshot lookup uses fixed native hash chains with full-value comparisons.
Hash collisions never establish equality. Vertex/instance pipeline keys likewise
compare full layouts, including offsets and normalization; their hashes are
computed once at mesh creation. Drivers copy descriptor arrays so later caller
mutations cannot corrupt those cache keys.

## Driver support

Check `Graphics.Driver.SupportsInstancing` before selecting this rendering path.
Native WebGPU, browser WebGPU, and the null driver implement it. The null driver
accepts submissions for headless execution; it does not render pixels.

Custom drivers retain source compatibility through default interface methods:
the capability defaults to false, unbinding slot 1 with handle zero is allowed,
and instancing operations throw `NotSupportedException`. Supporting drivers must
implement vertex-only meshes (`maxIndices == 0`), four-byte-aligned range uploads,
slot-1 binding, and indexed draws with instance count and first-instance offset.
There is no implicit fallback that guesses how to interpret application records.

## Validation

The temporary engine review harness passed 29 headless checks and native Vulkan
pixel comparisons for same-stride layouts, descriptor mutation, pipeline reuse
and binding-free shaders. That harness was removed after the review; it is not
part of the public engine. Applications should test their instance layouts and
rendered output against ordinary draws when integrating this API.
