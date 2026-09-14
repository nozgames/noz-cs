//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor.Graphics3D;

/// <summary>
/// Host-owned material choices. Names refer to exported assets in Project.OutputPath.
/// The shader must accept MeshVertex3D and enable depth testing. An omitted texture
/// binds white so a vertex-color shader needs no game-specific palette.
/// </summary>
public sealed record MeshPreviewSettings(string ShaderName)
{
    public string? TextureName { get; init; }
    public TextureFilter TextureFilter { get; init; } = TextureFilter.Linear;
}

public static class Graphics3DEditor
{
    /// <summary>Call from the host's RegisterDocumentTypes callback, including headless imports.</summary>
    public static void RegisterDocumentTypes(MeshPreviewSettings previewSettings)
    {
        MeshPreviewRenderer.Configure(previewSettings);
        Graphics3DModule.RegisterAssetTypes();
        MeshDocument.RegisterDef();
        TextureDocument.RegisterDef();
        PaletteTextureDocument.RegisterDef();
    }

    /// <summary>Call outside the workspace render pass, e.g. EditorApplicationConfig.Update.</summary>
    public static void Update() => MeshPreviewRenderer.Update();

    /// <summary>Release preview resources before shutting down graphics.</summary>
    public static void Shutdown() => MeshPreviewRenderer.Shutdown();
}
