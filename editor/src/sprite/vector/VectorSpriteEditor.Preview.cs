//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class VectorSpriteEditor
{
    private static bool s_previewRasterize;

    public static bool PreviewRasterize
    {
        get => s_previewRasterize;
        set => s_previewRasterize = value;
    }

    private void TogglePreviewRasterize()
    {
        s_previewRasterize = !s_previewRasterize;
    }

    public override void PreUpdate()
    {
        base.PreUpdate();

        // Always keep the preview RT current — the PreviewRasterize flag
        // only controls whether Update() displays it, so toggling on never
        // shows stale content.
        UpdateMeshFromLayers();
        UpdatePreviewTexture();
    }

    internal static void LoadUserSettings(PropertySet props)
    {
        s_previewRasterize = props.GetBool("vector_sprite", "preview_rasterize", false);
    }

    internal static void SaveUserSettings(PropertySet props)
    {
        props.SetBool("vector_sprite", "preview_rasterize", s_previewRasterize);
    }

    // Supersample factor for the hi-res RT. A single bilinear downsample
    // only samples 2x2 source texels, so values above 2 waste work.
    private const int PreviewSupersample = 1;

    // Fixed ceiling — the RTs are acquired at this size once and never
    // reallocated as RasterBounds change, which avoids flashes on resize.
    private const int MaxPreviewSize = 1024;

    private RenderTexture _previewRT;
    private RenderTexture _previewRTHi;
    private int _previewMeshVersion = -1;
    private Vector2Int _renderedSize;
    private readonly Camera _previewCamera = new() { FlipY = false };

    private void UpdatePreviewTexture()
    {
        var size = Document.RasterBounds.Size;
        if (size.X <= 0 || size.Y <= 0 ||
            size.X > MaxPreviewSize || size.Y > MaxPreviewSize)
        {
            // Clear the cached size so DrawPreviewQuad hides stale content.
            _renderedSize = default;
            return;
        }
        var hiSize = size * PreviewSupersample;

        // Fixed max-size acquire: the pool reuses the same entry each frame,
        // and we render into a (0,0,size.X,size.Y) sub-region via viewport.
        const int maxHi = MaxPreviewSize * PreviewSupersample;
        _previewRTHi = RenderTexturePool.Acquire(maxHi, maxHi, sampleCount: 4);
        _previewRT = RenderTexturePool.Acquire(MaxPreviewSize, MaxPreviewSize, sampleCount: 1);

        if (_previewMeshVersion == _meshVersion && _renderedSize == size)
            return;
        _previewMeshVersion = _meshVersion;
        _renderedSize = size;

        // Pass 1: mesh → hi-res MSAA RT sub-region.
        Graphics.BeginPass(_previewRTHi, Color.Transparent);
        Graphics.SetViewport(0, 0, hiSize.X, hiSize.Y);

        _previewCamera.SetExtents(Document.Bounds);
        _previewCamera.Position = Vector2.Zero;
        _previewCamera.Update(hiSize);
        Graphics.SetCamera(_previewCamera);

        Graphics.SetShader(EditorAssets.Shaders.Texture);
        Graphics.SetTexture(Graphics.WhiteTexture);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetBlendMode(BlendMode.Alpha);

        foreach (var slot in _meshSlots)
        {
            Graphics.SetColor(slot.FillColor);
            Graphics.Draw(
                _meshVertices.AsSpan(slot.VertexOffset, slot.VertexCount),
                _meshIndices.AsSpan(slot.IndexOffset, slot.IndexCount));
        }

        Graphics.EndPass();

        // Pass 2: linear downsample the used sub-region into the 1x RT.
        Graphics.BeginPass(_previewRT, Color.Transparent);
        Graphics.SetViewport(0, 0, size.X, size.Y);

        _previewCamera.Update(size);
        Graphics.SetCamera(_previewCamera);

        Graphics.SetShader(EditorAssets.Shaders.Texture);
        Graphics.SetTexture(_previewRTHi.Handle);
        Graphics.SetTextureFilter(TextureFilter.Linear);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetBlendMode(BlendMode.None);
        Graphics.SetColor(Color.White);

        var bounds = Document.Bounds;
        var hiU = (float)hiSize.X / maxHi;
        var hiV = (float)hiSize.Y / maxHi;
        Graphics.Draw(bounds.X, bounds.Y, bounds.Width, bounds.Height, 0f, 0f, hiU, hiV);

        Graphics.EndPass();
    }

    private void DrawPreviewQuad() => DrawPreviewQuad(Document.Transform);

    private void DrawPreviewQuad(Matrix3x2 transform)
    {
        if (!_previewRT.IsValid) return;
        if (_renderedSize.X <= 0 || _renderedSize.Y <= 0) return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(3);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(transform);
            Graphics.SetTexture(_previewRT.Handle);
            Graphics.SetTextureFilter(TextureFilter.Point);
            Graphics.SetShader(EditorAssets.Shaders.Texture);

            // The MSAA-resolved RT is premultiplied by construction, so
            // display with Premultiplied to avoid darkening edges twice.
            Graphics.SetBlendMode(BlendMode.Premultiplied);
            var xray = Workspace.XrayAlpha;
            Graphics.SetColor(new Color(xray, xray, xray, xray));

            var bounds = Document.Bounds;
            var u = (float)_renderedSize.X / MaxPreviewSize;
            var v = (float)_renderedSize.Y / MaxPreviewSize;
            Graphics.Draw(bounds.X, bounds.Y, bounds.Width, bounds.Height, 0f, 0f, u, v);
        }
    }
}
