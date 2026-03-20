//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class SpriteDocument : Document, IShapeDocument, ISkeletonBound
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

    public class SkeletonBinding
    {
        public StringId SkeletonName;
        public SkeletonDocument? Skeleton;

        public bool IsBound => Skeleton != null;
        public bool IsBoundTo(SkeletonDocument skeleton) => Skeleton == skeleton;

        public void Set(SkeletonDocument? skeleton)
        {
            if (skeleton == null)
            {
                Clear();
                return;
            }

            Skeleton = skeleton;
            SkeletonName = StringId.Get(skeleton.Name);
        }

        public void Clear()
        {
            Skeleton = null;
            SkeletonName = StringId.None;
        }

        public void CopyFrom(SkeletonBinding src)
        {
            SkeletonName = src.SkeletonName;
            Skeleton = src.Skeleton;
        }

        public void Resolve()
        {
            Skeleton = DocumentManager.Find(AssetType.Skeleton, SkeletonName.ToString()) as SkeletonDocument;
        }
    }

    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount = 1;

    private readonly List<Rect> _atlasUV = new();
    private Sprite? _sprite;
    public float Depth;
    public RectInt RasterBounds { get; private set; }
    public EdgeInsets Edges { get; set; } = EdgeInsets.Zero;

    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public PathOperation CurrentOperation;

    // Generation (optional — editor-only, stripped on Import)
    public string Prompt = "";
    public string NegativePrompt = "";
    public string Seed = "";
    public GenerationImage Generation { get; } = new();
    public string? StyleName;
    public GenStyleDocument? Style;
    public List<string> ReferenceNames { get; } = new();
    public List<SpriteDocument> References { get; } = new();
    public bool HasGeneration { get; set; }
    public bool IsGenerating => Generation.IsGenerating;

    public bool IsActiveLayerLocked => false;

    public Shape GetFrameShape(int frameIndex) => Frames[frameIndex].Shape;

    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    public ushort AtlasFrameCount => (ushort)TotalTimeSlots;
    internal AtlasDocument? Atlas { get; set; }

    public readonly SkeletonBinding Binding = new();
    SkeletonBinding ISkeletonBound.Binding => Binding;

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

    public SpriteDocument()
    {
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new SpriteFrame();
    }

    static SpriteDocument()
    {
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
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
        FrameCount = 1;

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
        Binding.Clear();
        Prompt = "";
        NegativePrompt = "";
        Seed = "";
        ReferenceNames.Clear();
        References.Clear();
        for (var fi = 0; fi < FrameCount; fi++)
            Frames[fi].Shape.Clear();
        FrameCount = 1;

        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        Binding.Resolve();
        if (HasGeneration)
        {
            LoadGeneratedTexture();
            if (!string.IsNullOrEmpty(StyleName))
                Style = DocumentManager.Find(GenStyleDocument.AssetTypeGenStyle, StyleName) as GenStyleDocument;

            References.Clear();
            foreach (var refName in ReferenceNames)
            {
                if (DocumentManager.Find(AssetType.Sprite, refName) is SpriteDocument refDoc)
                    References.Add(refDoc);
            }
        }
        UpdateBounds();
    }

    private void Load(ref Tokenizer tk)
    {
        FrameCount = 0;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("frame"))
            {
                var fi = FrameCount;
                if (fi >= Sprite.MaxFrames)
                    break;

                if (fi > 0 || Frames[0].Shape.PathCount > 0)
                    FrameCount = (ushort)(fi + 1);
                else
                    FrameCount = 1;

                var f = Frames[FrameCount - 1];

                if (tk.ExpectIdentifier("hold"))
                    f.Hold = tk.ExpectInt();

                while (!tk.IsEOF && tk.ExpectIdentifier("path"))
                    ParsePath(f, ref tk);

                if (FrameCount == 0)
                    FrameCount = 1;
            }
            else if (tk.ExpectIdentifier("path"))
            {
                if (FrameCount == 0)
                    FrameCount = 1;
                ParsePath(Frames[0], ref tk);
            }
            else if (tk.ExpectIdentifier("edges"))
            {
                if (tk.ExpectVec4(out var edgesVec))
                    Edges = new EdgeInsets(edgesVec.X, edgesVec.Y, edgesVec.Z, edgesVec.W);
            }
            else if (tk.ExpectIdentifier("skeleton"))
            {
                Binding.SkeletonName = StringId.Get(tk.ExpectQuotedString());
            }
            else if (tk.ExpectIdentifier("generate"))
            {
                ParseGeneration(ref tk);
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        if (FrameCount == 0)
            FrameCount = 1;
    }

    private void ParseGeneration(ref Tokenizer tk)
    {
        HasGeneration = true;
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("seed"))
            {
                if (tk.ExpectQuotedString(out var seedStr))
                    Seed = seedStr;
                else
                    Seed = tk.ExpectInt().ToString();
            }
            else if (tk.ExpectIdentifier("style"))
                StyleName = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("prompt_hash"))
                tk.ExpectQuotedString(); // Legacy: skip
            else if (tk.ExpectIdentifier("reference"))
                ReferenceNames.Add(tk.ExpectQuotedString() ?? "");
            else if (tk.ExpectIdentifier("image"))
            {
                // Legacy migration: extract embedded base64 to companion file
                var base64 = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(base64))
                {
                    Generation.ImageData = Convert.FromBase64String(base64);

                    // Migrate: write to companion file if not already present
                    if (ImageFilePath == null)
                    {
                        var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
                        var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
                        ImageFilePath = System.IO.Path.Combine(dir, stem + ".png");
                    }

                    if (!File.Exists(ImageFilePath))
                        File.WriteAllBytes(ImageFilePath, Generation.ImageData);
                }
            }
            else
                break;
        }

        // Load image data from companion file if not already loaded
        if (!Generation.HasImageData && ImageFilePath != null && File.Exists(ImageFilePath))
            Generation.ImageData = File.ReadAllBytes(ImageFilePath);
    }

    private void ParsePath(SpriteFrame f, ref Tokenizer tk)
    {
        var pathIndex = f.Shape.AddPath(Color32.White);
        var fillColor = Color32.White;
        var strokeColor = new Color32(0, 0, 0, 0);
        var strokeWidth = 1;
        var operation = PathOperation.Normal;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("fill"))
            {
                if (tk.ExpectColor(out var color))
                    fillColor = color.ToColor32();
                else
                {
                    fillColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(1.0f);
                    fillColor = fillColor.WithAlpha(legacyOpacity);
                }
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                if (tk.ExpectColor(out var color))
                    strokeColor = color.ToColor32();
                else
                {
                    strokeColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(0.0f);
                    strokeColor = strokeColor.WithAlpha(legacyOpacity);
                }
                strokeWidth = tk.ExpectInt(strokeWidth);
            }
            else if (tk.ExpectIdentifier("subtract"))
            {
                if (tk.ExpectBool())
                    operation = PathOperation.Subtract;
            }
            else if (tk.ExpectIdentifier("clip"))
            {
                if (tk.ExpectBool())
                    operation = PathOperation.Clip;
            }
            else if (tk.ExpectIdentifier("layer"))
            {
                // Legacy per-path layer — skip
                tk.ExpectQuotedString();
            }
            else if (tk.ExpectIdentifier("bone"))
            {
                // Legacy per-path bone — skip
                tk.ExpectQuotedString();
            }
            else if (tk.ExpectIdentifier("anchor"))
                ParseAnchor(f.Shape, pathIndex, ref tk);
            else
                break;
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor);
        f.Shape.SetPathStroke(pathIndex, strokeColor, (byte)strokeWidth);
        if (operation != PathOperation.Normal)
            f.Shape.SetPathOperation(pathIndex, operation);
    }

    private static void ParseAnchor(Shape shape, ushort pathIndex, ref Tokenizer tk)
    {
        var x = tk.ExpectFloat();
        var y = tk.ExpectFloat();
        var curve = tk.ExpectFloat();
        shape.AddAnchor(pathIndex, new Vector2(x, y), curve);
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

        if (FrameCount == 0)
        {
            SetDefaultBounds();
            return;
        }

        var first = true;
        var bounds = Rect.Zero;
        RasterBounds = RectInt.Zero;

        for (ushort fi = 0; fi < FrameCount; fi++)
        {
            var shape = Frames[fi].Shape;
            shape.UpdateSamples();
            shape.UpdateBounds();

            if (shape.AnchorCount == 0)
                continue;

            if (first)
            {
                bounds = shape.Bounds;
                RasterBounds = shape.RasterBounds;
                first = false;
            }
            else
            {
                var fb = shape.Bounds;
                var minX = MathF.Min(bounds.X, fb.X);
                var minY = MathF.Min(bounds.Y, fb.Y);
                var maxX = MathF.Max(bounds.Right, fb.Right);
                var maxY = MathF.Max(bounds.Bottom, fb.Bottom);
                bounds = Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));
                RasterBounds = RasterBounds.Union(shape.RasterBounds);
            }
        }

        if (first)
        {
            SetDefaultBounds();
            return;
        }

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

        if (Binding.IsBound)
            writer.WriteLine($"skeleton \"{Binding.SkeletonName}\"");

        if (FrameCount > 0)
            writer.WriteLine();

        for (ushort frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var f = Frames[frameIndex];
            var shape = f.Shape;

            if (FrameCount > 1 || f.Hold > 0)
            {
                writer.Write("frame");
                if (f.Hold > 0)
                    writer.Write($" hold {f.Hold}");
                writer.WriteLine();
            }

            if (shape.PathCount > 0)
                SaveFrame(shape, writer);
        }

        // Generation config (editor-only)
        if (HasGeneration)
        {
            writer.WriteLine("generate");
            if (!string.IsNullOrEmpty(Prompt))
                writer.WriteLine($"prompt \"{Prompt.Replace("\"", "\\\"")}\"");
            if (!string.IsNullOrEmpty(NegativePrompt))
                writer.WriteLine($"prompt_neg \"{NegativePrompt.Replace("\"", "\\\"")}\"");
            if (!string.IsNullOrEmpty(Seed))
                writer.WriteLine($"seed \"{Seed}\"");
            if (!string.IsNullOrEmpty(StyleName))
                writer.WriteLine($"style \"{StyleName}\"");
            foreach (var refName in ReferenceNames)
                writer.WriteLine($"reference \"{refName}\"");
            // prompt_hash moved to .meta
            // image data moved to companion file
            if (Generation.HasImageData && ImageFilePath != null)
            {
                File.WriteAllBytes(ImageFilePath, Generation.ImageData!);
            }
        }
    }

    private static void SaveFrame(Shape shape, StreamWriter writer)
    {
        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            ref readonly var path = ref shape.GetPath(pIdx);

            writer.WriteLine("path");
            if (path.IsSubtract)
                writer.WriteLine("subtract true");
            if (path.IsClip)
                writer.WriteLine("clip true");
            writer.WriteLine($"fill {FormatColor(path.FillColor)}");

            if (path.StrokeColor.A > 0)
                writer.WriteLine($"stroke {FormatColor(path.StrokeColor)} {path.StrokeWidth}");

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                ref readonly var anchor = ref shape.GetAnchor((ushort)(path.AnchorStart + aIdx));
                writer.Write(string.Format(CultureInfo.InvariantCulture, "anchor {0} {1}", anchor.Position.X, anchor.Position.Y));
                if (MathF.Abs(anchor.Curve) > float.Epsilon)
                    writer.Write(string.Format(CultureInfo.InvariantCulture, " {0}", anchor.Curve));
                writer.WriteLine();
            }

            writer.WriteLine();
        }
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
        else if (HasGeneration)
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

    private void DrawGeneration()
    {
        var texture = Generation.Texture;
        if (texture != null)
        {
            var ppu = EditorApplication.Config.PixelsPerUnitInv;
            var cs = ConstrainedSize ?? new Vector2Int(256, 256);
            var rect = new Rect(
                cs.X * ppu * -0.5f,
                cs.Y * ppu * -0.5f,
                cs.X * ppu,
                cs.Y * ppu);

            using (Graphics.PushState())
            {
                Graphics.SetTransform(Transform);
                Graphics.SetTexture(texture);
                Graphics.SetShader(EditorAssets.Shaders.Texture);
                var alpha = Generation.IsGenerating ? 0.3f : Workspace.XrayAlpha;
                Graphics.SetColor(Color.White.WithAlpha(alpha));
                Graphics.Draw(rect);
            }
        }
        else if (!DrawFromAtlas() && !DrawFromPreviewTexture())
        {
            DrawBounds();
        }

        if (Generation.IsGenerating)
        {
            var angle = Time.TotalTime * 3f;
            var rotation = Matrix3x2.CreateRotation(angle);
            var pulse = 0.7f + 0.3f * (0.5f + 0.5f * MathF.Sin(Time.TotalTime * 3f));
            var scale = Matrix3x2.CreateScale(pulse);

            using (Graphics.PushState())
            {
                Graphics.SetTransform(scale * rotation * Transform);
                Graphics.SetSortGroup(7);
                Graphics.SetLayer(EditorLayer.DocumentEditor);
                Graphics.SetColor(Color.White);
                Graphics.Draw(EditorAssets.Sprites.IconGenerating);
            }
        }
    }

    private void DrawVector()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (Frames[0].Shape.PathCount == 0)
        {
            DrawBounds();
            return;
        }

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
        Binding.CopyFrom(src.Binding);

        FrameCount = src.FrameCount;
        for (var fi = 0; fi < FrameCount; fi++)
        {
            Frames[fi].Shape.CopyFrom(src.Frames[fi].Shape);
            Frames[fi].Hold = src.Frames[fi].Hold;
        }

        // Generation
        Prompt = src.Prompt;
        NegativePrompt = src.NegativePrompt;
        Seed = src.Seed;
        ConstrainedSize = src.ConstrainedSize;
        StyleName = src.StyleName;
        Style = src.Style;
        if (src.Generation.HasImageData)
            Generation.ImageData = (byte[])src.Generation.ImageData!.Clone();
    }

    public override void LoadMetadata(PropertySet meta)
    {
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
        ConstrainedSize = ParseConstrainedSize(meta.GetString("sprite", "constrained_size", ""));

        // Backward compat: migrate style from metadata to file content
        if (string.IsNullOrEmpty(StyleName))
        {
            var style = meta.GetString("sprite", "style", "");
            if (!string.IsNullOrEmpty(style))
                StyleName = style;
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
        Binding.Resolve();

        if (HasGeneration)
        {
            LoadGeneratedTexture();
            if (!string.IsNullOrEmpty(StyleName))
                Style = DocumentManager.Find(GenStyleDocument.AssetTypeGenStyle, StyleName) as GenStyleDocument;
            Log.Info($"[Gen] '{Name}' PostLoad: style={Style?.Name ?? "null"} hasImage={Generation.HasImageData}");

            References.Clear();
            foreach (var refName in ReferenceNames)
            {
                if (DocumentManager.Find(AssetType.Sprite, refName) is SpriteDocument refDoc)
                    References.Add(refDoc);
            }
        }
    }

    public override void GetReferences(List<Document> references)
    {
        references.AddRange(References);
    }

    internal void ClearAtlasUVs()
    {
        _atlasUV.Clear();
        MarkSpriteDirty();
    }

    internal void Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        // Static image or companion image: blit from file
        if (ImageFilePath != null && File.Exists(ImageFilePath) && (!IsMutable || (HasGeneration && Generation.HasImageData)))
        {
            RasterizeImageFile(image, rect, padding);
            return;
        }

        // Generated sprites: blit texture pixels instead of rasterizing paths
        if (HasGeneration && Generation.HasImageData)
        {
            var w = RasterBounds.Size.X;
            var h = RasterBounds.Size.Y;

            using var ms = new MemoryStream(Generation.ImageData!);
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
        var shape = Frames[fi].Shape;

        var targetRect = new RectInt(
            rect.Rect.Position,
            new Vector2Int(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2));
        var sourceOffset = -RasterBounds.Position + new Vector2Int(padding, padding);

        RasterizeShape(shape, image, targetRect, sourceOffset, dpi);

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

    private static void RasterizeShape(
        Shape shape,
        PixelData<Color32> image,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi)
    {
        // Collect subtract paths
        List<(ushort PathIndex, Clipper2Lib.PathsD Contours)>? subtractEntries = null;
        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (!path.IsSubtract || path.AnchorCount < 3) continue;

            var subShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(subShape, shape, pi);
            var subContours = Msdf.ShapeClipper.ShapeToPaths(subShape, 8);
            if (subContours.Count > 0)
            {
                subtractEntries ??= new();
                subtractEntries.Add((pi, subContours));
            }
        }

        Clipper2Lib.PathsD? accumulatedPaths = null;

        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (path.IsSubtract || path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
            pathShape = Msdf.ShapeClipper.Union(pathShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
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
                    var halfStroke = path.StrokeWidth * Shape.StrokeScale;
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

            // Apply subtract paths (higher path index subtracts from lower)
            if (subtractEntries != null)
            {
                Clipper2Lib.PathsD? subtractPaths = null;
                foreach (var (subPi, subContours) in subtractEntries)
                {
                    if (subPi <= pi) continue;
                    subtractPaths ??= new Clipper2Lib.PathsD();
                    subtractPaths.AddRange(subContours);
                }

                if (subtractPaths is { Count: > 0 })
                {
                    contours = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                        contours, subtractPaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                    if (contours.Count == 0) continue;
                }
            }

            var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
            var hasFill = path.FillColor.A > 0;

            if (hasStroke)
            {
                var halfStroke = path.StrokeWidth * Shape.StrokeScale;
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
        if (Atlas == null || FrameCount == 0)
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
        Binding.Resolve();
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

    #region Generation

    internal void LoadGeneratedTexture()
    {
        Generation.Dispose();

        if (!Generation.HasImageData) return;

        try
        {
            using var ms = new MemoryStream(Generation.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

            var cs = ConstrainedSize ?? new Vector2Int(256, 256);
            if (srcImage.Width != cs.X || srcImage.Height != cs.Y)
                srcImage.Mutate(x => x.Resize(cs.X, cs.Y));

            var w = srcImage.Width;
            var h = srcImage.Height;
            var pixels = new byte[w * h * 4];
            srcImage.CopyPixelDataTo(pixels);
            Generation.Texture = Texture.Create(w, h, pixels, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create generated texture for '{Name}': {ex.Message}");
        }
    }

    private const int RasterizeSize = 1024;

    private byte[] RasterizeMaskToPng()
    {
        UpdateBounds();
        var cs = ConstrainedSize ?? new Vector2Int(256, 256);
        if (cs.X <= 0 || cs.Y <= 0)
            return [];

        var shape = Frames[0].Shape;
        var w = RasterizeSize;
        var h = RasterizeSize;
        var dpi = (int)(EditorApplication.Config.PixelsPerUnit * ((float)RasterizeSize / cs.X));

        using var pixels = new PixelData<Color32>(w, h);
        pixels.Clear(new Color32(0, 0, 0, 255));
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = new Vector2Int(w / 2, h / 2);
        var white = new Color32(255, 255, 255, 255);

        // Collect subtract paths
        var negativePaths = new Clipper2Lib.PathsD();
        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (!path.IsSubtract || path.AnchorCount < 3) continue;

            var subShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(subShape, shape, pi);
            subShape = Msdf.ShapeClipper.Union(subShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(subShape, 8);
            if (contours.Count > 0)
                negativePaths.AddRange(contours);
        }

        // Collect normal paths (clip paths are always within normal bounds, skip for mask)
        var positivePaths = new Clipper2Lib.PathsD();

        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (path.IsSubtract || path.IsClip || path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
            pathShape = Msdf.ShapeClipper.Union(pathShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
            if (contours.Count > 0)
                positivePaths.AddRange(contours);
        }

        if (positivePaths.Count == 0)
            return [];

        Clipper2Lib.PathsD maskPaths;
        if (negativePaths.Count > 0)
        {
            maskPaths = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                positivePaths, negativePaths, Clipper2Lib.FillRule.NonZero, precision: 6);
        }
        else
        {
            maskPaths = positivePaths;
        }

        if (maskPaths.Count > 0)
            Rasterizer.Fill(maskPaths, pixels, targetRect, sourceOffset, dpi, white);

        using var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = pixels[x, y];
                img[x, y] = new Rgba32(c.R, c.G, c.B, 255);
            }
        }

        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        var pngBytes = ms.ToArray();

        try
        {
            var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
            Directory.CreateDirectory(tmpDir);
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_mask.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    private byte[] RasterizeColorToPng()
    {
        UpdateBounds();
        var cs = ConstrainedSize ?? new Vector2Int(256, 256);
        if (cs.X <= 0 || cs.Y <= 0)
            return [];

        var shape = Frames[0].Shape;
        var w = RasterizeSize;
        var h = RasterizeSize;
        var dpi = (int)(EditorApplication.Config.PixelsPerUnit * ((float)RasterizeSize / cs.X));

        using var pixels = new PixelData<Color32>(w, h);
        pixels.Clear(new Color32(255, 255, 255, 255));
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = new Vector2Int(w / 2, h / 2);

        // Collect subtract paths
        var negativePaths = new Clipper2Lib.PathsD();
        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (!path.IsSubtract || path.AnchorCount < 3) continue;

            var subShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(subShape, shape, pi);
            subShape = Msdf.ShapeClipper.Union(subShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(subShape, 8);
            if (contours.Count > 0)
                negativePaths.AddRange(contours);
        }

        // Render each normal/clip path with its fill and stroke colors
        Clipper2Lib.PathsD? accumulatedPaths = null;

        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (path.IsSubtract || path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
            pathShape = Msdf.ShapeClipper.Union(pathShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
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
                    var halfStroke = path.StrokeWidth * Shape.StrokeScale;
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

            // Apply subtract paths
            if (negativePaths.Count > 0)
            {
                contours = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                    contours, negativePaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }

            var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
            var fillColor = path.FillColor;
            var hasFill = fillColor.A > 0;

            if (hasStroke)
            {
                var halfStroke = path.StrokeWidth * Shape.StrokeScale;
                Clipper2Lib.PathsD? contractedPaths = null;
                if (contours.Count > 0)
                {
                    contractedPaths = Clipper2Lib.Clipper.InflatePaths(contours, -halfStroke,
                        Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, precision: 6);
                }

                if (hasFill)
                {
                    Rasterizer.Fill(contours, pixels, targetRect, sourceOffset, dpi, path.StrokeColor);
                    if (contractedPaths is { Count: > 0 })
                        Rasterizer.Fill(contractedPaths, pixels, targetRect, sourceOffset, dpi, fillColor);
                }
                else
                {
                    if (contractedPaths is { Count: > 0 })
                    {
                        var strokeRing = Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference,
                            contours, contractedPaths, Clipper2Lib.FillRule.NonZero, precision: 6);
                        if (strokeRing.Count > 0)
                            Rasterizer.Fill(strokeRing, pixels, targetRect, sourceOffset, dpi, path.StrokeColor);
                    }
                    else
                    {
                        Rasterizer.Fill(contours, pixels, targetRect, sourceOffset, dpi, path.StrokeColor);
                    }
                }
            }
            else if (hasFill)
            {
                Rasterizer.Fill(contours, pixels, targetRect, sourceOffset, dpi, fillColor);
            }
        }

        using var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = pixels[x, y];
                img[x, y] = new Rgba32(c.R, c.G, c.B, 255);
            }
        }

        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        var pngBytes = ms.ToArray();

        try
        {
            var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
            Directory.CreateDirectory(tmpDir);
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_color.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    public GenerationRequest BuildGenerationRequest()
    {
        var prompt = GenStyleDocument.FormatPrompt(Style?.Prompt ?? "", Prompt);
        var negPrompt = GenStyleDocument.FormatPrompt(Style?.NegativePrompt ?? "", NegativePrompt);

        var imageBytes = RasterizeColorToPng();

        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";

        var images = new List<ImageRef>();
        if (imageBytes.Length > 0)
            images.Add(new ImageRef { Data = Convert.ToBase64String(imageBytes) });

        foreach (var refDoc in References)
        {
            byte[] refBytes;
            if (refDoc.Generation.HasImageData)
                refBytes = refDoc.Generation.ImageData!;
            else
                refBytes = refDoc.RasterizeColorToPng();

            if (refBytes.Length > 0)
                images.Add(new ImageRef { Data = Convert.ToBase64String(refBytes) });
        }

        return new GenerationRequest
        {
            Server = server,
            Model = Style?.ModelName,
            Lora = Style?.LoraName,
            Images = images.Count > 0 ? images : null,
            Prompt = prompt,
            NegativePrompt = string.IsNullOrEmpty(negPrompt) ? null : negPrompt,
            Seed = string.IsNullOrEmpty(Seed) ? null : Seed,
        };
    }

    public void ApplyGenerationResult(GenerationStatus status, bool createTexture = true)
    {
        var cs = ConstrainedSize ?? new Vector2Int(256, 256);

        if (status.Result == null)
        {
            Log.Error($"Generation completed but no result for '{Name}'");
            return;
        }

        try
        {
            var imageResultBytes = Convert.FromBase64String(status.Result.Image);

            using var ms = new MemoryStream(imageResultBytes);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

            if (srcImage.Width != cs.X || srcImage.Height != cs.Y)
                srcImage.Mutate(x => x.Resize(cs.X, cs.Y));

            using var outMs = new MemoryStream();
            srcImage.SaveAsPng(outMs);
            Generation.ImageData = outMs.ToArray();

            if (createTexture)
            {
                Generation.Dispose();
                var rw = srcImage.Width;
                var rh = srcImage.Height;
                var px = new byte[rw * rh * 4];
                srcImage.CopyPixelDataTo(px);
                Generation.Texture = Texture.Create(rw, rh, px, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");
            }

            try
            {
                var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
                Directory.CreateDirectory(tmpDir);
                File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_gen.png"), Generation.ImageData);
            }
            catch { }

            if (!string.IsNullOrEmpty(status.Result.Seed) && string.IsNullOrEmpty(Seed))
                Seed = status.Result.Seed;

            // Ensure companion image path exists so Save() can write the .png
            if (ImageFilePath == null)
            {
                var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
                var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
                ImageFilePath = System.IO.Path.Combine(dir, stem + ".png");
            }

            Log.Info($"[Gen] '{Name}' ApplyResult: imagePath='{ImageFilePath}' hasImage={Generation.HasImageData}");
            Log.Info($"Generation complete for '{Name}' ({status.Result.Width}x{status.Result.Height}, seed={status.Result.Seed})");
            IncrementVersion();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to process generated image for '{Name}': {ex.Message}");
        }
    }

    public void GenerateAsync()
    {
        if (string.IsNullOrEmpty(Prompt))
        {
            Log.Error($"No prompt set for '{Name}'");
            return;
        }

        if (string.IsNullOrEmpty(Style?.ModelName))
        {
            Log.Error($"Invalid style for '{Name}': no model specified");
            return;
        }

        if (Generation.IsGenerating)
            return;

        var request = BuildGenerationRequest();

        Log.Info($"Starting generation for '{Name}' on {request.Server}...");

        var genImage = Generation;
        genImage.IsGenerating = true;
        genImage.GenerationError = null;

        var cts = new System.Threading.CancellationTokenSource();
        genImage.CancellationSource = cts;

        GenerationClient.Generate(request, status =>
        {
            if (genImage.CancellationSource == null)
                return;

            switch (status.State)
            {
                case GenerationState.Queued:
                    if (genImage.GenerationState != GenerationState.Queued || genImage.QueuePosition != status.QueuePosition)
                        Log.Info($"Generation queued for '{Name}' (position {status.QueuePosition})");
                    genImage.GenerationState = GenerationState.Queued;
                    genImage.QueuePosition = status.QueuePosition;
                    genImage.GenerationProgress = 0f;
                    break;

                case GenerationState.Running:
                    genImage.GenerationState = GenerationState.Running;
                    genImage.GenerationProgress = status.Progress;
                    break;

                case GenerationState.Completed:
                    genImage.IsGenerating = false;
                    genImage.CancellationSource = null;
                    genImage.GenerationState = GenerationState.Completed;
                    genImage.GenerationProgress = 1f;
                    ApplyGenerationResult(status);
                    Log.Info($"[Gen] '{Name}' saving after generation...");
                    Save();
                    SaveMetadata();
                    DocumentManager.QueueExport(this, force: true);
                    Log.Info($"[Gen] '{Name}' saved and queued for export");
                    break;

                case GenerationState.Failed:
                    genImage.IsGenerating = false;
                    genImage.CancellationSource = null;
                    genImage.GenerationState = GenerationState.Failed;
                    genImage.GenerationError = status.Error;
                    Log.Error($"Generation failed for '{Name}': {status.Error}");
                    break;
            }
        }, cts.Token);
    }

    #endregion

    public override void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        Generation.Dispose();
        for (var fi = 0; fi < FrameCount; fi++)
            Frames[fi].Dispose();
        base.Dispose();
    }
}
