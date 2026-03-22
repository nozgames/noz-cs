//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class SpriteDocument : Document, ISkeletonAttachment
{
    public override bool CanSave => IsMutable;

    public bool IsMutable { get; private set; } = true;
    public bool IsReference { get; private set; }
    public string? ImageFilePath { get; private set; }
    private Texture? _texture;
    private Vector2Int _textureSize;
    private Vector2Int _sourceImageSize;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"];

    public bool ShouldAtlas
    {
        get
        {
            var maxSize = EditorApplication.Config.AtlasMaxSpriteSize;
            for (ushort i = 0; i < (ushort)TotalTimeSlots; i++)
            {
                var size = GetFrameAtlasSize(i);
                if (size.X > maxSize || size.Y > maxSize)
                    return false;
            }
            return true;
        }
    }

    // Layer-based model
    public SpriteLayer RootLayer { get; } = new() { Name = "Root" };
    public List<SpriteAnimFrame> AnimFrames { get; } = new();
    public SpriteLayer? ActiveLayer { get; set; }

    private readonly List<Rect> _atlasUV = new();
    private Sprite? _sprite;
    public float Depth;
    public RectInt RasterBounds { get; private set; }
    public EdgeInsets Edges { get; set; } = EdgeInsets.Zero;

    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public SpritePathOperation CurrentOperation;

    public bool IsActiveLayerLocked => false;

    public int TotalTimeSlots
    {
        get
        {
            if (AnimFrames.Count == 0) return 1;
            var total = 0;
            foreach (var frame in AnimFrames)
                total += 1 + frame.Hold;
            return total;
        }
    }

    public int GetFrameAtTimeSlot(int timeSlot)
    {
        if (AnimFrames.Count == 0) return 0;
        var accumulated = 0;
        for (var i = 0; i < AnimFrames.Count; i++)
        {
            var slots = 1 + AnimFrames[i].Hold;
            if (timeSlot < accumulated + slots)
                return i;
            accumulated += slots;
        }
        return AnimFrames.Count - 1;
    }

    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    public ushort AtlasFrameCount => (ushort)TotalTimeSlots;
    internal AtlasDocument? Atlas { get; set; }

    public DocumentRef<SkeletonDocument> Skeleton;

    public bool ShouldShowInSkeleton(SkeletonDocument skeleton) => ShowInSkeleton;

    public int InsertFrame(int insertAt)
    {
        var index = Math.Clamp(insertAt, 0, AnimFrames.Count);
        var frame = new SpriteAnimFrame();
        // Copy visibility from adjacent frame if available
        if (AnimFrames.Count > 0)
        {
            var sourceIndex = Math.Min(index, AnimFrames.Count - 1);
            foreach (var layer in AnimFrames[sourceIndex].VisibleLayers)
                frame.VisibleLayers.Add(layer);
        }
        AnimFrames.Insert(index, frame);
        IncrementVersion();
        return index;
    }

    public int DeleteFrame(int frameIndex)
    {
        if (AnimFrames.Count <= 1 || frameIndex < 0 || frameIndex >= AnimFrames.Count)
            return Math.Max(0, frameIndex);
        AnimFrames.RemoveAt(frameIndex);
        IncrementVersion();
        return Math.Min(frameIndex, AnimFrames.Count - 1);
    }

    public void DrawSkinned(ReadOnlySpan<Matrix3x2> bindPose, ReadOnlySpan<Matrix3x2> animatedPose, in Matrix3x2 baseTransform)
    {
        DrawSprite(bindPose, animatedPose, baseTransform);
    }

    public Rect AtlasUV => GetAtlasUV(0);

    public Sprite? Sprite
    {
        get
        {
            if (_sprite == null) UpdateSprite();
            return _sprite;
        }
    }

    static SpriteDocument()
    {
    }

    public static void RegisterDef()
    {
        DocumentDef<SpriteDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "Sprite",
            Extensions = [".sprite", ".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"],
            Factory = () => new SpriteDocument(),
            EditorFactory = doc => new SpriteEditor((SpriteDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    private static void NewFile(StreamWriter writer)
    {
    }

    public override void Load()
    {
        DiscoverFiles();

        if (IsMutable)
        {
            var contents = File.ReadAllText(Path);
            var tk = new Tokenizer(contents);
            Load(ref tk);
        }
        else
        {
            LoadStaticImage();
        }

        UpdateBounds();
        Loaded = true;
    }

    private void DiscoverFiles()
    {
        var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
        var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(Path);

        if (ext == ".sprite")
        {
            // Created from .sprite — look for companion image
            foreach (var imgExt in ImageExtensions)
            {
                var imgPath = System.IO.Path.Combine(dir, stem + imgExt);
                if (File.Exists(imgPath))
                {
                    ImageFilePath = imgPath;
                    break;
                }
            }
        }
        else
        {
            // Created from an image file — check if .sprite exists
            var spritePath = System.IO.Path.Combine(dir, stem + ".sprite");
            if (File.Exists(spritePath))
            {
                // .sprite is the primary file, image is companion
                ImageFilePath = Path;
                Path = System.IO.Path.GetFullPath(spritePath).ToLowerInvariant();
            }
            else
            {
                // No .sprite — this is a static/immutable image
                IsMutable = false;
                ImageFilePath = Path;
            }
        }

        // Files in a "reference" directory are never exported
        if (Path.Contains("reference", StringComparison.OrdinalIgnoreCase))
        {
            IsReference = true;
            ShouldExport = false;
        }
    }

    private void LoadStaticImage()
    {
        if (ImageFilePath == null || !File.Exists(ImageFilePath))
            return;

        var info = Image.Identify(ImageFilePath);
        if (info == null)
            return;

        var w = info.Width;
        var h = info.Height;

        _sourceImageSize = new Vector2Int(w, h);
        RasterBounds = new RectInt(-w / 2, -h / 2, w, h);
    }

    public override void Reload()
    {
        if (!IsMutable)
        {
            LoadStaticImage();
            UpdateBounds();
            return;
        }

        Edges = EdgeInsets.Zero;
        Skeleton.Clear();
        ReloadGeneration();
        RootLayer.Children.Clear();
        AnimFrames.Clear();
        ActiveLayer = null;

        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        Skeleton.Resolve();
        ResolveGeneration();
        UpdateBounds();
    }

    private void Load(ref Tokenizer tk)
    {
        RootLayer.Children.Clear();
        AnimFrames.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
                ParseLayer(ref tk, RootLayer);
            else if (tk.ExpectIdentifier("path"))
                ParsePath(ref tk, RootLayer);
            else if (tk.ExpectIdentifier("frame"))
                ParseAnimFrame(ref tk);
            else if (tk.ExpectIdentifier("edges"))
            {
                if (tk.ExpectVec4(out var edgesVec))
                    Edges = new EdgeInsets(edgesVec.X, edgesVec.Y, edgesVec.Z, edgesVec.W);
            }
            else if (tk.ExpectIdentifier("skeleton"))
                Skeleton.Name = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("generate"))
                ParseGeneration(ref tk);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        ActiveLayer = RootLayer.Children.OfType<SpriteLayer>().FirstOrDefault() ?? RootLayer;
    }

    private void ParseLayer(ref Tokenizer tk, SpriteLayer parent)
    {
        var name = tk.ExpectQuotedString() ?? "";
        tk.ExpectDelimiter('{');

        var layer = new SpriteLayer { Name = name };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("layer"))
                ParseLayer(ref tk, layer);
            else if (tk.ExpectIdentifier("path"))
                ParsePath(ref tk, layer);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.ParseLayer: Unexpected token '{tk.GetString(badToken)}' in layer '{name}'");
                break;
            }
        }

        parent.Children.Add(layer);
    }

    private static void ParsePath(ref Tokenizer tk, SpriteLayer layer)
    {
        // Optional path name: path "name" { ... } or path { ... }
        string? pathName = null;
        if (!tk.ExpectDelimiter('{'))
        {
            pathName = tk.ExpectQuotedString();
            tk.ExpectDelimiter('{');
        }

        var path = new SpritePath { Name = pathName };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("fill"))
            {
                if (tk.ExpectColor(out var color))
                    path.FillColor = color.ToColor32();
                else
                {
                    path.FillColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(1.0f);
                    path.FillColor = path.FillColor.WithAlpha(legacyOpacity);
                }
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                if (tk.ExpectColor(out var color))
                    path.StrokeColor = color.ToColor32();
                else
                {
                    path.StrokeColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(0.0f);
                    path.StrokeColor = path.StrokeColor.WithAlpha(legacyOpacity);
                }
                path.StrokeWidth = (byte)tk.ExpectInt(path.StrokeWidth);
            }
            else if (tk.ExpectIdentifier("subtract"))
            {
                if (tk.ExpectBool())
                    path.Operation = SpritePathOperation.Subtract;
            }
            else if (tk.ExpectIdentifier("clip"))
            {
                if (tk.ExpectBool())
                    path.Operation = SpritePathOperation.Clip;
            }
            else if (tk.ExpectIdentifier("open"))
            {
                path.Open = tk.ExpectBool();
            }
            else if (tk.ExpectIdentifier("anchor"))
            {
                var x = tk.ExpectFloat();
                var y = tk.ExpectFloat();
                var curve = tk.ExpectFloat();
                path.Anchors.Add(new SpritePathAnchor
                {
                    Position = new Vector2(x, y),
                    Curve = curve,
                });
            }
            else
                break;
        }

        layer.Children.Add(path);
    }

    private void ParseAnimFrame(ref Tokenizer tk)
    {
        tk.ExpectDelimiter('{');
        var frame = new SpriteAnimFrame();

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("hold"))
                frame.Hold = tk.ExpectInt();
            else if (tk.ExpectIdentifier("visible"))
            {
                // Read layer names until we hit a non-string token
                while (!tk.IsEOF)
                {
                    var name = tk.ExpectQuotedString();
                    if (name == null)
                        break;
                    var layer = RootLayer.FindLayer(name);
                    if (layer != null)
                        frame.VisibleLayers.Add(layer);
                    else
                        Log.Warning($"SpriteDocument: Animation frame references unknown layer '{name}'");
                }
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.ParseAnimFrame: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        AnimFrames.Add(frame);
    }


    public void UpdateBounds()
    {
        if (!IsMutable)
        {
            // Immutable sprites: use source image size or constrained size
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
            MarkSpriteDirty();
            return;
        }

        // Compute bounds from layer paths
        var allPaths = new List<SpritePath>();
        RootLayer.CollectVisiblePaths(allPaths);

        if (allPaths.Count == 0)
        {
            SetDefaultBounds();
            return;
        }

        var first = true;
        var bounds = Rect.Zero;

        foreach (var path in allPaths)
        {
            path.UpdateSamples();
            path.UpdateBounds();

            if (path.Anchors.Count == 0)
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

        // Convert world-space bounds to raster bounds
        var dpi = EditorApplication.Config.PixelsPerUnit;
        RasterBounds = new RectInt(
            (int)MathF.Floor(bounds.X * dpi),
            (int)MathF.Floor(bounds.Y * dpi),
            (int)MathF.Ceiling(bounds.Width * dpi),
            (int)MathF.Ceiling(bounds.Height * dpi));

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

        ClampToMaxSpriteSize();
        Bounds = RasterBounds.ToRect().Scale(1.0f / EditorApplication.Config.PixelsPerUnit);
        MarkSpriteDirty();
    }

    private void SetDefaultBounds()
    {
        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            RasterBounds = new RectInt(-cs.X / 2, -cs.Y / 2, cs.X, cs.Y);
            var ppu = EditorApplication.Config.PixelsPerUnitInv;
            Bounds = new Rect(
                RasterBounds.X * ppu,
                RasterBounds.Y * ppu,
                RasterBounds.Width * ppu,
                RasterBounds.Height * ppu);
        }
        else
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
        }
    }

    private void ClampToMaxSpriteSize()
    {
        var maxSize = EditorApplication.Config.AtlasMaxSpriteSize;
        var width = RasterBounds.Width;
        var height = RasterBounds.Height;

        if (width <= maxSize && height <= maxSize)
            return;

        var centerX = RasterBounds.X + width / 2;
        var centerY = RasterBounds.Y + height / 2;
        var clampedWidth = Math.Min(width, maxSize);
        var clampedHeight = Math.Min(height, maxSize);

        RasterBounds = new RectInt(
            centerX - clampedWidth / 2,
            centerY - clampedHeight / 2,
            clampedWidth,
            clampedHeight);
    }

    public override void Save(StreamWriter writer)
    {
        if (!Edges.IsZero)
            writer.WriteLine($"edges ({Edges.T},{Edges.L},{Edges.B},{Edges.R})");

        if (Skeleton.HasValue)
            writer.WriteLine($"skeleton \"{Skeleton.Name}\"");

        SaveLayers(writer);
        SaveGeneration(writer);
    }

    private void SaveLayers(StreamWriter writer)
    {
        writer.WriteLine();

        // Paths directly on root layer are written at top level (no wrapping layer)
        foreach (var child in RootLayer.Children)
        {
            if (child is SpritePath path)
                SavePathV2(writer, path, 0);
        }

        foreach (var child in RootLayer.Children)
        {
            if (child is SpriteLayer layer)
                SaveLayer(writer, layer, 0);
        }

        if (AnimFrames.Count > 0)
        {
            foreach (var frame in AnimFrames)
            {
                writer.Write("frame {");
                writer.WriteLine();

                if (frame.Hold > 0)
                    writer.WriteLine($"  hold {frame.Hold}");

                if (frame.VisibleLayers.Count > 0)
                {
                    writer.Write("  visible");
                    foreach (var layer in frame.VisibleLayers)
                        writer.Write($" \"{layer.Name}\"");
                    writer.WriteLine();
                }

                writer.WriteLine("}");
                writer.WriteLine();
            }
        }
    }

    private void SavePathV2(StreamWriter writer, SpritePath path, int depth)
    {
        var indent = new string(' ', depth * 2);
        var propIndent = new string(' ', (depth + 1) * 2);

        if (path.Name != null)
            writer.WriteLine($"{indent}path \"{path.Name}\" {{");
        else
            writer.WriteLine($"{indent}path {{");

        if (path.IsSubtract)
            writer.WriteLine($"{propIndent}subtract true");
        if (path.IsClip)
            writer.WriteLine($"{propIndent}clip true");
        if (path.Open)
            writer.WriteLine($"{propIndent}open true");
        writer.WriteLine($"{propIndent}fill {FormatColor(path.FillColor)}");

        if (path.StrokeColor.A > 0)
            writer.WriteLine($"{propIndent}stroke {FormatColor(path.StrokeColor)} {path.StrokeWidth}");

        foreach (var anchor in path.Anchors)
        {
            writer.Write(string.Format(CultureInfo.InvariantCulture, "{0}anchor {1} {2}", propIndent, anchor.Position.X, anchor.Position.Y));
            if (MathF.Abs(anchor.Curve) > float.Epsilon)
                writer.Write(string.Format(CultureInfo.InvariantCulture, " {0}", anchor.Curve));
            writer.WriteLine();
        }

        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }

    private void SaveLayer(StreamWriter writer, SpriteLayer layer, int depth)
    {
        var indent = new string(' ', depth * 2);
        writer.WriteLine($"{indent}layer \"{layer.Name}\" {{");

        foreach (var child in layer.Children)
        {
            if (child is SpritePath path)
                SavePathV2(writer, path, depth + 1);
            else if (child is SpriteLayer childLayer)
                SaveLayer(writer, childLayer, depth + 1);
        }

        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }


    private static string FormatColor(Color32 c)
    {
        if (c.A < 255)
            return $"rgba({c.R},{c.G},{c.B},{c.A / 255f:G})";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public override void Draw()
    {
        DrawOrigin();

        if (!IsMutable)
            DrawStaticImage();
        else if (Generation != null)
            DrawGeneration();
        else
            DrawVector();
    }

    public override bool DrawThumbnail()
    {
        if (Sprite != null)
        {
            UI.Image(Sprite, ImageStyle.Center);
            return true;
        }

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
        if (_texture != null || ImageFilePath == null || !File.Exists(ImageFilePath))
            return;

        try
        {
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageFilePath);
            _textureSize = new Vector2Int(srcImage.Width, srcImage.Height);
            var data = new byte[srcImage.Width * srcImage.Height * 4];
            srcImage.CopyPixelDataTo(data);
            _texture = Texture.Create(srcImage.Width, srcImage.Height, data, TextureFormat.RGBA8, TextureFilter.Linear, Name + "_preview");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load preview texture '{ImageFilePath}': {ex.Message}");
        }
    }

    private bool DrawFromAtlas(float alpha = -1f)
    {
        if (Atlas?.Texture == null) return false;

        var uv = GetAtlasUV(0);
        if (uv == Rect.Zero) return false;

        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(alpha < 0 ? Workspace.XrayAlpha : alpha));
            Graphics.Draw(Bounds, uv);
        }
        return true;
    }

    private bool DrawFromPreviewTexture()
    {
        EnsurePreviewTexture();

        if (_texture == null) return false;

        var uv = new Rect(0, 0, 1, 1);
        var drawBounds = Bounds;

        if (ConstrainedSize.HasValue && _textureSize.X > 0 && _textureSize.Y > 0)
        {
            var cs = ConstrainedSize.Value;
            if (cs.X < _textureSize.X || cs.Y < _textureSize.Y)
            {
                // Constraint is smaller — crop UVs to center of source image
                var uW = Math.Min(1f, (float)cs.X / _textureSize.X);
                var uH = Math.Min(1f, (float)cs.Y / _textureSize.Y);
                uv = new Rect((1f - uW) * 0.5f, (1f - uH) * 0.5f, uW, uH);
            }
            else if (cs.X > _textureSize.X || cs.Y > _textureSize.Y)
            {
                // Constraint is larger — draw image at original size centered in bounds
                var ppu = EditorApplication.Config.PixelsPerUnitInv;
                var imgW = _textureSize.X * ppu;
                var imgH = _textureSize.Y * ppu;
                drawBounds = new Rect(-imgW * 0.5f, -imgH * 0.5f, imgW, imgH);
            }
        }

        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetTexture(_texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(Workspace.XrayAlpha));
            Graphics.Draw(drawBounds, uv);
        }
        return true;
    }

    private void DrawStaticImage()
    {
        if (!DrawFromAtlas() && !DrawFromPreviewTexture())
            DrawBounds();
    }

    private void DrawVector()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (!DrawFromAtlas())
            DrawBounds();
    }

    public void DrawSprite(in Vector2 offset = default, float alpha = 1.0f, int frame = 0)
    {
        if (Atlas?.Texture == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(alpha * Workspace.XrayAlpha));
            Graphics.SetTextureFilter(sprite.TextureFilter);

            if (frame < sprite.Frames.Length)
            {
                ref readonly var sf = ref sprite.Frames[frame];

                Rect bounds;
                if (sf.Size.X > 0 && sf.Size.Y > 0)
                    bounds = new Rect(sf.Offset.X * Graphics.PixelsPerUnitInv, sf.Offset.Y * Graphics.PixelsPerUnitInv, sf.Size.X * Graphics.PixelsPerUnitInv, sf.Size.Y * Graphics.PixelsPerUnitInv).Translate(offset);
                else
                    bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv).Translate(offset);

                Graphics.Draw(bounds, sf.UV);
            }
        }
    }

    public void DrawSprite(ReadOnlySpan<Matrix3x2> bindPose, ReadOnlySpan<Matrix3x2> animatedPose, in Matrix3x2 baseTransform, int frame = 0, Color? tint = null)
    {
        if (Atlas?.Texture == null) return;

        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(tint ?? Color.White);
            Graphics.SetTextureFilter(sprite.TextureFilter);

            if (frame < sprite.Frames.Length)
            {
                ref readonly var sf = ref sprite.Frames[frame];

                Rect bounds;
                if (sf.Size.X > 0 && sf.Size.Y > 0)
                    bounds = new Rect(sf.Offset.X * Graphics.PixelsPerUnitInv, sf.Offset.Y * Graphics.PixelsPerUnitInv, sf.Size.X * Graphics.PixelsPerUnitInv, sf.Size.Y * Graphics.PixelsPerUnitInv);
                else
                    bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv);

                Graphics.SetColor(Color.White);
                Graphics.SetTransform(baseTransform);
                Graphics.Draw(bounds, sf.UV);
            }
        }
    }

    public override void Clone(Document source)
    {
        var src = (SpriteDocument)source;
        Depth = src.Depth;
        Bounds = src.Bounds;
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;
        CurrentStrokeWidth = src.CurrentStrokeWidth;

        Edges = src.Edges;
        Skeleton = src.Skeleton;

        // Clone layer model
        RootLayer.Children.Clear();
        foreach (var child in src.RootLayer.Children)
            RootLayer.Children.Add(child.Clone());

        AnimFrames.Clear();
        foreach (var frame in src.AnimFrames)
            AnimFrames.Add(frame.Clone(src.RootLayer, RootLayer));

        // Restore active layer by matching position in tree
        if (src.ActiveLayer != null)
        {
            var name = src.ActiveLayer.Name;
            ActiveLayer = RootLayer.FindLayer(name);
        }

        CloneGeneration(src);
    }

    public override void LoadMetadata(PropertySet meta)
    {
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
        ConstrainedSize = ParseConstrainedSize(meta.GetString("sprite", "constrained_size", ""));

        // Backward compat: migrate style from metadata to file content
        if (Generation != null && string.IsNullOrEmpty(Generation.Config.Name))
        {
            var style = meta.GetString("sprite", "style", "");
            if (!string.IsNullOrEmpty(style))
                Generation.Config.Name = style;
        }

        // Recompute bounds now that ConstrainedSize is available
        // (Load() calls UpdateBounds() before metadata is loaded)
        if (Loaded && ConstrainedSize.HasValue)
            UpdateBounds();
    }

    private static Vector2Int? ParseConstrainedSize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        var parts = value.Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h))
        {
            return new Vector2Int(w, h);
        }
        return null;
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetBool("sprite", "show_in_skeleton", ShowInSkeleton);
        meta.SetBool("sprite", "show_tiling", ShowTiling);
        meta.SetBool("sprite", "show_skeleton_overlay", ShowSkeletonOverlay);

        if (ConstrainedSize.HasValue)
            meta.SetString("sprite", "constrained_size", $"{ConstrainedSize.Value.X}x{ConstrainedSize.Value.Y}");
        else
            meta.RemoveKey("sprite", "constrained_size");

        meta.RemoveKey("sprite", "prompt_hash");

        // Clean up legacy style from metadata (now stored in file)
        meta.RemoveKey("sprite", "style");
    }

    public override void PostLoad()
    {
        Skeleton.Resolve();
        PostLoadGeneration();
    }

    public override void GetReferences(List<Document> references)
    {
        if (Generation != null)
            foreach (var r in Generation.References)
                if (r.Value != null)
                    references.Add(r.Value);
    }

    internal void ClearAtlasUVs()
    {
        _atlasUV.Clear();
        MarkSpriteDirty();
    }

    internal void Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        // Static image or companion image: blit from file
        if (ImageFilePath != null && File.Exists(ImageFilePath) && (!IsMutable || Generation is { HasImageData: true }))
        {
            RasterizeImageFile(image, rect, padding);
            return;
        }

        // Generated sprites: blit texture pixels instead of rasterizing paths
        if (Generation is { HasImageData: true })
        {
            var w = RasterBounds.Size.X;
            var h = RasterBounds.Size.Y;

            using var ms = new MemoryStream(Generation.Job.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);
            if (srcImage.Width != w || srcImage.Height != h)
                srcImage.Mutate(x => x.Resize(w, h));

            var rasterRect = new RectInt(
                rect.Rect.Position + new Vector2Int(padding, padding),
                new Vector2Int(w, h));

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var pixel = srcImage[x, y];
                    image[rasterRect.X + x, rasterRect.Y + y] = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
                }

            image.BleedColors(rasterRect);
            return;
        }

        var frameIndex = rect.FrameIndex;
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var padding2 = padding * 2;

        var fi = GetFrameAtTimeSlot(frameIndex);

        // Apply frame visibility if animated
        if (fi < AnimFrames.Count)
            AnimFrames[fi].ApplyVisibility(RootLayer);

        var targetRect = new RectInt(
            rect.Rect.Position,
            new Vector2Int(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2));
        var sourceOffset = -RasterBounds.Position + new Vector2Int(padding, padding);

        RasterizeLayer(RootLayer, image, targetRect, sourceOffset, dpi);

        image.BleedColors(targetRect);
    }

    private void RasterizeImageFile(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        if (ImageFilePath == null) return;

        using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageFilePath);
        var srcW = srcImage.Width;
        var srcH = srcImage.Height;
        var dstW = RasterBounds.Width;
        var dstH = RasterBounds.Height;
        var padding2 = padding * 2;

        // Compute source region (center crop if constraint is smaller)
        var srcX = Math.Max(0, (srcW - dstW) / 2);
        var srcY = Math.Max(0, (srcH - dstH) / 2);
        var copyW = Math.Min(srcW, dstW);
        var copyH = Math.Min(srcH, dstH);

        // Compute destination offset (center pad if constraint is larger)
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

    // Layer-scoped rasterization: booleans only affect paths within the same layer
    private static void RasterizeLayer(
        SpriteLayer layer,
        PixelData<Color32> image,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi)
    {
        if (!layer.Visible) return;

        // Collect subtract paths within this layer
        List<Clipper2Lib.PathsD>? subtractContours = null;
        foreach (var child in layer.Children)
        {
            if (child is not SpritePath path) continue;
            if (!path.IsSubtract || path.Anchors.Count < 3) continue;
            var contours = SpritePathClipper.SpritePathToPaths(path);
            if (contours.Count > 0)
            {
                subtractContours ??= new();
                subtractContours.Add(contours);
            }
        }

        Clipper2Lib.PathsD? accumulatedPaths = null;

        foreach (var child in layer.Children)
        {
            if (child is not SpritePath path) continue;
            if (path.IsSubtract || path.Anchors.Count < 3) continue;

            var contours = SpritePathClipper.SpritePathToPaths(path);
            if (contours.Count == 0) continue;

            if (path.IsClip)
            {
                if (accumulatedPaths is not { Count: > 0 }) continue;
                contours = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Intersection,
                    contours, accumulatedPaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }
            else
            {
                var accContours = contours;
                if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
                {
                    var halfStroke = path.StrokeWidth * SpritePath.StrokeScale;
                    var contracted = Clipper2Lib.Clipper.InflatePaths(contours, -halfStroke,
                        Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, precision: 6);
                    if (contracted.Count > 0)
                        accContours = contracted;
                }

                if (accumulatedPaths == null)
                    accumulatedPaths = new Clipper2Lib.PathsD(accContours);
                else
                    accumulatedPaths = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Union,
                        accumulatedPaths, accContours, Clipper2Lib.FillRule.NonZero, precision: 6);
            }

            // Apply subtract paths within THIS layer only
            if (subtractContours != null)
            {
                Clipper2Lib.PathsD? negativePaths = null;
                foreach (var subContours in subtractContours)
                {
                    negativePaths ??= new Clipper2Lib.PathsD();
                    negativePaths.AddRange(subContours);
                }

                if (negativePaths is { Count: > 0 })
                {
                    contours = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                        contours, negativePaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                    if (contours.Count == 0) continue;
                }
            }

            var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
            var hasFill = path.FillColor.A > 0;

            if (hasStroke)
            {
                var halfStroke = path.StrokeWidth * SpritePath.StrokeScale;
                var contracted = Clipper2Lib.Clipper.InflatePaths(contours, -halfStroke,
                    Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, precision: 6);

                if (hasFill)
                {
                    Rasterizer.Fill(contours, image, targetRect, sourceOffset, dpi, path.StrokeColor);
                    if (contracted.Count > 0)
                        Rasterizer.Fill(contracted, image, targetRect, sourceOffset, dpi, path.FillColor);
                }
                else
                {
                    var ring = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                        contours, contracted, Clipper2Lib.FillRule.NonZero, precision: 6);
                    if (ring.Count > 0)
                        Rasterizer.Fill(ring, image, targetRect, sourceOffset, dpi, path.StrokeColor);
                }
            }
            else if (hasFill)
            {
                Rasterizer.Fill(contours, image, targetRect, sourceOffset, dpi, path.FillColor);
            }
        }

        // Recurse into child layers
        foreach (var child in layer.Children)
        {
            if (child is SpriteLayer childLayer)
                RasterizeLayer(childLayer, image, targetRect, sourceOffset, dpi);
        }
    }

    internal void UpdateAtlasUVs(AtlasDocument atlas, ReadOnlySpan<AtlasSpriteRect> allRects, int padding)
    {
        ClearAtlasUVs();
        var padding2 = padding * 2;
        int uvIndex = 0;
        var ts = (float)EditorApplication.Config.AtlasSize;

        var totalSlots = (ushort)TotalTimeSlots;
        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            int rectIndex = -1;
            for (int i = 0; i < allRects.Length; i++)
            {
                if (allRects[i].Source == this && allRects[i].FrameIndex == frameIndex)
                {
                    rectIndex = i;
                    break;
                }
            }
            if (rectIndex == -1) return;

            ref readonly var rect = ref allRects[rectIndex];

            var u = (rect.Rect.Left + padding) / ts;
            var v = (rect.Rect.Top + padding) / ts;
            var s = u + RasterBounds.Size.X / ts;
            var t = v + RasterBounds.Size.Y / ts;
            SetAtlasUV(uvIndex++, Rect.FromMinMax(u, v, s, t));
        }
    }

    internal void SetAtlasUV(int slotIndex, Rect uv)
    {
        while (_atlasUV.Count <= slotIndex)
            _atlasUV.Add(Rect.Zero);
        _atlasUV[slotIndex] = uv;
        MarkSpriteDirty();
    }

    internal Rect GetAtlasUV(int slotIndex) =>
        slotIndex < _atlasUV.Count ? _atlasUV[slotIndex] : Rect.Zero;

    private void UpdateSprite()
    {
        if (Atlas == null)
        {
            _sprite = null;
            return;
        }

        var totalSlots = TotalTimeSlots;
        var frames = new NoZ.SpriteFrame[totalSlots];

        for (int frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var uv = GetAtlasUV(frameIndex);
            if (uv == Rect.Zero)
            {
                _sprite = null;
                return;
            }

            frames[frameIndex] = new NoZ.SpriteFrame(uv, RasterBounds.Position, RasterBounds.Size);
        }

        _sprite = Sprite.Create(
            name: Name,
            bounds: RasterBounds,
            pixelsPerUnit: EditorApplication.Config.PixelsPerUnit,
            filter: TextureFilter.Linear,
            boneIndex: -1,
            frames: frames,
            frameRate: 12.0f,
            edges: ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero,
            sliceMask: Sprite.CalculateSliceMask(RasterBounds, ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero),
            texture: Atlas?.Texture);
    }

    internal void MarkSpriteDirty()
    {
        _sprite?.Dispose();
        _sprite = null;
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        Skeleton.Resolve();
        UpdateBounds();

        var totalSlots = (ushort)TotalTimeSlots;
        var isStandalone = !ShouldAtlas || Atlas == null;

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(totalSlots);
        writer.Write(isStandalone ? (ushort)0xFFFF : (ushort)(Atlas?.Index ?? 0));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write((float)EditorApplication.Config.PixelsPerUnit);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((short)-1);  // Legacy bone index field
        writer.Write(totalSlots); // totalMeshes = 1 per frame = totalSlots
        writer.Write(12.0f);  // Frame rate

        // 9-slice edges
        var activeEdges = ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero;
        writer.Write((short)activeEdges.T);
        writer.Write((short)activeEdges.L);
        writer.Write((short)activeEdges.B);
        writer.Write((short)activeEdges.R);
        writer.Write(Sprite.CalculateSliceMask(RasterBounds, activeEdges));

        // Write one mesh per frame
        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var uv = isStandalone
                ? new Rect(0, 0, 1, 1)
                : GetAtlasUV(frameIndex);
            WriteMesh(writer, uv, sortOrder: 0, boneIndex: -1, RasterBounds);
        }

        // Frame table
        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            writer.Write(frameIndex);   // meshStart
            writer.Write((ushort)1);    // meshCount
        }

        // Embedded texture for standalone sprites
        if (isStandalone)
            ExportEmbeddedTexture(writer);
    }

    private void ExportEmbeddedTexture(BinaryWriter writer)
    {
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        if (w <= 0 || h <= 0) return;

        using var image = new PixelData<Color32>(w, h);

        // Rasterize into the image
        var rect = new AtlasSpriteRect
        {
            Name = Name,
            Source = this,
            Rect = new RectInt(0, 0, w, h),
            FrameIndex = 0
        };
        Rasterize(image, rect, padding: 0);

        // Write texture header (same format as Atlas/Texture binary)
        writer.Write((byte)TextureFormat.RGBA8);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((byte)TextureClamp.Clamp);
        writer.Write((uint)w);
        writer.Write((uint)h);
        writer.Write(image.AsByteSpan());
    }

    private static void WriteMesh(BinaryWriter writer, Rect uv, short sortOrder, short boneIndex, RectInt bounds)
    {
        writer.Write(uv.Left);
        writer.Write(uv.Top);
        writer.Write(uv.Right);
        writer.Write(uv.Bottom);
        writer.Write(sortOrder);
        writer.Write(boneIndex);
        writer.Write((short)bounds.X);
        writer.Write((short)bounds.Y);
        writer.Write((short)bounds.Width);
        writer.Write((short)bounds.Height);
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();

        if (!IsEditing && Atlas != null)
            AtlasManager.UpdateSource(this);

        base.OnUndoRedo();
    }

    public Vector2Int GetFrameAtlasSize(ushort timeSlot)
    {
        var padding2 = EditorApplication.Config.AtlasPadding * 2;
        return new(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2);
    }

    public override void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        DisposeGeneration();
        base.Dispose();
    }
}
