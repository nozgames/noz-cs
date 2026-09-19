//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ;
using NoZ.Editor;

namespace NoZ.Editor.Graphics3D;

public class MeshDocument : Document
{
    private const int PreviewSize = 256;

    private readonly Camera3D _previewCamera = new() { FieldOfView = MathF.PI / 4f };
    private ImportedMesh? _imported;
    private Mesh? _previewMesh;
    private RenderTexture? _previewTexture;
    private bool _previewDirty = true;
    private bool _previewMeshDirty = true;

    public int VertexCount => _imported?.Vertices.Length ?? 0;
    public int IndexCount => _imported?.Indices.Length ?? 0;
    public int PrimitiveCount => _imported?.Primitives.Length ?? 0;

    public static void RegisterDef()
    {
        DocumentDef<MeshDocument>.Register(new DocumentDef
        {
            Type = Mesh.Type,
            Name = "Mesh",
            Extensions = [".gltf", ".glb"],
            Factory = _ => new MeshDocument(),
            EditorFactory = document => new MeshEditor((MeshDocument)document),
            Icon = () => EditorAssets.Sprites.AssetIconBin,
        });
    }

    public override void Load()
    {
        TryImport();
    }

    public override void Reload()
    {
        TryImport();
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        var imported = ImportMesh();
        using var stream = File.Create(outputPath);
        Mesh.Write(
            stream,
            imported.Vertices,
            imported.Indices,
            imported.Primitives,
            imported.BoundsMin,
            imported.BoundsMax);
        _imported = imported;
    }

    public override void Draw()
    {
        if (_previewTexture is { Handle: not 0 })
        {
            using var _ = Graphics.PushState();
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTexture(_previewTexture.Handle);
            Graphics.SetTextureFilter(TextureFilter.Linear);
            Graphics.SetBlendMode(BlendMode.Premultiplied);
            Graphics.SetColor(Color.White);
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.Draw(Bounds);
            return;
        }

        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconBin);
        }
    }

    public override bool DrawThumbnail()
    {
        if (_previewTexture is not { Handle: not 0 })
            return false;

        UI.Image(_previewTexture, ImageStyle.Center);
        return true;
    }

    public override void InspectorUI()
    {
        UI.Text(Def.Name);
        UI.Text($"Vertices: {VertexCount:N0}");
        UI.Text($"Triangles: {IndexCount / 3:N0}");
        UI.Text($"Primitives: {PrimitiveCount:N0}");
    }

    protected virtual ImportedMesh ImportMesh() => GltfImporter.Import(Path);

    private void TryImport()
    {
        try
        {
            _imported = ImportMesh();
        }
        catch (Exception ex)
        {
            _imported = null;
            ReportError(ex.Message);
        }
    }

    internal void InvalidatePreview(bool reloadMesh = true)
    {
        _previewDirty = true;
        _previewMeshDirty |= reloadMesh;
    }

    internal bool TryRenderPreview(Shader shader)
    {
        if (!_previewDirty)
            return false;

        var meshPath = System.IO.Path.Combine(Project.OutputPath, "mesh", Name);
        if (!File.Exists(meshPath))
            return false;

        var passStarted = false;
        try
        {
            if (_previewMeshDirty || _previewMesh == null)
            {
                var mesh = Asset.Load(
                    Mesh.Type,
                    Name,
                    useRegistry: false,
                    libraryPath: Project.OutputPath) as Mesh;
                if (mesh == null)
                {
                    _previewDirty = false;
                    _previewMeshDirty = false;
                    return true;
                }

                _previewMesh?.Dispose();
                _previewMesh = mesh;
                _previewMeshDirty = false;
            }

            if (_previewMesh.RenderMesh.Handle == nuint.Zero)
            {
                _previewDirty = false;
                return true;
            }

            _previewTexture ??= RenderTexture.Create(
                PreviewSize,
                PreviewSize,
                sampleCount: 4,
                format: TextureFormat.RGBA8,
                name: $"{Name}_mesh_preview",
                depth: true);

            var size = _previewMesh.BoundsSize;
            var radius = MathF.Max(size.Length() * 0.5f, 0.01f);
            var distance = MathF.Max(radius * 2.75f, 0.1f);
            const float yaw = MathF.PI * 0.25f;
            const float pitch = MathF.PI * 0.15f;
            var cosPitch = MathF.Cos(pitch);
            var orbit = new Vector3(
                cosPitch * MathF.Sin(yaw),
                MathF.Sin(pitch),
                cosPitch * MathF.Cos(yaw));
            var target = _previewMesh.BoundsCenter;
            var eye = target + orbit * distance;
            var near = MathF.Max(radius * 0.01f, 0.001f);
            var far = MathF.Max(distance + radius * 4f, near + 1f);
            _previewCamera.Position = eye;
            _previewCamera.Target = target;
            _previewCamera.NearClip = near;
            _previewCamera.FarClip = far;
            _previewCamera.ProjectionOffset = Vector2.Zero;
            _previewCamera.Update(new Vector2Int(PreviewSize, PreviewSize));

            Graphics.BeginPass(_previewTexture, Color.Transparent);
            passStarted = true;
            Graphics.SetTransform(Matrix3x2.Identity);
            Graphics.SetShader(shader);
            MeshPreviewRenderer.BindTexture();
            Graphics.SetBlendMode(BlendMode.None);
            Graphics.SetLayer(EditorLayer.Document);
            global::NoZ.Graphics3D.Draw(_previewMesh, _previewCamera.ViewProjectionMatrix);
            Graphics.EndPass();
            passStarted = false;

            _previewDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            if (passStarted && Graphics.IsRenderTexturePassActive)
                Graphics.EndPass();
            _previewDirty = false;
            ReportError($"Failed to render mesh preview: {ex.Message}");
            return true;
        }
    }

    internal void ReleasePreview()
    {
        _previewMesh?.Dispose();
        _previewTexture?.Dispose();
        _previewMesh = null;
        _previewTexture = null;
        _previewDirty = true;
        _previewMeshDirty = true;
    }

    public override void Dispose()
    {
        ReleasePreview();
        base.Dispose();
    }
}
