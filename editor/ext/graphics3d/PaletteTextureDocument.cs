//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using NoZ;
using NoZ.Editor;
using Color = NoZ.Color;

namespace NoZ.Editor.Graphics3D;

/// <summary>
/// An editable square color grid whose cells occupy 8×8 pixels in the PNG.
/// Construction data is stored in metadata, and the exported asset is a
/// regular runtime Texture.
/// </summary>
public partial class PaletteTextureDocument : Document
{
    public const int CellPixelSize = 8;
    public const int DefaultSize = 128;
    public const int MaxSize = 2048;

    internal static readonly int[] SizeOptions = [8, 16, 32, 64, 128, 256, 512, 1024, 2048];

    private static partial class WidgetIds
    {
        public static partial WidgetId Size { get; }
        public static partial WidgetId Filter { get; }
    }

    private Color32[] _pixels = [];
    private Texture? _previewTexture;
    private bool _previewDirty = true;

    public override bool CanSave => true;
    public int Size { get; private set; } = DefaultSize;
    public int GridSize => Size / CellPixelSize;
    public TextureFilter Filter { get; private set; } = TextureFilter.Point;

    public PaletteTextureDocument()
    {
        ResetPixels(DefaultSize, Color32.Transparent);
        UpdateBounds();
    }

    public static void RegisterDef()
    {
        DocumentDef<PaletteTextureDocument>.Register(new DocumentDef
        {
            Type = AssetType.Texture,
            Name = "Palette Texture",
            Extensions = [".png"],
            Factory = _ => new PaletteTextureDocument(),
            EditorFactory = doc => new PaletteTextureEditor((PaletteTextureDocument)doc),
            CreateNew = position => CreateNew(position: position),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    public static Document? CreateNew(string? name = null, System.Numerics.Vector2? position = null)
    {
        var imageDoc = Project.New(AssetType.Texture, ".png", name, stream =>
        {
            using var image = new Image<Rgba32>(DefaultSize, DefaultSize);
            image.SaveAsPng(stream);
        }, position);

        if (imageDoc == null)
            return null;

        var doc = Project.ChangeType(imageDoc, DocumentDef<PaletteTextureDocument>.Def);
        doc?.SaveMetadata();
        return doc;
    }

    public override void Load()
    {
        LoadPixelsFromImage();
        UpdateBounds();
        InvalidatePreview(recreate: true);
    }

    public override void Reload()
    {
        LoadPixelsFromImage();
        UpdateBounds();
        InvalidatePreview(recreate: true);
    }

    public override void LoadMetadata(PropertySet meta)
    {
        var filter = meta.GetString("texture", "filter", "point");
        Filter = filter.Equals("linear", StringComparison.OrdinalIgnoreCase)
            ? TextureFilter.Linear
            : TextureFilter.Point;

        var version = meta.GetInt("palette_texture", "version", 0);
        if (version == 1)
        {
            LoadLegacyMetadata(meta);
        }
        else if (version >= 2)
        {
            var size = NormalizeSize(meta.GetInt("palette_texture", "size", Size));
            ResetPixels(size, Color32.Transparent);

            for (var y = 0; y < GridSize; y++)
                ParseMetadataRow(meta.GetString("palette_texture", $"row_{y}", ""), y);

            if (version >= 3)
                LoadGradientMetadata(meta);
        }

        UpdateBounds();
        InvalidatePreview(recreate: true);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetString("texture", "filter", Filter == TextureFilter.Point ? "point" : "linear");

        meta.ClearGroup("palette_texture");
        meta.SetInt("palette_texture", "version", 3);
        meta.SetInt("palette_texture", "size", Size);
        meta.SetInt("palette_texture", "cell_pixels", CellPixelSize);

        var row = new StringBuilder(GridSize * 9);
        for (var y = 0; y < GridSize; y++)
        {
            row.Clear();
            for (var x = 0; x < GridSize; x++)
            {
                if (x > 0)
                    row.Append(' ');

                var color = _pixels[y * GridSize + x];
                row.Append(color.R.ToString("X2", CultureInfo.InvariantCulture));
                row.Append(color.G.ToString("X2", CultureInfo.InvariantCulture));
                row.Append(color.B.ToString("X2", CultureInfo.InvariantCulture));
                row.Append(color.A.ToString("X2", CultureInfo.InvariantCulture));
            }

            meta.SetString("palette_texture", $"row_{y}", row.ToString());
        }

        SaveGradientMetadata(meta);
    }

    protected override void Save(Stream stream)
    {
        var pixels = CreateTexturePixels();
        using var image = Image.LoadPixelData<Rgba32>(MemoryMarshal.AsBytes(pixels.AsSpan()), Size, Size);
        image.SaveAsPng(stream);
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        // Export may run before Project.LoadAll(), so reconstruct from the PNG
        // and metadata instead of relying on this document's in-memory state.
        using var source = new PaletteTextureDocument
        {
            Name = Name,
            Path = Path
        };
        source.Load();
        source.LoadMetadata(meta);

        using var writer = new BinaryWriter(File.Create(outputPath));
        var pixels = source.CreateTexturePixels();
        TextureAssetWriter.WriteRgba8(writer, source.Size, source.Size, source.Filter, MemoryMarshal.AsBytes(pixels.AsSpan()));
    }

    public override void Clone(Document source)
    {
        var src = (PaletteTextureDocument)source;
        Size = src.Size;
        Filter = src.Filter;
        _pixels = [.. src._pixels];
        _gradientCells = new(src._gradientCells);
        UpdateBounds();
        InvalidatePreview(recreate: true);
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();
        InvalidatePreview();
    }

    public Color32 GetPixel(int index) =>
        (uint)index < (uint)_pixels.Length ? _pixels[index] : Color32.Transparent;

    public void SetPixels(IReadOnlyList<int> indices, Color32 color)
    {
        foreach (var index in indices)
        {
            if ((uint)index < (uint)_pixels.Length)
            {
                _pixels[index] = color;
                _gradientCells.Remove(index);
            }
        }

        InvalidatePreview();
    }

    public void FillGradient(IReadOnlyList<int> stops)
    {
        if (stops.Count < 2)
            return;

        var stopColors = new Color32[stops.Count];
        for (var i = 0; i < stops.Count; i++)
            stopColors[i] = GetPixel(stops[i]);

        var coverage = new Dictionary<int, List<GradientSegment>>();
        for (var segment = 0; segment < stops.Count - 1; segment++)
        {
            var from = stops[segment];
            var to = stops[segment + 1];
            if ((uint)from >= (uint)_pixels.Length || (uint)to >= (uint)_pixels.Length)
                continue;

            var x0 = from % GridSize;
            var y0 = from / GridSize;
            var x1 = to % GridSize;
            var y1 = to / GridSize;
            var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));

            if (steps == 0)
                continue;

            var gradient = new GradientSegment(x0, y0, x1, y1, stopColors[segment], stopColors[segment + 1]);

            for (var step = 0; step <= steps; step++)
            {
                var t = step / (float)steps;
                var x = (int)MathF.Round(x0 + (x1 - x0) * t);
                var y = (int)MathF.Round(y0 + (y1 - y0) * t);
                var index = y * GridSize + x;
                if (!coverage.TryGetValue(index, out var segments))
                    coverage[index] = segments = [];
                segments.Add(gradient);
            }
        }

        foreach (var (index, segments) in coverage)
        {
            var gradient = new GradientCell([.. segments]);
            _gradientCells[index] = gradient;
            _pixels[index] = gradient.Sample(
                index % GridSize * CellPixelSize + CellPixelSize / 2,
                index / GridSize * CellPixelSize + CellPixelSize / 2);
        }

        InvalidatePreview();
    }

    public void Resize(int size)
    {
        size = NormalizeSize(size);
        if (size == Size || !CanResize(size))
            return;

        var oldSize = GridSize;
        var newSize = size / CellPixelSize;
        var oldPixels = _pixels;
        var newPixels = new Color32[newSize * newSize];
        var copySize = Math.Min(oldSize, newSize);

        for (var y = 0; y < copySize; y++)
            Array.Copy(oldPixels, y * oldSize, newPixels, y * newSize, copySize);

        _gradientCells = _gradientCells
            .Where(pair => pair.Key % oldSize < newSize && pair.Key / oldSize < newSize)
            .ToDictionary(pair => pair.Key / oldSize * newSize + pair.Key % oldSize, pair => pair.Value);

        Size = size;
        _pixels = newPixels;
        UpdateBounds();
        InvalidatePreview(recreate: true);
    }

    public bool CanResize(int size)
    {
        var newSize = NormalizeSize(size) / CellPixelSize;
        if (newSize >= GridSize)
            return true;

        for (var y = 0; y < GridSize; y++)
        for (var x = 0; x < GridSize; x++)
        {
            if ((x >= newSize || y >= newSize) &&
                (_pixels[y * GridSize + x] != Color32.Transparent || _gradientCells.ContainsKey(y * GridSize + x)))
                return false;
        }

        return true;
    }

    public void SetFilter(TextureFilter filter)
    {
        if (Filter == filter)
            return;

        Filter = filter;
        InvalidatePreview(recreate: true);
    }

    public override Color32 GetPixelAt(System.Numerics.Vector2 worldPos)
    {
        var x = (int)MathF.Floor((worldPos.X - Position.X + 0.5f) * GridSize);
        var y = (int)MathF.Floor((worldPos.Y - Position.Y + 0.5f) * GridSize);
        if ((uint)x >= (uint)GridSize || (uint)y >= (uint)GridSize)
            return Color32.Transparent;
        return _pixels[y * GridSize + x];
    }

    public override void Draw()
    {
        DrawOrigin();
        EnsurePreviewTexture();
        if (_previewTexture == null)
        {
            DrawBounds();
            return;
        }

        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTexture(_previewTexture);
            Graphics.SetTextureFilter(Filter);
            Graphics.SetColor(Color.White);
            Graphics.Draw(Bounds);
        }
    }

