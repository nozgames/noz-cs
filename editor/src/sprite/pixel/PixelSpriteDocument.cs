//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteDocument : SpriteDocument
{
    private static partial class WidgetIds
    {
        public static partial WidgetId PixelsPerUnit { get; }
    }

    public int? PixelsPerUnitOverride { get; set; }

    protected override int PixelsPerUnit => PixelsPerUnitOverride ?? 32;
    protected override TextureFilter TextureFilter => TextureFilter.Point;

    public Vector2Int CanvasSize { get; set; } = new(64, 64);
    public PixelData<byte>? SelectionMask { get; set; }
    public int BrushSize { get; set; } = 1;
    public Color32 BrushColor { get; set; } = Color32.Black;
    public bool AlphaLock { get; set; }
    public string ActiveLayerName { get; set; } = "";

    public override DocumentEditor CreateEditor() => new PixelSpriteEditor(this);

    public static Document? CreateNew(System.Numerics.Vector2? position = null)
    {
        return DocumentManager.New(AssetType.Sprite, Extension, null, position, WriteNewFile);
    }

    public static void WriteNewFile(StreamWriter writer)
    {
        writer.WriteLine("type raster");
        writer.WriteLine("canvas 64 64");
        writer.WriteLine();
        writer.WriteLine("layer \"Layer 1\" {");
        writer.WriteLine("}");
    }

    public RectInt? ComputeContentBounds()
    {
        var w = CanvasSize.X;
        var h = CanvasSize.Y;
        var minX = w;
        var minY = h;
        var maxX = 0;
        var maxY = 0;

        // For animated sprites, include all layers regardless of visibility
        // since different frames show different layers.
        var hasAnimation = AnimFrames.Count > 0;

        foreach (var child in Root.Children)
        {
            if (child is not PixelLayer layer || layer.Pixels == null)
                continue;

            if (!hasAnimation && !layer.Visible)
                continue;

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
        }

        if (minX >= maxX) return null;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    protected override void UpdateContentBounds()
    {
        var ppu = PixelsPerUnit;
        var w = CanvasSize.X;
        var h = CanvasSize.Y;

        var contentBounds = ComputeContentBounds();
        if (contentBounds.HasValue)
        {
            var cb = contentBounds.Value;
            RasterBounds = new RectInt(
                cb.X - w / 2,
                cb.Y - h / 2,
                cb.Width,
                cb.Height);
        }
        else
        {
            RasterBounds = new RectInt(-w / 2, -h / 2, w, h);
        }

        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            var fw = cs.X / (float)ppu;
            var fh = cs.Y / (float)ppu;
            Bounds = new Rect(-fw / 2, -fh / 2, fw, fh);
        }
        else
        {
            var fw = RasterBounds.Width / (float)ppu;
            var fh = RasterBounds.Height / (float)ppu;
            Bounds = new Rect(
                RasterBounds.X / (float)ppu,
                RasterBounds.Y / (float)ppu,
                fw, fh);
        }
    }

    protected override void CloneContent(SpriteDocument source)
    {
        if (source is not PixelSpriteDocument src) return;
        CanvasSize = src.CanvasSize;
        PixelsPerUnitOverride = src.PixelsPerUnitOverride;

        SelectionMask?.Dispose();
        SelectionMask = src.SelectionMask?.Clone();

        ActiveLayerName = src.ActiveLayerName;
    }

    protected override void LoadContentMetadata(PropertySet meta)
    {
        var ppu = meta.GetInt("sprite", "ppu", 0);
        PixelsPerUnitOverride = ppu > 0 ? ppu : null;

        BrushSize = Math.Clamp(meta.GetInt("sprite", "brush_size", 1), 1, 16);
        var brushColor = meta.GetColor("sprite", "brush_color", Color.Black);
        BrushColor = (Color32)brushColor;
        AlphaLock = meta.GetBool("sprite", "alpha_lock", false);
        ActiveLayerName = meta.GetString("sprite", "active_layer", "");
    }

    protected override void SaveContentMetadata(PropertySet meta)
    {
        if (PixelsPerUnitOverride.HasValue)
            meta.SetInt("sprite", "ppu", PixelsPerUnitOverride.Value);
        else
            meta.RemoveKey("sprite", "ppu");

        meta.SetInt("sprite", "brush_size", BrushSize);
        meta.SetColor("sprite", "brush_color", (Color)BrushColor);
        meta.SetBool("sprite", "alpha_lock", AlphaLock);

        if (!string.IsNullOrEmpty(ActiveLayerName))
            meta.SetString("sprite", "active_layer", ActiveLayerName);
        else
            meta.RemoveKey("sprite", "active_layer");
    }

    public override void InspectorUI()
    {
        Inspector.Section("PIXEL SPRITE", icon: Def.Icon?.Invoke());
        if (!Inspector.IsSectionCollapsed)
        {
            using (Inspector.BeginProperty("Pixels Per Unit"))
            {
                var current = PixelsPerUnitOverride ?? 32;
                var label = PixelsPerUnitOverride.HasValue ? $"{current}" : $"{current} (Default)";
                UI.DropDown(WidgetIds.PixelsPerUnit, () =>
                [
                    ..new[] { 8, 16, 32, 64, 128 }.Select(v => new PopupMenuItem
                    {
                        Label = v == 32 ? "32 (Default)" : $"{v}",
                        Handler = () =>
                        {
                            Undo.Record(this);
                            PixelsPerUnitOverride = v == 32 ? null : v;
                            UpdateBounds();
                            AssetManifest.IsModified = true;
                        }
                    })
                ], label);
            }
        }
    }

    public override void Dispose()
    {
        SelectionMask?.Dispose();
        SelectionMask = null;
        base.Dispose();
    }
}
