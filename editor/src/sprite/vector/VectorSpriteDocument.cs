//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class VectorSpriteDocument : SpriteDocument
{
    protected override int PixelsPerUnit => EditorApplication.Config.PixelsPerUnit;
    protected override TextureFilter TextureFilter => TextureFilter.Linear;

    public Color32 CurrentFillColor = Color32.White;
    public SpriteFillType CurrentFillType;
    public SpriteFillGradient CurrentFillGradient;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public SpriteStrokeJoin CurrentStrokeJoin;
    public SpritePathOperation CurrentOperation;

    private readonly List<SpritePath> _visiblePathsCache = new();

    public override DocumentEditor? CreateEditor() => new VectorSpriteEditor(this);

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

        var dpi = EditorApplication.Config.PixelsPerUnit;
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

        Bounds = RasterBounds.ToRect().Scale(1.0f / EditorApplication.Config.PixelsPerUnit);
    }

    protected override void CloneContent(SpriteDocument source)
    {
        if (source is not VectorSpriteDocument src) return;
        CurrentFillColor = src.CurrentFillColor;
        CurrentFillType = src.CurrentFillType;
        CurrentFillGradient = src.CurrentFillGradient;
        CurrentStrokeColor = src.CurrentStrokeColor;
        CurrentStrokeWidth = src.CurrentStrokeWidth;
        CurrentStrokeJoin = src.CurrentStrokeJoin;
        CurrentOperation = src.CurrentOperation;
    }
}