    public override bool DrawThumbnail()
    {
        EnsurePreviewTexture();
        if (_previewTexture == null)
            return false;

        UI.Image(_previewTexture, ImageStyle.Center);
        return true;
    }

    public override void InspectorUI()
    {
        if (EditorInspector.IsSectionCollapsed)
            return;

        using (EditorInspector.BeginProperty("Size"))
            DrawSizeDropDown(WidgetIds.Size, ResizeWithUndo);

        using (EditorInspector.BeginProperty("Color Grid"))
            UI.Text($"{GridSize} × {GridSize}");

        using (EditorInspector.BeginProperty("Cell Pixels"))
            UI.Text($"{CellPixelSize} × {CellPixelSize}");

        using (EditorInspector.BeginProperty("Filter"))
            DrawFilterDropDown(WidgetIds.Filter, SetFilterWithUndo);
    }

    public override void Dispose()
    {
        DisposePreview();
        base.Dispose();
    }

    internal void DrawSizeDropDown(WidgetId id, Action<int> handler)
    {
        UI.DropDown(id, () => SizeOptions.Where(CanResize).Select(size => new PopupMenuItem
        {
            Label = $"{size} × {size}",
            Handler = () => handler(size)
        }).ToArray(), $"{Size} × {Size}");
    }

    internal void DrawFilterDropDown(WidgetId id, Action<TextureFilter> handler)
    {
        UI.DropDown(id, () =>
        [
            new PopupMenuItem { Label = "Point", Handler = () => handler(TextureFilter.Point) },
            new PopupMenuItem { Label = "Linear", Handler = () => handler(TextureFilter.Linear) }
        ], Filter.ToString());
    }

