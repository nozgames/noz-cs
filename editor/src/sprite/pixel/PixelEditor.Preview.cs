//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelEditor
{
    private const int PreviewCellSize = 25;
    private const int PreviewPadding = 1;
    private const int PreviewContentSize = PreviewCellSize - PreviewPadding * 2;
    private const int PreviewAtlasSize = 512;
    private const int PreviewCellsPerRow = PreviewAtlasSize / PreviewCellSize;
    private const int MaxPreviewSlots = PreviewCellsPerRow * PreviewCellsPerRow;

    private Atlas? _previewAtlas;
    private readonly byte[] _previewAtlasData = new byte[PreviewAtlasSize * PreviewAtlasSize * 4];
    private readonly List<Sprite> _previewSprites = [];
    private readonly List<int> _slotGenerations = [];
    private readonly Stack<int> _freePreviewSlots = new();
    private int _nextPreviewSlot;

    protected override Sprite? GetNodePreview(SpriteNode node)
    {
        if (node.PreviewIndex == -1 || node.PreviewIndex >= _slotGenerations.Count)
        {
            node.PreviewIndex = -1;
            AllocatePreviewSlot(node);
        }

        if (node.PreviewIndex < 0)
            return null;

        if (_slotGenerations[node.PreviewIndex] != node.PreviewGeneration)
            UpdatePreview(node);

        return _previewSprites[node.PreviewIndex];
    }

    private void AllocatePreviewSlot(SpriteNode node)
    {
        int slot;
        if (_freePreviewSlots.Count > 0)
            slot = _freePreviewSlots.Pop();
        else if (_nextPreviewSlot < MaxPreviewSlots)
            slot = _nextPreviewSlot++;
        else
            return;

        node.PreviewIndex = slot;

        EnsurePreviewAtlas();
        EnsurePreviewSprite(slot);

        while (_slotGenerations.Count <= slot)
            _slotGenerations.Add(-1);
    }

    private void EnsurePreviewAtlas()
    {
        if (_previewAtlas != null)
            return;

        Array.Clear(_previewAtlasData);
        _previewAtlas = Atlas.CreatePreview(
            "preview_atlas",
            PreviewAtlasSize, PreviewAtlasSize,
            _previewAtlasData);
    }

    private void EnsurePreviewSprite(int slot)
    {
        while (_previewSprites.Count <= slot)
        {
            var idx = _previewSprites.Count;
            var col = idx % PreviewCellsPerRow;
            var row = idx / PreviewCellsPerRow;
            var u0 = (col * PreviewCellSize + PreviewPadding) / (float)PreviewAtlasSize;
            var v0 = (row * PreviewCellSize + PreviewPadding) / (float)PreviewAtlasSize;
            var u1 = u0 + PreviewContentSize / (float)PreviewAtlasSize;
            var v1 = v0 + PreviewContentSize / (float)PreviewAtlasSize;

            var sprite = Sprite.Create(
                name: $"preview_{idx}",
                bounds: new RectInt(0, 0, PreviewContentSize, PreviewContentSize),
                pixelsPerUnit: PreviewContentSize,
                boneIndex: -1,
                frames: [new SpriteFrame(Rect.FromMinMax(u0, v0, u1, v1), Vector2Int.Zero, new Vector2Int(PreviewContentSize, PreviewContentSize))],
                atlas: _previewAtlas,
                filter: TextureFilter.Point);

            _previewSprites.Add(sprite);
        }
    }

    private void UpdatePreview(SpriteNode node)
    {
        _slotGenerations[node.PreviewIndex] = node.PreviewGeneration;
        var slot = node.PreviewIndex;
        var cellX = (slot % PreviewCellsPerRow) * PreviewCellSize + PreviewPadding;
        var cellY = (slot / PreviewCellsPerRow) * PreviewCellSize + PreviewPadding;

        // Clear content area
        ClearPreviewCell(cellX, cellY);

        if (node is PixelLayer layer)
            RenderLayerPreview(layer, cellX, cellY);
        else if (node is SpriteGroup group)
            RenderGroupPreview(group, cellX, cellY);

        // Upload the cell region
        _previewAtlas?.UpdateLayer(0, _previewAtlasData);
    }

    private void ClearPreviewCell(int cellX, int cellY)
    {
        for (var y = 0; y < PreviewContentSize; y++)
        {
            var rowStart = ((cellY + y) * PreviewAtlasSize + cellX) * 4;
            Array.Clear(_previewAtlasData, rowStart, PreviewContentSize * 4);
        }
    }

    private void RenderLayerPreview(PixelLayer layer, int cellX, int cellY)
    {
        if (layer.Pixels == null) return;

        var contentRect = ComputeLayerContentRect(layer);
        if (contentRect == null) return;

        var cr = contentRect.Value;
        BlitScaled(layer.Pixels, cr, cellX, cellY);
    }

    private void RenderGroupPreview(SpriteGroup group, int cellX, int cellY)
    {
        // Compute union content rect of all visible child layers
        RectInt? unionRect = null;
        CollectUnionRect(group, ref unionRect);
        if (unionRect == null) return;

        var ur = unionRect.Value;

        // Composite into a temp buffer at preview size, then copy to atlas
        var tempW = PreviewContentSize;
        var tempH = PreviewContentSize;
        Span<Color32> temp = stackalloc Color32[tempW * tempH];
        temp.Clear();

        CompositeScaled(group, ur, temp, tempW, tempH);

        // Write temp to atlas data
        for (var y = 0; y < tempH; y++)
        {
            for (var x = 0; x < tempW; x++)
            {
                var c = temp[y * tempW + x];
                var dstIdx = ((cellY + y) * PreviewAtlasSize + cellX + x) * 4;
                _previewAtlasData[dstIdx + 0] = c.R;
                _previewAtlasData[dstIdx + 1] = c.G;
                _previewAtlasData[dstIdx + 2] = c.B;
                _previewAtlasData[dstIdx + 3] = c.A;
            }
        }
    }

    private void CollectUnionRect(SpriteNode node, ref RectInt? unionRect)
    {
        foreach (var child in node.Children)
        {
            if (!child.Visible) continue;

            if (child is PixelLayer layer)
            {
                var lr = ComputeLayerContentRect(layer);
                if (lr != null)
                    unionRect = unionRect == null ? lr.Value : RectIntUnion(unionRect.Value, lr.Value);
            }
            else if (child is SpriteGroup group)
            {
                CollectUnionRect(group, ref unionRect);
            }
        }
    }

    private static RectInt RectIntUnion(RectInt a, RectInt b)
    {
        var minX = Math.Min(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
        var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    private void CompositeScaled(SpriteNode node, in RectInt sourceRect, Span<Color32> dest, int destW, int destH)
    {
        foreach (var child in node.Children)
        {
            if (!child.Visible) continue;

            if (child is PixelLayer layer && layer.Pixels != null)
                CompositeScaled(layer.Pixels, sourceRect, dest, destW, destH);
            else if (child is SpriteGroup group)
                CompositeScaled(group, sourceRect, dest, destW, destH);
        }
    }

    private void BlitScaled(PixelData<Color32> pixels, in RectInt sourceRect, int cellX, int cellY)
    {
        // Compute fit scale preserving aspect ratio
        var scaleX = (float)PreviewContentSize / sourceRect.Width;
        var scaleY = (float)PreviewContentSize / sourceRect.Height;
        var scale = MathF.Min(scaleX, scaleY);
        var dstW = Math.Min((int)MathF.Ceiling(sourceRect.Width * scale), PreviewContentSize);
        var dstH = Math.Min((int)MathF.Ceiling(sourceRect.Height * scale), PreviewContentSize);
        var offsetX = (PreviewContentSize - dstW) / 2;
        var offsetY = (PreviewContentSize - dstH) / 2;

        for (var y = 0; y < dstH; y++)
        {
            var srcY = sourceRect.Y + y * (sourceRect.Height - 1) / Math.Max(dstH - 1, 1);
            if (srcY >= pixels.Height) continue;

            for (var x = 0; x < dstW; x++)
            {
                var srcX = sourceRect.X + x * (sourceRect.Width - 1) / Math.Max(dstW - 1, 1);
                if (srcX >= pixels.Width) continue;

                var c = pixels[srcX, srcY];
                if (c.A == 0) continue;

                var dstIdx = ((cellY + offsetY + y) * PreviewAtlasSize + cellX + offsetX + x) * 4;
                _previewAtlasData[dstIdx + 0] = c.R;
                _previewAtlasData[dstIdx + 1] = c.G;
                _previewAtlasData[dstIdx + 2] = c.B;
                _previewAtlasData[dstIdx + 3] = c.A;
            }
        }
    }

    private static void CompositeScaled(PixelData<Color32> pixels, in RectInt sourceRect, Span<Color32> dest, int destW, int destH)
    {
        var scaleX = (float)destW / sourceRect.Width;
        var scaleY = (float)destH / sourceRect.Height;
        var scale = MathF.Min(scaleX, scaleY);
        var dstW = Math.Min((int)MathF.Ceiling(sourceRect.Width * scale), destW);
        var dstH = Math.Min((int)MathF.Ceiling(sourceRect.Height * scale), destH);
        var offsetX = (destW - dstW) / 2;
        var offsetY = (destH - dstH) / 2;

        for (var y = 0; y < dstH; y++)
        {
            var srcY = sourceRect.Y + y * (sourceRect.Height - 1) / Math.Max(dstH - 1, 1);
            if (srcY >= pixels.Height) continue;

            for (var x = 0; x < dstW; x++)
            {
                var srcX = sourceRect.X + x * (sourceRect.Width - 1) / Math.Max(dstW - 1, 1);
                if (srcX >= pixels.Width) continue;

                var src = pixels[srcX, srcY];
                if (src.A == 0) continue;

                ref var dst = ref dest[(offsetY + y) * destW + offsetX + x];
                if (dst.A == 0)
                {
                    dst = src;
                }
                else
                {
                    var sa = src.A / 255f;
                    var da = dst.A / 255f;
                    var outA = sa + da * (1f - sa);
                    if (outA > 0f)
                    {
                        var inv = 1f / outA;
                        dst = new Color32(
                            (byte)((src.R * sa + dst.R * da * (1f - sa)) * inv),
                            (byte)((src.G * sa + dst.G * da * (1f - sa)) * inv),
                            (byte)((src.B * sa + dst.B * da * (1f - sa)) * inv),
                            (byte)(outA * 255f));
                    }
                }
            }
        }
    }

    private static RectInt? ComputeLayerContentRect(PixelLayer layer)
    {
        if (layer.Pixels == null) return null;

        var w = layer.Pixels.Width;
        var h = layer.Pixels.Height;
        var minX = w;
        var minY = h;
        var maxX = 0;
        var maxY = 0;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (layer.Pixels[x, y].A == 0) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x >= maxX) maxX = x + 1;
                if (y >= maxY) maxY = y + 1;
            }
        }

        if (minX >= maxX) return null;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    private void ResetPreviews()
    {
        Document.Root.ForEach((SpriteNode n) => n.PreviewIndex = -1);
        _nextPreviewSlot = 0;
        _freePreviewSlots.Clear();
        _slotGenerations.Clear();
    }

    private void DisposePreviews()
    {
        foreach (var sprite in _previewSprites)
            sprite.Dispose();
        _previewSprites.Clear();

        _previewAtlas?.Dispose();
        _previewAtlas = null;

        _nextPreviewSlot = 0;
        _freePreviewSlots.Clear();
        _slotGenerations.Clear();
    }
}
