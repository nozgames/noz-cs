//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using NoZ;
using NoZ.Editor;
using Color = NoZ.Color;

namespace NoZ.Editor.Graphics3D;

/// <summary>
/// Imports a standalone GPU texture from an image source. Image files default
/// to ImageSpriteDocument; the shared document-type selector opts into this
/// importer by writing editor.document_type to the image metadata.
/// </summary>
public partial class TextureDocument : Document
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"];

    private static partial class WidgetIds
    {
        public static partial WidgetId Filter { get; }
    }

    private Vector2Int _imageSize;
    private Texture? _previewTexture;

    public TextureFilter Filter { get; private set; } = TextureFilter.Linear;

    public static void RegisterDef()
    {
        DocumentDef<TextureDocument>.Register(new DocumentDef
        {
            Type = AssetType.Texture,
            Name = "Texture",
            Extensions = ImageExtensions,
            Factory = _ => new TextureDocument(),
            // Reuse the image icon until the editor has a dedicated texture icon.
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    public override void Load()
    {
        LoadImageSize();
        UpdateBounds();
        Loaded = true;
    }

    public override void Reload()
    {
        DisposePreview();
        LoadImageSize();
        UpdateBounds();
    }

    public override void LoadMetadata(PropertySet meta)
    {
        var filter = meta.GetString("texture", "filter", "linear");
        Filter = filter.Equals("point", StringComparison.OrdinalIgnoreCase) ||
                 filter.Equals("nearest", StringComparison.OrdinalIgnoreCase)
            ? TextureFilter.Point
            : TextureFilter.Linear;
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetString("texture", "filter", Filter == TextureFilter.Point ? "point" : "linear");
    }

    public override void Clone(Document source)
    {
        Filter = ((TextureDocument)source).Filter;
    }

    public override void OnUndoRedo()
    {
        DisposePreview();
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
            UI.Text($"{_imageSize.X} × {_imageSize.Y}");

        using (EditorInspector.BeginProperty("Filter"))
        {
            UI.DropDown(WidgetIds.Filter, () =>
            [
                new PopupMenuItem
                {
                    Label = "Point",
                    Handler = () => SetFilter(TextureFilter.Point)
                },
                new PopupMenuItem
                {
                    Label = "Linear",
                    Handler = () => SetFilter(TextureFilter.Linear)
                }
            ], Filter.ToString());
        }
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        LoadMetadata(meta);
        using var stream = File.OpenRead(Path);
        using var image = Image.Load<Rgba32>(stream);
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        using var writer = new BinaryWriter(File.Create(outputPath));
        TextureAssetWriter.WriteRgba8(writer, image.Width, image.Height, Filter, pixels);
    }

    public override void Dispose()
    {
        DisposePreview();
        base.Dispose();
    }

    private void SetFilter(TextureFilter filter)
    {
        if (Filter == filter)
            return;

        Undo.Record(this);
        Filter = filter;
        DisposePreview();
        AssetManifest.IsModified = true;
    }

    private void LoadImageSize()
    {
        _imageSize = Vector2Int.Zero;
        if (!File.Exists(Path))
            return;

        using var stream = File.OpenRead(Path);
        var info = Image.Identify(stream);
        if (info != null)
            _imageSize = new Vector2Int(info.Width, info.Height);
    }

    private void UpdateBounds()
    {
        if (_imageSize == Vector2Int.Zero)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        var ppu = Math.Max(EditorApplication.Config.PixelsPerUnit, 1);
        var width = (float)_imageSize.X / ppu;
        var height = (float)_imageSize.Y / ppu;
        Bounds = new Rect(-width * 0.5f, -height * 0.5f, width, height);
    }

    private void EnsurePreviewTexture()
    {
        if (_previewTexture != null || !File.Exists(Path))
            return;

        try
        {
            using var stream = File.OpenRead(Path);
            using var image = Image.Load<Rgba32>(stream);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            _previewTexture = Texture.Create(
                image.Width,
                image.Height,
                pixels,
                TextureFormat.RGBA8,
                Filter,
                Name + "_texture_preview");
        }
        catch (Exception ex)
        {
            ReportError($"Failed to load texture preview: {ex.Message}");
        }
    }

    private void DisposePreview()
    {
        _previewTexture?.Dispose();
        _previewTexture = null;
    }
}