    private void ResizeWithUndo(int size)
    {
        if (size == Size || !CanResize(size))
            return;
        Undo.Record(this);
        Resize(size);
    }

    private void SetFilterWithUndo(TextureFilter filter)
    {
        if (filter == Filter)
            return;
        Undo.Record(this);
        SetFilter(filter);
    }

    private void LoadPixelsFromImage()
    {
        if (!File.Exists(Path))
            return;

        try
        {
            using var image = Image.Load<Rgba32>(Path);
            var size = NormalizeSize(Math.Max(image.Width, image.Height));
            if (image.Width != image.Height)
                ReportWarning($"Palette textures must be square; the next save will produce a {size} × {size} PNG");
            if (image.Width > MaxSize || image.Height > MaxSize)
                ReportWarning($"Palette texture was clipped to the maximum supported size of {MaxSize} × {MaxSize}");

            ResetPixels(size, Color32.Transparent);
            var copyWidth = Math.Min((image.Width + CellPixelSize - 1) / CellPixelSize, GridSize);
            var copyHeight = Math.Min((image.Height + CellPixelSize - 1) / CellPixelSize, GridSize);
            for (var y = 0; y < copyHeight; y++)
            for (var x = 0; x < copyWidth; x++)
            {
                var color = image[x * CellPixelSize, y * CellPixelSize];
                _pixels[y * GridSize + x] = new Color32(color.R, color.G, color.B, color.A);
            }
        }
        catch (Exception ex)
        {
            ReportError($"Failed to load palette texture: {ex.Message}");
            ResetPixels(DefaultSize, Color32.Transparent);
        }
    }

