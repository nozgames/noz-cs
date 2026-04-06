//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  SkiaSharp-based preview: renders vector shapes to a texture
//  via SpriteSkiaRenderer for real-time editing feedback.
//

namespace NoZ.Editor;

public partial class SpriteEditor
{
    private Texture? _meshTexture;
    private int _meshTextureW;
    private int _meshTextureH;
    private bool _meshDirty = true;
    private int _meshFrame = -1;

    private readonly List<LayerPathResult> _tessellateResults = new();

    private void UpdateMeshFromLayers()
    {
        if (!_meshDirty && _meshFrame == CurrentFrameIndex) return;

        _meshDirty = false;
        _meshFrame = CurrentFrameIndex;

        _tessellateResults.Clear();
        SpriteLayerProcessor.ProcessLayer(Document.RootLayer, _tessellateResults);

        if (_tessellateResults.Count == 0)
        {
            DisposeMeshTexture();
            return;
        }

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var rb = Document.RasterBounds;
        int w = rb.Width;
        int h = rb.Height;
        if (w <= 0 || h <= 0) return;

        // Use the same FillPixelData path as export so preview matches rasterized output exactly
        using var pixels = new PixelData<Color32>(w, h);
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = new Vector2Int(-rb.X, -rb.Y);
        SpriteSkiaRenderer.FillPixelData(_tessellateResults, pixels, targetRect, sourceOffset, dpi, premul: true);

        var byteSpan = pixels.AsByteSpan();

        if (_meshTexture != null && _meshTextureW == w && _meshTextureH == h)
        {
            _meshTexture.Update(byteSpan);
        }
        else
        {
            DisposeMeshTexture();
            _meshTexture = Texture.Create(w, h, byteSpan, name: "sprite_preview");
            _meshTextureW = w;
            _meshTextureH = h;
        }
    }

    private void DrawMesh()
    {
        if (_meshTexture == null) return;
        DrawPreviewTexture(_meshTexture, sortGroup: 3, Workspace.XrayAlpha);
    }

    private void DrawColoredMesh(int sortGroup)
    {
        if (_meshTexture == null) return;
        DrawPreviewTexture(_meshTexture, sortGroup, alpha: 1f);
    }

    private void DrawPreviewTexture(Texture texture, int sortGroup, float alpha)
    {
        var bounds = Document.Bounds;
        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(sortGroup);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetBlendMode(BlendMode.Premultiplied);
            Graphics.SetColor(Color.White.WithAlpha(alpha));
            Graphics.Draw(bounds);
        }
    }

    private void DrawGeneratedImage(int sortGroup, float alpha)
    {
        var texture = Document.Generation?.Job.Texture;
        if (texture == null) return;

        var cs = Document.ConstrainedSize ?? new Vector2Int(256, 256);
        var ppu = EditorApplication.Config.PixelsPerUnitInv;

        var rect = new Rect(
            cs.X * ppu * -0.5f,
            cs.Y * ppu * -0.5f,
            cs.X * ppu,
            cs.Y * ppu);

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(sortGroup);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(alpha));
            Graphics.Draw(rect);
        }
    }

    private void DisposeMeshTexture()
    {
        _meshTexture?.Dispose();
        _meshTexture = null;
        _meshTextureW = 0;
        _meshTextureH = 0;
    }
}
