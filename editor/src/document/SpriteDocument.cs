//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class SpriteDocument : Document, ISpriteSource, IShapeDocument
{
    public override bool CanSave => true;

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

    public bool HasGeneration { get; set; }
    public bool IsGenerating => Generation.IsGenerating;

    public bool IsActiveLayerLocked => false;

    public Shape GetFrameShape(int frameIndex) => Frames[frameIndex].Shape;

    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    ushort ISpriteSource.FrameCount => (ushort)TotalTimeSlots;
    AtlasDocument? ISpriteSource.Atlas { get => Atlas; set => Atlas = value; }
    internal AtlasDocument? Atlas { get; set; }

    public readonly SkeletonBinding Binding = new();

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
            Extension = ".sprite",
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
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    public override void Reload()
    {
        Edges = EdgeInsets.Zero;
        Binding.Clear();
        Prompt = "";
        NegativePrompt = "";
        Seed = "";

        for (var fi = 0; fi < FrameCount; fi++)
            Frames[fi].Shape.Clear();
        FrameCount = 1;

        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        Binding.Resolve();
        if (HasGeneration)
            LoadGeneratedTexture();
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
            else if (tk.ExpectIdentifier("image"))
            {
                var base64 = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(base64))
                    Generation.ImageData = Convert.FromBase64String(base64);
            }
            else
                break;
        }
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
        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            var ppu = EditorApplication.Config.PixelsPerUnitInv;
            Bounds = new Rect(
                cs.X * ppu * -0.5f,
                cs.Y * ppu * -0.5f,
                cs.X * ppu,
                cs.Y * ppu);
            RasterBounds = new RectInt(
                -cs.X / 2,
                -cs.Y / 2,
                cs.X,
                cs.Y);

            return;
        }

        if (FrameCount == 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
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
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        Bounds = bounds;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            var centerX = RasterBounds.X + RasterBounds.Width / 2;
            var centerY = RasterBounds.Y + RasterBounds.Height / 2;
            RasterBounds = new RectInt(
                centerX - cs.X / 2,
                centerY - cs.Y / 2,
                cs.X,
                cs.Y);
        }

        ClampToMaxSpriteSize();
        Bounds = RasterBounds.ToRect().Scale(1.0f / EditorApplication.Config.PixelsPerUnit);
        MarkSpriteDirty();
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

            if (Generation.HasImageData)
            {
                writer.WriteLine();
                writer.WriteLine($"image \"{Convert.ToBase64String(Generation.ImageData!)}\"");
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

        if (HasGeneration)
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
            else
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

            return;
        }

        var size = Bounds.Size;
        if (size.X <= 0 || size.Y <= 0 || Atlas == null)
            return;

        var hasContent = Frames[0].Shape.PathCount > 0;
        if (!hasContent)
        {
            DrawBounds();
            return;
        }

        DrawSprite();
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

            var fi = sprite.FrameTable[frame];
            for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
            {
                ref readonly var mesh = ref sprite.Meshes[i];

                Rect bounds;
                if (mesh.Size.X > 0 && mesh.Size.Y > 0)
                {
                    bounds = new Rect(
                        mesh.Offset.X * Graphics.PixelsPerUnitInv,
                        mesh.Offset.Y * Graphics.PixelsPerUnitInv,
                        mesh.Size.X * Graphics.PixelsPerUnitInv,
                        mesh.Size.Y * Graphics.PixelsPerUnitInv).Translate(offset);
                }
                else
                {
                    bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv).Translate(offset);
                }

                Graphics.Draw(bounds, mesh.UV, order: (ushort)mesh.SortOrder);
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

            var fi = sprite.FrameTable[frame];
            for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
            {
                ref readonly var mesh = ref sprite.Meshes[i];

                Rect bounds;
                if (mesh.Size.X > 0 && mesh.Size.Y > 0)
                {
                    bounds = new Rect(
                        mesh.Offset.X * Graphics.PixelsPerUnitInv,
                        mesh.Offset.Y * Graphics.PixelsPerUnitInv,
                        mesh.Size.X * Graphics.PixelsPerUnitInv,
                        mesh.Size.Y * Graphics.PixelsPerUnitInv);
                }
                else
                {
                    bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv);
                }

                Graphics.SetColor(Color.White);

                var boneIndex = mesh.BoneIndex >= 0 ? mesh.BoneIndex : 0;
                var transform = bindPose[boneIndex] * animatedPose[boneIndex] * baseTransform;
                Graphics.SetTransform(transform);
                Graphics.Draw(bounds, mesh.UV, order: (ushort)mesh.SortOrder);
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
        }
    }


    void ISpriteSource.ClearAtlasUVs() => ClearAtlasUVs();

    internal void ClearAtlasUVs()
    {
        _atlasUV.Clear();
        MarkSpriteDirty();
    }

    void ISpriteSource.Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
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

    void ISpriteSource.UpdateAtlasUVs(AtlasDocument atlas, ReadOnlySpan<AtlasSpriteRect> allRects, int padding)
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
                if (allRects[i].Source == (ISpriteSource)this && allRects[i].FrameIndex == frameIndex)
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
        var frameTable = new SpriteFrameInfo[totalSlots];
        var allMeshes = new List<SpriteMesh>();

        for (int frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var uv = GetAtlasUV(frameIndex);
            if (uv == Rect.Zero)
            {
                _sprite = null;
                return;
            }

            var meshStart = (ushort)allMeshes.Count;
            allMeshes.Add(new SpriteMesh(
                uv,
                order: 0,
                boneIndex: -1,
                RasterBounds.Position,
                RasterBounds.Size));

            frameTable[frameIndex] = new SpriteFrameInfo(meshStart, 1);
        }

        _sprite = Sprite.Create(
            name: Name,
            bounds: RasterBounds,
            pixelsPerUnit: EditorApplication.Config.PixelsPerUnit,
            filter: TextureFilter.Linear,
            boneIndex: -1,
            meshes: allMeshes.ToArray(),
            frameTable: frameTable,
            frameRate: 12.0f,
            edges: ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero,
            sliceMask: Sprite.CalculateSliceMask(RasterBounds, ConstrainedSize.HasValue ? Edges : EdgeInsets.Zero));
    }

    internal void MarkSpriteDirty()
    {
        _sprite?.Dispose();
        _sprite = null;
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        Binding.Resolve();
        UpdateBounds();

        var totalSlots = (ushort)TotalTimeSlots;

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(totalSlots);
        writer.Write((ushort)(Atlas?.Index ?? 0));
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
            var uv = GetAtlasUV(frameIndex);
            WriteMesh(writer, uv, sortOrder: 0, boneIndex: -1, RasterBounds);
        }

        // Frame table
        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            writer.Write(frameIndex);   // meshStart
            writer.Write((ushort)1);    // meshCount
        }
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

        if (!IsEditing)
            AtlasManager.UpdateSource(this);

        base.OnUndoRedo();
    }

    public Vector2Int GetFrameAtlasSize(ushort timeSlot)
    {
        var padding2 = EditorApplication.Config.AtlasPadding * 2;
        return new(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2);
    }

    #region Generation

    private static long HashSeed(string seed)
    {
        if (string.IsNullOrEmpty(seed))
            return 0;

        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var hash = BitConverter.ToInt64(bytes, 0);
        return (hash & 0x3FFFFFFFFFFFF) | 1; // 50-bit max for JS safety, positive and non-zero
    }

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

    public void GenerateAsync()
    {
        if (string.IsNullOrEmpty(Prompt))
        {
            Log.Error($"No prompt set for '{Name}'");
            return;
        }

        if (Generation.IsGenerating)
            return;

        var globalPromptPrefix = Style?.PromptPrefix ?? "";
        var globalPrompt = Style?.Prompt ?? "";
        var globalNegPrompt = Style?.NegativePrompt ?? "";

        var workflow = Style?.Workflow ?? GenerationWorkflow.Sprite;

        // Rasterize image (color or mask depending on workflow)
        var imageBytes = RasterizeColorToPng();
        var maskBytes = RasterizeMaskToPng();

        var prompt = Prompt;
        if (!string.IsNullOrEmpty(globalPromptPrefix))
            prompt = $"{globalPromptPrefix} {prompt}";
        if (!string.IsNullOrEmpty(globalPrompt))
            prompt = $"{prompt}, {globalPrompt}";
        var negPrompt = NegativePrompt;
        if (!string.IsNullOrEmpty(globalNegPrompt))
            negPrompt = string.IsNullOrEmpty(negPrompt) ? globalNegPrompt : $"{negPrompt}, {globalNegPrompt}";

        var hashedSeed = HashSeed(Seed);
        var seed = hashedSeed == 0 ? Random.Shared.NextInt64(1, long.MaxValue) : hashedSeed;

        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";

        var request = new GenerationRequest
        {
            Server = server,
            Workflow = (Style?.Workflow ?? GenerationWorkflow.Sprite).ToString().ToLowerInvariant(),
            Model = Style?.ModelName,
            Image = imageBytes.Length > 0 ? $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}" : "",
            Mask = maskBytes.Length > 0 ? new MaskConfig { Image = $"data:image/png;base64,{Convert.ToBase64String(maskBytes)}" } : null,
            Prompt = prompt,
            NegativePrompt = string.IsNullOrEmpty(negPrompt) ? null : negPrompt,
            Seed = seed,
        };

        Log.Info($"Starting generation for '{Name}' on {server}...");

        var genImage = Generation;
        genImage.IsGenerating = true;
        genImage.GenerationError = null;

        var cts = new System.Threading.CancellationTokenSource();
        genImage.CancellationSource = cts;

        var cs = ConstrainedSize ?? new Vector2Int(256, 256);

        GenerationClient.Generate(request, status =>
        {
            if (genImage.CancellationSource == null)
                return;

            switch (status.State)
            {
                case GenerationState.Queued:
                    genImage.GenerationState = GenerationState.Queued;
                    genImage.QueuePosition = status.QueuePosition;
                    genImage.GenerationProgress = 0f;
                    Log.Info($"Generation queued for '{Name}' (position {status.QueuePosition})");
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
                        genImage.ImageData = outMs.ToArray();

                        genImage.Dispose();
                        var rw = srcImage.Width;
                        var rh = srcImage.Height;
                        var px = new byte[rw * rh * 4];
                        srcImage.CopyPixelDataTo(px);
                        genImage.Texture = Texture.Create(rw, rh, px, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");

                        try
                        {
                            var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
                            Directory.CreateDirectory(tmpDir);
                            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_gen.png"), genImage.ImageData);
                        }
                        catch { }

                        // Update seed from server response
                        if (status.Result.Seed != 0 && string.IsNullOrEmpty(Seed))
                            Seed = status.Result.Seed.ToString();

                        Log.Info($"Generation complete for '{Name}' ({status.Result.Width}x{status.Result.Height}, seed={status.Result.Seed})");
                        IncrementVersion();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to process generated image for '{Name}': {ex.Message}");
                    }
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
        Generation.Dispose();
        for (var fi = 0; fi < FrameCount; fi++)
            Frames[fi].Dispose();
        base.Dispose();
    }
}