    private void LoadLegacyMetadata(PropertySet meta)
    {
        // Version 1 stored one color per pixel. Preserve all populated grid
        // positions when moving to 8-pixel cells, enlarging only if necessary.
        var legacySize = Math.Clamp(meta.GetInt("palette_texture", "size", 16), 1, MaxSize / CellPixelSize);
        var legacyPixels = new Color32[legacySize * legacySize];
        var requiredGridSize = 1;

        for (var y = 0; y < legacySize; y++)
        {
            var row = meta.GetString("palette_texture", $"row_{y}", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var x = 0; x < Math.Min(row.Length, legacySize); x++)
            {
                if (!TryParseMetadataColor(row[x], out var color))
                    continue;
                legacyPixels[y * legacySize + x] = color;
                if (color != Color32.Transparent)
                    requiredGridSize = Math.Max(requiredGridSize, Math.Max(x, y) + 1);
            }
        }

        ResetPixels(NormalizeSize(Math.Max(legacySize, requiredGridSize * CellPixelSize)), Color32.Transparent);
        var copySize = Math.Min(legacySize, GridSize);
        for (var y = 0; y < copySize; y++)
            Array.Copy(legacyPixels, y * legacySize, _pixels, y * GridSize, copySize);
    }

    private void ParseMetadataRow(string value, int y)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var colors = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = Math.Min(colors.Length, GridSize);
        for (var x = 0; x < count; x++)
        {
            if (!TryParseMetadataColor(colors[x], out var color))
                continue;

            _pixels[y * GridSize + x] = color;
        }
    }

    private static bool TryParseMetadataColor(string value, out Color32 color)
    {
        color = Color32.Transparent;
        if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
            return false;

        color = new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        return true;
    }

    private static int NormalizeSize(int size)
    {
        size = Math.Clamp(size, CellPixelSize, MaxSize);
        return (size + CellPixelSize - 1) / CellPixelSize * CellPixelSize;
    }

    private void ResetPixels(int size, Color32 color)
    {
        Size = NormalizeSize(size);
        _pixels = new Color32[GridSize * GridSize];
        _gradientCells.Clear();
        Array.Fill(_pixels, color);
    }

    private Color32[] CreateTexturePixels()
    {
        var pixels = new Color32[Size * Size];
        for (var y = 0; y < GridSize; y++)
        for (var x = 0; x < GridSize; x++)
        {
            if (_gradientCells.TryGetValue(y * GridSize + x, out var gradient))
            {
                for (var cellY = 0; cellY < CellPixelSize; cellY++)
                for (var cellX = 0; cellX < CellPixelSize; cellX++)
                {
                    var pixelX = x * CellPixelSize + cellX;
                    var pixelY = y * CellPixelSize + cellY;
                    pixels[pixelY * Size + pixelX] = gradient.Sample(pixelX, pixelY);
                }
                continue;
            }

            var color = _pixels[y * GridSize + x];
            for (var cellY = 0; cellY < CellPixelSize; cellY++)
                Array.Fill(pixels, color, (y * CellPixelSize + cellY) * Size + x * CellPixelSize, CellPixelSize);
        }

        return pixels;
    }

    private void UpdateBounds()
    {
        Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
    }

    private void EnsurePreviewTexture()
    {
        if (_previewTexture != null && !_previewDirty)
            return;

        var pixels = CreateTexturePixels();
        var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
        if (_previewTexture == null)
        {
            _previewTexture = Texture.Create(
                Size,
                Size,
                bytes,
                TextureFormat.RGBA8,
                Filter,
                Name + "_palette_texture_preview");
            _previewDirty = false;
            return;
        }

        if (_previewDirty)
        {
            _previewTexture.Update(bytes);
            _previewDirty = false;
        }
    }

    private void InvalidatePreview(bool recreate = false)
    {
        if (recreate)
            DisposePreview();
        _previewDirty = true;
    }

    private void DisposePreview()
    {
        _previewTexture?.Dispose();
        _previewTexture = null;
    }
}
