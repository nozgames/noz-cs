//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public enum PixelBrushType { None, Brush, Pencil }

public partial class PixelDocument : SpriteDocument
{
    public const int MaxBrushSize = 32;
    public const float MinBrushSize = 0.5f;
    public static readonly float MinBrushSizeSlider = MathF.Sqrt(MinBrushSize / MaxBrushSize);

    protected override TextureFilter DefaultTextureFilter => TextureFilter.Point;    

    private Color32 _brushColor = Color32.Black;
    private float _brushSize = 1;

    public Vector2Int CanvasSize { get; set; } = new(64, 64);
    public PixelData<byte>? SelectionMask { get; set; }

    public float BrushSize
    {
        get => _brushSize;
        set
        {
            _brushSize = Math.Clamp(value, MinBrushSize, MaxBrushSize);
            IncrementVersion();
        }
    }

    public Color32 BrushColor
    {
        get => _brushColor;
        set
        {
            _brushColor = value;
            IncrementVersion();
        }
    }
    
    public PixelBrushType BrushType { get; set; } = PixelBrushType.Brush;
    public PixelBrushType EraserType { get; set; } = PixelBrushType.Brush;
    public float BrushHardness { get; set; } = 1f;
    public bool AlphaLock { get; set; }
    public string ActiveLayerName { get; set; } = "";

    public override DocumentEditor CreateEditor() => new PixelEditor(this);

    public override Color32 GetPixelAt(System.Numerics.Vector2 worldPos)
    {
        System.Numerics.Matrix3x2.Invert(Transform, out var invTransform);
        var local = System.Numerics.Vector2.Transform(worldPos, invTransform);

        var ppu = (float)PixelsPerUnit;
        var w = CanvasSize.X;
        var h = CanvasSize.Y;
        var cw = w / ppu;
        var ch = h / ppu;
        var nx = (local.X + cw / 2) / cw;
        var ny = (local.Y + ch / 2) / ch;
        var px = (int)MathF.Floor(nx * w);
        var py = (int)MathF.Floor(ny * h);

        if (px < 0 || px >= w || py < 0 || py >= h)
            return default;

        var result = default(Color32);
        CompositePixelAt(Root, px, py, ref result);
        return result;
    }

    private static void CompositePixelAt(SpriteNode parent, int px, int py, ref Color32 result)
    {
        foreach (var child in parent.Children)
        {
            if (!child.Visible) continue;

            if (child.IsExpandable)
            {
                CompositePixelAt(child, px, py, ref result);
                continue;
            }

            if (child is not PixelLayer layer || layer.Pixels == null)
                continue;

            var src = layer.Pixels[px, py];
            if (src.A == 0) continue;

            if (result.A == 0)
            {
                result = src;
            }
            else
            {
                var sa = src.A / 255f;
                var da = result.A / 255f;
                var outA = sa + da * (1f - sa);
                if (outA > 0f)
                {
                    var invOutA = 1f / outA;
                    result = new Color32(
                        (byte)((src.R * sa + result.R * da * (1f - sa)) * invOutA),
                        (byte)((src.G * sa + result.G * da * (1f - sa)) * invOutA),
                        (byte)((src.B * sa + result.B * da * (1f - sa)) * invOutA),
                        (byte)(outA * 255f));
                }
            }
        }
    }

    public static Document? CreateNew(System.Numerics.Vector2? position = null)
    {
        return Project.New(AssetType.Sprite, BinaryExtension, null, stream =>
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteEmptyPixelSprite(w, 64, 64);
        }, position);
    }

    public RectInt? ComputeContentBounds()
    {
        var w = CanvasSize.X;
        var h = CanvasSize.Y;
        var minX = w;
        var minY = h;
        var maxX = 0;
        var maxY = 0;

        // todo: dont allocate here.
        var layers = new List<PixelLayer>();
        Root.Collect(layers, l => l.Pixels != null && (IsAnimated || l.Visible));

        foreach (var layer in layers)
        {
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (layer.Pixels![x, y].A == 0) continue;
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

    private void SyncCanvasToConstrainedSize()
    {
        if (!ConstrainedSize.HasValue) return;
        var cs = ConstrainedSize.Value;
        if (cs == CanvasSize) return;

        var offset = (cs - CanvasSize) / 2;

        Root.ForEach((PixelLayer layer) =>
        {
            if (layer.Pixels != null)
            {
                var resized = PixelDataResize.Resized(layer.Pixels, cs, offset);
                layer.Pixels.Dispose();
                layer.Pixels = resized;
            }
            else
            {
                layer.Pixels = new PixelData<Color32>(cs.X, cs.Y);
            }
        });

        if (SelectionMask != null)
        {
            var resized = PixelDataResize.Resized(SelectionMask, cs, offset);
            SelectionMask.Dispose();
            SelectionMask = resized;
        }

        CanvasSize = cs;
    }

    protected override void UpdateContentBounds()
    {
        SyncCanvasToConstrainedSize();

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
            RasterBounds = new RectInt(-ppu / 2, -ppu / 2, ppu, ppu);
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
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
        if (source is not PixelDocument src) return;
        CanvasSize = src.CanvasSize;

        SelectionMask?.Dispose();
        SelectionMask = src.SelectionMask?.Clone();

        ActiveLayerName = src.ActiveLayerName;
    }

    protected override void LoadContentMetadata(PropertySet meta)
    {
        BrushSize = Math.Clamp(meta.GetInt("sprite", "brush_size", 1), 1, 16);
        var brushColor = meta.GetColor("sprite", "brush_color", Color.Black);
        BrushColor = (Color32)brushColor;
        BrushType = (PixelBrushType)meta.GetInt("sprite", "brush_type", (int)PixelBrushType.Brush);
        EraserType = (PixelBrushType)meta.GetInt("sprite", "eraser_type", (int)PixelBrushType.Brush);
        BrushHardness = meta.GetFloat("sprite", "brush_hardness", 1f);
        AlphaLock = meta.GetBool("sprite", "alpha_lock", false);
        ActiveLayerName = meta.GetString("sprite", "active_layer", "");
    }

    protected override void SaveContentMetadata(PropertySet meta)
    {
        meta.SetFloat("sprite", "brush_size", BrushSize);
        meta.SetColor("sprite", "brush_color", (Color)BrushColor);
        meta.SetBool("sprite", "alpha_lock", AlphaLock);
        meta.SetInt("sprite", "brush_type", (int)BrushType);
        meta.SetInt("sprite", "eraser_type", (int)EraserType);
        meta.SetFloat("sprite", "brush_hardness", BrushHardness);

        if (!string.IsNullOrEmpty(ActiveLayerName))
            meta.SetString("sprite", "active_layer", ActiveLayerName);
        else
            meta.RemoveKey("sprite", "active_layer");
    }

    public override void Dispose()
    {
        SelectionMask?.Dispose();
        SelectionMask = null;
        base.Dispose();
    }

    public override void InspectorUI()
    {
        base.InspectorUI();

        using var section = Inspector.BeginSection("PIXEL");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginProperty("Animated"))
        {
            if (UI.Toggle(WidgetIds.AnimatedToggle, IsAnimated, EditorStyle.Inspector.Toggle))
            {
                Undo.Record(this);
                IsAnimated = !IsAnimated;
                MarkSpriteDirty();
            }
        }
    }
}
