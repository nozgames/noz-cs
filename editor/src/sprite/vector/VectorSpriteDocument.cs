//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Clipper2Lib;

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

    public Color32 OutlineColor = new(0, 0, 0, 0);
    public byte OutlineSize;

    public bool HasOutline => OutlineColor.A > 0 && OutlineSize > 0;

    private readonly List<SpritePath> _visiblePathsCache = new();

    public override DocumentEditor CreateEditor() => new VectorSpriteEditor(this);

    public override Color32 GetPixelAt(Vector2 worldPos)
    {
        var size = RasterBounds.Size;
        if (size.X <= 0 || size.Y <= 0)
            return default;

        Matrix3x2.Invert(Transform, out var inv);
        var local = Vector2.Transform(worldPos, inv);

        if (ShowTiling && Bounds.Width > 0 && Bounds.Height > 0)
        {
            local.X = Bounds.X + FloorMod(local.X - Bounds.X, Bounds.Width);
            local.Y = Bounds.Y + FloorMod(local.Y - Bounds.Y, Bounds.Height);
        }

        var px = (int)((local.X - Bounds.X) / Bounds.Width * size.X);
        var py = (int)((local.Y - Bounds.Y) / Bounds.Height * size.Y);

        if (px < 0 || px >= size.X || py < 0 || py >= size.Y)
            return default;

        using var pixels = new PixelData<Color32>(size);
        var targetRect = new RectInt(Vector2Int.Zero, size);
        var sourceOffset = -RasterBounds.Position;
        RasterizeLayer(Root, pixels, targetRect, sourceOffset, PixelsPerUnit, clipRect: null, outlineSource: this);

        return pixels[px, py];
    }

    private static float FloorMod(float a, float b) => ((a % b) + b) % b;

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
        OutlineColor = src.OutlineColor;
        OutlineSize = src.OutlineSize;
    }

    protected override void LoadContentMetadata(PropertySet meta)
    {
        var packed = meta.GetInt("sprite", "outline_color", 0);
        OutlineColor = packed == 0
            ? new Color32(0, 0, 0, 0)
            : new Color32(
                (byte)((packed >> 24) & 0xFF),
                (byte)((packed >> 16) & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)(packed & 0xFF));
        OutlineSize = (byte)meta.GetInt("sprite", "outline_size", 0);
    }

    protected override void SaveContentMetadata(PropertySet meta)
    {
        if (HasOutline)
        {
            var packed = (OutlineColor.R << 24) | (OutlineColor.G << 16) | (OutlineColor.B << 8) | OutlineColor.A;
            meta.SetInt("sprite", "outline_color", packed);
            meta.SetInt("sprite", "outline_size", OutlineSize);
        }
        else
        {
            meta.RemoveKey("sprite", "outline_color");
            meta.RemoveKey("sprite", "outline_size");
        }
    }

    internal bool TryBuildOutlineResult(List<LayerPathResult> results, out LayerPathResult outline)
    {
        outline = default;
        if (!HasOutline || results.Count == 0) return false;

        var union = new PathsD(results[0].Contours);
        for (var i = 1; i < results.Count; i++)
        {
            if (results[i].Contours.Count == 0) continue;
            union = Clipper.BooleanOp(ClipType.Union,
                union, results[i].Contours, FillRule.NonZero,
                precision: SpriteGroupProcessor.ClipperPrecision);
        }
        if (union.Count == 0) return false;

        var inflated = Clipper.InflatePaths(union,
            OutlineSize * SpritePath.StrokeScale,
            JoinType.Round, EndType.Polygon,
            precision: SpriteGroupProcessor.ClipperPrecision);
        if (inflated.Count == 0) return false;

        outline = new LayerPathResult(inflated, OutlineColor, false);
        return true;
    }
}
