//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class SpriteEditor
{
    private Texture? _onionTexture;
    private int _onionTextureW;
    private int _onionTextureH;
    private readonly List<(SpriteLayer layer, bool visible)> _savedVisibility = new();
    private int _onionFrame = -1;

    private void DrawOnionSkin()
    {
        if (!_onionSkin || _isPlaying || Document.AnimFrames.Count <= 1)
            return;

        var currentFi = CurrentFrameIndex;
        if (_onionFrame != currentFi || _meshDirty)
        {
            _onionFrame = currentFi;
            UpdateOnionTexture(currentFi);
        }

        if (_onionTexture != null)
            DrawPreviewTexture(_onionTexture, sortGroup: 4, alpha: 1f);
    }

    private void UpdateOnionTexture(int currentFi)
    {
        var frameCount = Document.AnimFrames.Count;
        var prevFi = (currentFi - 1 + frameCount) % frameCount;
        var nextFi = (currentFi + 1) % frameCount;

        var prevColor = new Color(1f, 0.3f, 0.3f, 0.1f);
        var nextColor = new Color(0.3f, 1f, 0.3f, 0.1f);

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var rb = Document.RasterBounds;
        int w = rb.Width;
        int h = rb.Height;
        if (w <= 0 || h <= 0)
        {
            DisposeOnionTexture();
            return;
        }

        // Save layer visibility before mutating
        _savedVisibility.Clear();
        Document.RootLayer.ForEach((SpriteLayer layer) =>
        {
            if (layer != Document.RootLayer)
                _savedVisibility.Add((layer, layer.Visible));
        });

        unsafe
        {
            var pixels = stackalloc Color32[w * h];
            for (int i = 0; i < w * h; i++)
                pixels[i] = default;

            RenderOnionFrame(prevFi, prevColor, w, h, dpi, rb, pixels);
            RenderOnionFrame(nextFi, nextColor, w, h, dpi, rb, pixels);

            // Restore original visibility
            foreach (var (layer, visible) in _savedVisibility)
                layer.Visible = visible;

            var byteSpan = new ReadOnlySpan<byte>(pixels, w * h * 4);

            if (_onionTexture != null && _onionTextureW == w && _onionTextureH == h)
            {
                _onionTexture.Update(byteSpan);
            }
            else
            {
                DisposeOnionTexture();
                _onionTexture = Texture.Create(w, h, byteSpan, name: "onion_skin");
                _onionTextureW = w;
                _onionTextureH = h;
            }
        }
    }

    private unsafe void RenderOnionFrame(int frameIndex, Color tint, int w, int h, int dpi, RectInt rb, Color32* pixels)
    {
        if (frameIndex < 0 || frameIndex >= Document.AnimFrames.Count)
            return;

        Document.AnimFrames[frameIndex].ApplyVisibility(Document.RootLayer);

        _tessellateResults.Clear();
        SpriteLayerProcessor.ProcessLayer(Document.RootLayer, _tessellateResults);
        if (_tessellateResults.Count == 0) return;

        SpriteSkiaRenderer.RenderToPixelsTintedPremul(
            _tessellateResults, w, h, pixels, tint,
            dpi, dpi,
            -rb.X, -rb.Y);
    }

    private void DisposeOnionTexture()
    {
        _onionTexture?.Dispose();
        _onionTexture = null;
        _onionTextureW = 0;
        _onionTextureH = 0;
    }
}
