//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteDocument : SpriteDocument
{
    protected override int PixelsPerUnit => 32;
    protected override TextureFilter TextureFilter => TextureFilter.Point;

    public Vector2Int CanvasSize { get; set; } = new(64, 64);
    public PixelData<byte>? SelectionMask { get; set; }
    public int BrushSize { get; set; } = 1;
    public Color32 BrushColor { get; set; } = Color32.Black;
    public bool AlphaLock { get; set; }
    public string ActiveLayerName { get; set; } = "";

    public override DocumentEditor? CreateEditor() => new PixelSpriteEditor(this);

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

        foreach (var child in Root.Children)
        {
            if (child is not PixelLayer layer || !layer.Visible || layer.Pixels == null)
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
        const int ppu = 32;
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
            RasterBounds = new RectInt(-ppu / 2, -ppu / 2, ppu, ppu);
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

        SelectionMask?.Dispose();
        SelectionMask = src.SelectionMask?.Clone();

        ActiveLayerName = src.ActiveLayerName;
    }

    protected override void LoadContentMetadata(PropertySet meta)
    {
        BrushSize = Math.Clamp(meta.GetInt("sprite", "pixel_brush_size", 1), 1, 16);
        var brushColor = meta.GetColor("sprite", "pixel_brush_color", Color.Black);
        BrushColor = (Color32)brushColor;
        AlphaLock = meta.GetBool("sprite", "pixel_alpha_lock", false);
        ActiveLayerName = meta.GetString("sprite", "pixel_active_layer", "");
    }

    protected override void SaveContentMetadata(PropertySet meta)
    {
        meta.SetInt("sprite", "pixel_brush_size", BrushSize);
        meta.SetColor("sprite", "pixel_brush_color", (Color)BrushColor);
        meta.SetBool("sprite", "pixel_alpha_lock", AlphaLock);

        if (!string.IsNullOrEmpty(ActiveLayerName))
            meta.SetString("sprite", "pixel_active_layer", ActiveLayerName);
        else
            meta.RemoveKey("sprite", "pixel_active_layer");
    }

    public override void Dispose()
    {
        SelectionMask?.Dispose();
        SelectionMask = null;
        base.Dispose();
    }
}
