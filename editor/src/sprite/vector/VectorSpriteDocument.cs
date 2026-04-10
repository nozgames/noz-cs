//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class VectorSpriteDocument : SpriteDocument
{
    public static Document? CreateNew(System.Numerics.Vector2? position = null)
    {
        return DocumentManager.New(AssetType.Sprite, Extension, null, position);
    }

    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public SpriteStrokeJoin CurrentStrokeJoin;
    public SpritePathOperation CurrentOperation;

    private readonly List<SpritePath> _visiblePathsCache = new();

    public override DocumentEditor CreateEditor() => new VectorSpriteEditor(this);

    public override Color32 GetPixelAt(Vector2 worldPos)
    {
        var size = RasterBounds.Size;
        if (size.X <= 0 || size.Y <= 0)
            return default;

        Matrix3x2.Invert(Transform, out var inv);
        var local = Vector2.Transform(worldPos, inv);

        var px = (int)((local.X - Bounds.X) / Bounds.Width * size.X);
        var py = (int)((local.Y - Bounds.Y) / Bounds.Height * size.Y);

        if (px < 0 || px >= size.X || py < 0 || py >= size.Y)
            return default;

        using var pixels = new PixelData<Color32>(size);
        var targetRect = new RectInt(Vector2Int.Zero, size);
        var sourceOffset = -RasterBounds.Position;
        RasterizeLayer(Root, pixels, targetRect, sourceOffset, PixelsPerUnit);

        return pixels[px, py];
    }

    protected override void UpdateContentBounds()
    {
        _visiblePathsCache.Clear();
        Root.CollectVisiblePaths(_visiblePathsCache);

        if (_visiblePathsCache.Count == 0)
        {
            SetDefaultBounds();
            return;
        }

        var first = true;
        var bounds = Rect.Zero;

        foreach (var path in _visiblePathsCache)
        {
            path.UpdateSamples();
            path.UpdateBounds();

            if (path.TotalAnchorCount == 0)
                continue;

            if (path.Operation != SpritePathOperation.Normal)
                continue;

            if (first)
            {
                bounds = path.Bounds;
                first = false;
            }
            else
            {
                var pb = path.Bounds;
                var minX = MathF.Min(bounds.X, pb.X);
                var minY = MathF.Min(bounds.Y, pb.Y);
                var maxX = MathF.Max(bounds.Right, pb.Right);
                var maxY = MathF.Max(bounds.Bottom, pb.Bottom);
                bounds = Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));
            }
        }

        if (first)
        {
            SetDefaultBounds();
            return;
        }

        var dpi = PixelsPerUnit;
        var rMinX = SnapFloor(bounds.X * dpi);
        var rMinY = SnapFloor(bounds.Y * dpi);
        var rMaxX = SnapCeil(bounds.Right * dpi);
        var rMaxY = SnapCeil(bounds.Bottom * dpi);
        RasterBounds = new RectInt(rMinX, rMinY, rMaxX - rMinX, rMaxY - rMinY);

        Bounds = bounds;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            SetDefaultBounds();
            return;
        }

        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            RasterBounds = new RectInt(
                -cs.X / 2,
                -cs.Y / 2,
                cs.X,
                cs.Y);
        }

        Bounds = RasterBounds.ToRect().Scale(1.0f / PixelsPerUnit);
    }

    protected override void CloneContent(SpriteDocument source)
    {
        if (source is not VectorSpriteDocument src) return;
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;
        CurrentStrokeWidth = src.CurrentStrokeWidth;
        CurrentStrokeJoin = src.CurrentStrokeJoin;
        CurrentOperation = src.CurrentOperation;
    }
}
