//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ;
using NoZ.Editor;

namespace NoZ.Editor.Graphics3D;

/// <summary>
/// Renders at most one dirty mesh thumbnail per frame. Keeping this outside
/// Document.Draw avoids nesting a depth render pass inside the workspace pass.
/// </summary>
public static class MeshPreviewRenderer
{
    private static MeshPreviewSettings? _settings;

    private static Shader? _shader;
    private static Texture? _texture;
    private static readonly Dictionary<string, Texture> _additionalTextures = [];
    private static bool _initialized;
    public static int MaterialRevision { get; private set; }

    internal static void Configure(MeshPreviewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ShaderName);
        Shutdown();
        _settings = settings;
    }

    /// <summary>Bind the host-selected preview texture, or white when none is available.</summary>
    public static void BindTexture()
    {
        EnsureInitialized();
        var textureName = _settings?.TextureName;
        if (_texture == null && !string.IsNullOrEmpty(textureName))
        {
            var texturePath = System.IO.Path.Combine(Project.OutputPath, "texture", textureName);
            if (File.Exists(texturePath))
                _texture = Asset.Load(
                    AssetType.Texture,
                    textureName,
                    useRegistry: false,
                    libraryPath: Project.OutputPath) as Texture;
        }

        var texture = _texture ?? Graphics.WhiteTexture;
        Graphics.SetTexture(texture);
        Graphics.SetTextureFilter(_settings?.TextureFilter ?? TextureFilter.Linear);
    }

    /// <summary>Bind the complete host preview material, including auxiliary maps.</summary>
    public static void BindMaterialTextures()
    {
        BindTexture();
        var shader = GetShader();
        if (shader == null) return;
        var count = shader.Bindings.Count(b => b.Type is ShaderBindingType.Texture2D or
            ShaderBindingType.Texture2DArray or ShaderBindingType.Texture2DUnfilterable);
        for (var slot = 1; slot < count; slot++)
        {
            Texture? texture = null;
            if (_settings is { } settings && slot <= settings.AdditionalTextures.Count)
            {
                var name = settings.AdditionalTextures[slot - 1];
                if (!_additionalTextures.TryGetValue(name, out texture) &&
                    File.Exists(System.IO.Path.Combine(Project.OutputPath, "texture", name)))
                {
                    texture = Asset.Load(AssetType.Texture, name, useRegistry: false, libraryPath: Project.OutputPath) as Texture;
                    if (texture != null) _additionalTextures[name] = texture;
                }
            }
            Graphics.SetTexture(texture ?? Graphics.WhiteTexture, slot);
            Graphics.SetTextureFilter(TextureFilter.Linear, slot);
        }
    }

    /// <summary>Returns a shared shader owned by this service; callers must not dispose it.</summary>
    public static Shader? GetShader()
    {
        if (!Project.IsInitialized || _settings == null)
            return null;

        EnsureInitialized();

        if (_shader == null)
        {
            var shaderPath = System.IO.Path.Combine(Project.OutputPath, "shader", _settings.ShaderName);
            if (!File.Exists(shaderPath))
                return null;

            _shader = Asset.Load(
                AssetType.Shader,
                _settings.ShaderName,
                useRegistry: false,
                libraryPath: Project.OutputPath) as Shader;
        }

        return _shader;
    }

    public static void Update()
    {
        var shader = GetShader();
        if (shader == null)
            return;

        // A preview is cached after rendering, so spreading initial generation
        // across frames avoids a large hitch in projects with many mesh assets.
        foreach (var document in Project.Documents.OfType<MeshDocument>())
            if (document.TryRenderPreview(shader))
                break;
    }

    public static void Shutdown()
    {
        if (_initialized)
        {
            Project.OnExported -= OnDocumentExported;
            _initialized = false;
        }

        foreach (var document in Project.Documents.OfType<MeshDocument>())
            document.ReleasePreview();

        _shader?.Dispose();
        _shader = null;
        _texture?.Dispose();
        _texture = null;
        foreach (var texture in _additionalTextures.Values) texture.Dispose();
        _additionalTextures.Clear();
        MaterialRevision++;
        _settings = null;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        Project.OnExported += OnDocumentExported;
        _initialized = true;
    }

    private static void OnDocumentExported(Document document)
    {
        if (document is MeshDocument meshDocument)
            meshDocument.InvalidatePreview();

        if (document.Def.Type == AssetType.Shader && document.Name == _settings?.ShaderName)
        {
            _shader?.Dispose();
            _shader = null;
        }
        else if (document.Def.Type == AssetType.Texture && document.Name == _settings?.TextureName)
        {
            _texture?.Dispose();
            _texture = null;
        }
        else if (document.Def.Type == AssetType.Texture && _settings?.AdditionalTextures.Contains(document.Name) == true)
        {
            if (_additionalTextures.Remove(document.Name, out var texture)) texture.Dispose();
        }
        else
            return;

        MaterialRevision++;
        foreach (var mesh in Project.Documents.OfType<MeshDocument>())
            mesh.InvalidatePreview(reloadMesh: false);
    }
}
