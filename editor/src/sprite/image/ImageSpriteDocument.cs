//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public class ImageSpriteDocument : SpriteDocument
{
    protected override int PixelsPerUnit => EditorApplication.Config.PixelsPerUnit;
    protected override TextureFilter TextureFilter => TextureFilter.Linear;

    private Vector2Int _sourceImageSize;
    private Texture? _texture;

    public override bool CanSave => false;
    
    public override DocumentEditor CreateEditor() => new ImageEditor(this);

    public override Color32 GetPixelAt(System.Numerics.Vector2 worldPos)
    {
        EnsurePreviewTexture();
        EnsurePreviewTexture();
        if (_texture == null)
            return default;

        System.Numerics.Matrix3x2.Invert(Transform, out var invTransform);
        var local = System.Numerics.Vector2.Transform(worldPos, invTransform);

        var nx = (local.X - Bounds.X) / Bounds.Width;
        var ny = (local.Y - Bounds.Y) / Bounds.Height;
        return _texture.GetPixel((int)(nx * _texture.Width), (int)(ny * _texture.Height));
    }

    public override void Load()
    {
        LoadImageSize();

        // Files in a "reference" directory are never exported
        if (Path.Contains("reference", StringComparison.OrdinalIgnoreCase))
            ShouldExport = false;

        UpdateBounds();
        Loaded = true;
    }

    public override void Reload()
    {
        LoadImageSize();
        UpdateBounds();
    }

    private void LoadImageSize()
    {
        if (!EditorApplication.Store.FileExists(Path))
            return;

        var info = Image.Identify(EditorApplication.Store.OpenRead(Path));
        if (info == null)
            return;

        _sourceImageSize = new Vector2Int(info.Width, info.Height);
        RasterBounds = new RectInt(-info.Width / 2, -info.Height / 2, info.Width, info.Height);
    }

    protected override void UpdateContentBounds()
    {
        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            RasterBounds = new RectInt(-cs.X / 2, -cs.Y / 2, cs.X, cs.Y);
        }
        else
        {
            var w = _sourceImageSize.X;
            var h = _sourceImageSize.Y;
            RasterBounds = new RectInt(-w / 2, -h / 2, w, h);
        }

        var ppu = EditorApplication.Config.PixelsPerUnitInv;
        Bounds = new Rect(
            RasterBounds.X * ppu,
            RasterBounds.Y * ppu,
            RasterBounds.Width * ppu,
            RasterBounds.Height * ppu);
    }

    public override void LoadMetadata(PropertySet meta)
    {
        base.LoadMetadata(meta);

        // Image documents store bone/skeleton/sort/edges in metadata
        Skeleton.Name = meta.GetString("sprite", "skeleton", "");
        BoneName = meta.GetString("sprite", "bone", "");
        SortOrderId = meta.GetString("sprite", "sort", "");
        if (string.IsNullOrEmpty(Skeleton.Name)) Skeleton.Name = null;
        if (string.IsNullOrEmpty(BoneName)) BoneName = null;
        if (string.IsNullOrEmpty(SortOrderId)) SortOrderId = null;

        var edgesStr = meta.GetString("sprite", "edges", "");
        if (!string.IsNullOrEmpty(edgesStr))
        {
            // Parse "(T,L,B,R)" format
            var trimmed = edgesStr.Trim('(', ')');
            var parts = trimmed.Split(',');
            if (parts.Length == 4 &&
                float.TryParse(parts[0], out var t) &&
                float.TryParse(parts[1], out var l) &&
                float.TryParse(parts[2], out var b) &&
                float.TryParse(parts[3], out var r))
            {
                Edges = new EdgeInsets(t, l, b, r);
            }
        }
    }

    public override void SaveMetadata(PropertySet meta)
    {
        base.SaveMetadata(meta);

        if (Skeleton.HasValue)
            meta.SetString("sprite", "skeleton", Skeleton.Name!);
        else
            meta.RemoveKey("sprite", "skeleton");

        if (BoneName != null)
            meta.SetString("sprite", "bone", BoneName);
        else
            meta.RemoveKey("sprite", "bone");

        if (SortOrderId != null)
            meta.SetString("sprite", "sort", SortOrderId);
        else
            meta.RemoveKey("sprite", "sort");

        if (!Edges.IsZero)
            meta.SetString("sprite", "edges", $"({Edges.T},{Edges.L},{Edges.B},{Edges.R})");
        else
            meta.RemoveKey("sprite", "edges");
    }

    public override void Draw()
    {
        DrawOrigin();

        if (Sprite != null)
            DrawSprite();
        else
        {
            EnsurePreviewTexture();
            if (_texture != null)
                DrawTexturedRect(_texture, Bounds, Color.White);
            else
                DrawBounds();
        }
    }

    public override bool DrawThumbnail()
    {
        if (base.DrawThumbnail())
            return true;

        EnsurePreviewTexture();
        if (_texture != null)
        {
            UI.Image(_texture, ImageStyle.Center);
            return true;
        }

        return false;
    }

    private void EnsurePreviewTexture()
    {
        if (_texture != null || !EditorApplication.Store.FileExists(Path))
            return;

        try
        {
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(EditorApplication.Store.OpenRead(Path));
            _texture = CreateTextureFromImage(srcImage, Name + "_preview");
        }
        catch (Exception ex)
        {
            ReportError($"Failed to load preview texture '{Path}': {ex.Message}");
        }
    }

    internal override void RasterizeCore(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        if (!EditorApplication.Store.FileExists(Path)) return;

        using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(EditorApplication.Store.OpenRead(Path));
        var srcW = srcImage.Width;
        var srcH = srcImage.Height;
        var dstW = RasterBounds.Width;
        var dstH = RasterBounds.Height;
        var padding2 = padding * 2;

        var srcX = Math.Max(0, (srcW - dstW) / 2);
        var srcY = Math.Max(0, (srcH - dstH) / 2);
        var copyW = Math.Min(srcW, dstW);
        var copyH = Math.Min(srcH, dstH);

        var dstOffX = Math.Max(0, (dstW - srcW) / 2);
        var dstOffY = Math.Max(0, (dstH - srcH) / 2);

        var rasterRect = new RectInt(
            rect.Rect.Position + new Vector2Int(padding, padding),
            new Vector2Int(dstW, dstH));

        for (int y = 0; y < copyH; y++)
            for (int x = 0; x < copyW; x++)
            {
                var pixel = srcImage[srcX + x, srcY + y];
                image[rasterRect.X + dstOffX + x, rasterRect.Y + dstOffY + y] = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
            }

        var outerRect = new RectInt(rect.Rect.Position, new Vector2Int(dstW + padding2, dstH + padding2));
        image.BleedColors(rasterRect);
        for (int p = padding - 1; p >= 0; p--)
        {
            var padRect = new RectInt(
                outerRect.Position + new Vector2Int(p, p),
                outerRect.Size - new Vector2Int(p * 2, p * 2));
            image.ExtrudeEdges(padRect);
        }
    }

    protected override void SaveContent(StreamWriter writer) { }
    protected override void CloneContent(SpriteDocument source) { }

    public override void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        base.Dispose();
    }
}
