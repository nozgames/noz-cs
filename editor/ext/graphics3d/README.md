# Optional 3D editor

Reference `NoZ.Editor.Graphics3D.csproj` in a custom editor host. It depends on `NoZ.Editor` and `NoZ.Graphics3D`, never on a game assembly. Core NoZ editor excludes `ext/**` from compilation and has no extension references.

Register explicitly through the existing host callbacks:

```csharp
using NoZ;
using NoZ.Editor;
using NoZ.Editor.Graphics3D;

var config = new EditorApplicationConfig
{
    // Also set the usual title, project and editor paths.
    RegisterAssetTypes = Graphics3DModule.RegisterAssetTypes,
    RegisterDocumentTypes = () => Graphics3DEditor.RegisterDocumentTypes(
        new MeshPreviewSettings("my_mesh_shader")
        {
            TextureName = "my_texture", // Optional: null binds white.
            TextureFilter = TextureFilter.Linear,
        }),
    Update = Graphics3DEditor.Update,
};
```

Call `Graphics3DEditor.Shutdown()` before `Application.Shutdown()` and before replacing/shutting down a project. For headless imports, use the same registration callback in `Project.Init`; no GPU is needed to export a mesh. Shutdown releases thumbnail meshes/textures, cached preview assets and event subscriptions. Register again for a new project.

The preview shader and optional texture are exported asset names in `Project.OutputPath`. The host supplies the shader: it must accept `MeshVertex3D`, use the supplied object-to-clip matrix and enable depth testing. Lit shaders can append `normal_to_world: mat4x4<f32>` to their globals block after projection/time (byte 80), as described in the runtime extension guide. The optional texture is bound to slot 0. This leaves palette selection and lighting policy with the host. Missing shaders leave the document's icon visible until the shader is exported. Shader/texture exports invalidate cached thumbnails automatically.

Included tools:

- `MeshDocument` and its glTF/GLB importer, preserving the existing `Mesh` document identity and `MESH` binary format.
- Public `GltfImporter.ImportNamedMeshes(path)` for host-owned compound assets.
  Returns uniquely named mesh-local geometry; it intentionally does not apply
  scene-node transforms. Exporters must bake the desired local frame before calling it.
  Blender launch/conversion and construction-role policy remain host-owned.
- `TextureDocument` for standalone PNG/JPEG/TGA/WebP/BMP texture import, with point/linear filtering in the inspector.
- `PaletteTextureDocument` and its editor: square textures, 8×8-pixel swatches, continuous multi-stop gradients, and the Palette Texture entry in the New menu. Construction data stays in `.png.meta`; the PNG remains usable in Blender and exports as a regular runtime Texture. The `Palette Texture` metadata identity and binary output are unchanged.
- `TextureAssetWriter` for shared RGBA8 texture export, also usable by host-owned image importers.
- Mesh orbit editing and cached fixed-angle thumbnails, rendered outside the workspace pass.
- `MeshWorkspaceProjection` for custom 3D document editors: pan/zoom follow the 2D workspace, with depth preserved.
- `MeshPreviewRenderer.GetShader()` / `BindTexture()` for host editors that share the preview material. Returned shader ownership remains with the service.

The extension registers Texture and Palette Texture after the core sprite importer, preserving the inspector order Image / Texture / Palette Texture. Ordinary PNGs still default to sprites. Prefabs, snapping and terrain remain host-owned. Palette selection for mesh shading still comes from the host's preview settings. The runtime graphics3d extension also supplies SSAO; include its `assets` directory in the project's source list to import its shaders using the normal Shader importer. glTF import currently supports triangle geometry, vertex channels and material names; it does not automatically import glTF materials/textures or add scene/animation support.
