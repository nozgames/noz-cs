//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class VectorSpriteEditor
{
    // Editor-wide toggle; state lives on the editor instance but the user
    // preference is sticky across editor instances within a session.
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

        // Build/refresh the preview RT before the scene pass starts so Update()
        // can display it the same frame — avoids a one-frame blank when the
        // user toggles preview on.
        if (PreviewRasterize)
        {
            UpdateMeshFromLayers();
            UpdatePreviewTexture();
        }
        else if (_previewRT.IsValid)
        {
            DisposePreview();
        }
    }

    internal static void LoadUserSettings(PropertySet props)
    {
        s_previewRasterize = props.GetBool("vector_sprite", "preview_rasterize", false);
    }

    internal static void SaveUserSettings(PropertySet props)
    {
        props.SetBool("vector_sprite", "preview_rasterize", s_previewRasterize);
    }

    // Supersample factor: render mesh into _previewRTHi (Nx native + 4xMSAA)
    // then downsample to _previewRT (1x, no MSAA) via a single linear blit.
    // A bilinear lookup only samples 2x2 source texels, so going above 2x
    // without cascaded downsampling wastes rendering work.
    private const int PreviewSupersample = 1;

    // Fixed allocation ceiling — the preview RTs are acquired at this size
    // once and never reallocated as the sprite's RasterBounds change. This
    // avoids the visual flash that happens when the RT is recreated (or the
    // pool hands us a different handle) on sprite resize.
    private const int MaxPreviewSize = 1024;

    private RenderTexture _previewRT;      // MaxPreviewSize x MaxPreviewSize
    private RenderTexture _previewRTHi;    // MaxPreviewSize * PreviewSupersample, 4xMSAA
    private int _previewMeshVersion = -1;
    private Vector2Int _renderedSize;
    private readonly Camera _previewCamera = new() { FlipY = false };

    private void UpdatePreviewTexture()
    {
        var size = Document.RasterBounds.Size;
        if (size.X <= 0 || size.Y <= 0 ||
            size.X > MaxPreviewSize || size.Y > MaxPreviewSize)
        {
            // Nothing to render — clear the cached size so DrawPreviewQuad
            // stops displaying the stale RT contents (e.g. after the user
            // deletes every path in the sprite).
            _renderedSize = default;
            return;
        }
        var hiSize = size * PreviewSupersample;

        // Always acquire at the MAX size — the pool reuses the same entry
        // across frames, and we render into a (0,0,size.X,size.Y) sub-region
        // via viewport. DrawPreviewQuad samples only the used UV corner.
        const int maxHi = MaxPreviewSize * PreviewSupersample;
        _previewRTHi = RenderTexturePool.Acquire(maxHi, maxHi, sampleCount: 4);
        _previewRT = RenderTexturePool.Acquire(MaxPreviewSize, MaxPreviewSize, sampleCount: 1);

        // Re-render if the mesh or the rendered sub-region changed.
        if (_previewMeshVersion == _meshVersion && _renderedSize == size)
            return;
        _previewMeshVersion = _meshVersion;
        _renderedSize = size;

        // --- Pass 1: render mesh into the (0,0,hiSize.X,hiSize.Y) sub-region
        // of the fixed max-size hi RT ---
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

        // --- Pass 2: downsample the used sub-region of hi RT into the 1x RT ---
        // Samples only the (0,0) -> (hiU,hiV) UV corner so the unused portion
        // of the max-size hi RT doesn't contaminate the blit.
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

    private void DrawPreviewQuad()
    {
        if (!_previewRT.IsValid) return;
        if (_renderedSize.X <= 0 || _renderedSize.Y <= 0) return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(3);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(_previewRT.Handle);
            Graphics.SetTextureFilter(TextureFilter.Point);
            Graphics.SetShader(EditorAssets.Shaders.Texture);

            // The MSAA-resolved RT holds premultiplied values by construction:
            // box-averaging per-sample Alpha-blend results produces (R*cov,
            // G*cov, B*cov, cov). Display with Premultiplied so the engine
            // doesn't darken the RGB a second time — this matches the CPU
            // rasterizer's alpha-weighted straight-alpha output visually.
            Graphics.SetBlendMode(BlendMode.Premultiplied);
            var xray = Workspace.XrayAlpha;
            Graphics.SetColor(new Color(xray, xray, xray, xray));

            // Sample only the (0,0) -> (u,v) corner: the max-size RT contains
            // valid pixels in the top-left sub-region matching _renderedSize.
            var bounds = Document.Bounds;
            var u = (float)_renderedSize.X / MaxPreviewSize;
            var v = (float)_renderedSize.Y / MaxPreviewSize;
            Graphics.Draw(bounds.X, bounds.Y, bounds.Width, bounds.Height, 0f, 0f, u, v);
        }
    }

    private void DisposePreview()
    {
        // The pool owns the textures; we just drop our references. The next
        // couple of FlushPendingReleases calls (in Graphics.BeginFrame) will
        // destroy them once they've aged out of any in-flight GPU commands.
        _previewRT = default;
        _previewRTHi = default;
        _previewMeshVersion = -1;
        _renderedSize = default;
    }
}
