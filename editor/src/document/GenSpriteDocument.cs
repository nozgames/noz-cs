//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Security.Cryptography;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public partial class GenSpriteDocument : Document, IShapeDocument
{
    public static readonly AssetType AssetTypeGenSprite = AssetType.FromString("GNSP");

    public override bool CanSave => true;

    public const int MaxDocumentLayers = 32;

    private readonly List<GenSpriteLayer> _layers = new();

    public Vector2Int ConstrainedSize { get; set; } = new(256, 256);
    public GenerationImage Generation { get; } = new();
    public string? StyleName;
    public GenStyleDocument? Style;
    public int ActiveLayerIndex;
    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);

    public IReadOnlyList<GenSpriteLayer> Layers => _layers;
    public GenSpriteLayer ActiveLayer => _layers[ActiveLayerIndex];
    public bool IsActiveLayerLocked => false; // GenSprite layers are never locked

    public bool HasGeneration => _layers.Any(l => l.HasPrompt);
    public bool IsGenerating => Generation.IsGenerating;

    private static long HashSeed(string seed)
    {
        if (string.IsNullOrEmpty(seed))
            return 0;

        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var hash = BitConverter.ToInt64(bytes, 0);
        return (hash & 0x3FFFFFFFFFFFF) | 1; // 50-bit max for JS safety, positive and non-zero
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetTypeGenSprite,
            Name = "GenSprite",
            Extension = ".gensprite",
            Factory = () => new GenSpriteDocument(),
            EditorFactory = doc => new GenSpriteEditor((GenSpriteDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    public GenSpriteDocument()
    {
        IsEditorOnly = true;
        _layers.Add(new GenSpriteLayer { Name = "Layer 1", Index = 0 });
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("layer \"Layer 1\"");
        writer.WriteLine("generate");
        writer.WriteLine("prompt \"\"");
    }

    #region Load / Save

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Parse(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    public override void PostLoad()
    {
        LoadGeneratedTexture();

        if (!string.IsNullOrEmpty(StyleName))
            Style = DocumentManager.Find(GenStyleDocument.AssetTypeGenStyle, StyleName) as GenStyleDocument;
    }

    private void Parse(ref Tokenizer tk)
    {
        _layers.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
            {
                ParseLayer(ref tk);
            }
            else if (tk.ExpectIdentifier("gen_image"))
            {
                var base64 = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(base64))
                    Generation.ImageData = Convert.FromBase64String(base64);
            }
            // Backward compat: consume old lora/refine tokens
            else if (tk.ExpectIdentifier("lora"))
                tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("lora_strength"))
                tk.ExpectFloat(out _);
            // Backward compat: consume old refine block
            else if (tk.ExpectIdentifier("refine"))
                ConsumeGenerationConfig(ref tk);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"GenSpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        if (_layers.Count == 0)
            _layers.Add(new GenSpriteLayer { Name = "Layer 1", Index = 0 });
    }

    private void ParseLayer(ref Tokenizer tk)
    {
        var layer = new GenSpriteLayer
        {
            Name = tk.ExpectQuotedString() ?? $"Layer {_layers.Count + 1}",
            Index = _layers.Count,
        };
        _layers.Add(layer);

        // Parse generation config (mandatory for gensprite layers)
        if (tk.ExpectIdentifier("generate"))
            ParseGenerationConfig(ref tk, layer);

        // Parse mask paths
        while (!tk.IsEOF && tk.ExpectIdentifier("path"))
            ParsePath(layer.Shape, ref tk);
    }

    private static void ParsePath(Shape shape, ref Tokenizer tk)
    {
        var pathIndex = shape.AddPath(Color32.White);
        var fillColor = Color32.White;
        var strokeColor = new Color32(0, 0, 0, 0);
        var strokeWidth = 0;
        var operation = PathOperation.Normal;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("fill"))
            {
                if (tk.ExpectColor(out var color))
                    fillColor = color.ToColor32();
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                if (tk.ExpectColor(out var color))
                    strokeColor = color.ToColor32();
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
            else if (tk.ExpectIdentifier("anchor"))
            {
                var x = tk.ExpectFloat();
                var y = tk.ExpectFloat();
                var curve = tk.ExpectFloat();
                shape.AddAnchor(pathIndex, new Vector2(x, y), curve);
            }
            else
                break;
        }

        shape.SetPathFillColor(pathIndex, fillColor);
        if (strokeColor.A > 0)
            shape.SetPathStroke(pathIndex, strokeColor, (byte)strokeWidth);
        if (operation != PathOperation.Normal)
            shape.SetPathOperation(pathIndex, operation);
    }

    private static void ParseGenerationConfig(ref Tokenizer tk, GenSpriteLayer layer)
    {
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                layer.Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                layer.NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("seed"))
            {
                if (tk.ExpectQuotedString(out var seedStr))
                    layer.Seed = seedStr;
                else
                    layer.Seed = tk.ExpectInt().ToString();
            }
            // Backward compat: consume old fields
            else if (tk.ExpectIdentifier("combine"))
                tk.ExpectBool();
            else if (tk.ExpectIdentifier("strength"))
                tk.ExpectFloat(out _);
            else if (tk.ExpectIdentifier("steps"))
                tk.ExpectInt(out _);
            else if (tk.ExpectIdentifier("guidance_scale"))
                tk.ExpectFloat(out _);
            else
                break;
        }
    }

    private static void ConsumeGenerationConfig(ref Tokenizer tk)
    {
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("prompt_neg"))
                tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("seed"))
                tk.ExpectInt(out _);
            else if (tk.ExpectIdentifier("strength"))
                tk.ExpectFloat(out _);
            else if (tk.ExpectIdentifier("steps"))
                tk.ExpectInt(out _);
            else if (tk.ExpectIdentifier("guidance_scale"))
                tk.ExpectFloat(out _);
            else if (tk.ExpectIdentifier("combine"))
                tk.ExpectBool();
            else
                break;
        }
    }

    public override void Save(StreamWriter writer)
    {
        for (var layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
        {
            var layer = _layers[layerIndex];

            writer.WriteLine($"layer \"{layer.Name}\"");

            // Generation config (always present)
            writer.WriteLine("generate");
            WriteGenerationConfig(writer, layer);

            // Mask paths
            SaveMaskPaths(layer.Shape, writer);

            if (layerIndex < _layers.Count - 1)
                writer.WriteLine();
        }

        // Document-level generation image
        if (Generation.HasImageData)
        {
            writer.WriteLine();
            writer.WriteLine($"gen_image \"{Convert.ToBase64String(Generation.ImageData!)}\"");
        }

    }

    private static void WriteGenerationConfig(StreamWriter writer, GenSpriteLayer layer)
    {
        if (!string.IsNullOrEmpty(layer.Prompt))
            writer.WriteLine($"prompt \"{layer.Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(layer.NegativePrompt))
            writer.WriteLine($"prompt_neg \"{layer.NegativePrompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(layer.Seed))
            writer.WriteLine($"seed \"{layer.Seed}\"");
    }

    private static void SaveMaskPaths(Shape shape, StreamWriter writer)
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

    #endregion

    #region Metadata

    public override void LoadMetadata(PropertySet meta)
    {
        var sizeStr = meta.GetString("gensprite", "constrained_size", "256x256");
        var parts = sizeStr.Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h))
        {
            ConstrainedSize = new Vector2Int(w, h);
        }

        var style = meta.GetString("gensprite", "style", "");
        StyleName = string.IsNullOrEmpty(style) ? null : style;

    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetString("gensprite", "constrained_size", $"{ConstrainedSize.X}x{ConstrainedSize.Y}");

        if (!string.IsNullOrEmpty(StyleName))
            meta.SetString("gensprite", "style", StyleName);
    }

    #endregion

    #region Bounds

    public void UpdateBounds()
    {
        var cs = ConstrainedSize;
        var ppu = EditorApplication.Config.PixelsPerUnitInv;
        Bounds = new Rect(
            cs.X * ppu * -0.5f,
            cs.Y * ppu * -0.5f,
            cs.X * ppu,
            cs.Y * ppu);

        // Update all layer shape samples
        foreach (var layer in _layers)
        {
            layer.Shape.UpdateSamples();
            layer.Shape.UpdateBounds();
        }
    }

    #endregion

    #region Layers

    public int AddLayer()
    {
        if (_layers.Count >= MaxDocumentLayers)
            return -1;

        var name = $"Layer {_layers.Count + 1}";
        _layers.Add(new GenSpriteLayer { Name = name, Index = _layers.Count });
        ActiveLayerIndex = _layers.Count - 1;
        return ActiveLayerIndex;
    }

    public void RemoveLayer(int index)
    {
        if (index < 0 || index >= _layers.Count || _layers.Count <= 1)
            return;

        _layers[index].Dispose();
        _layers.RemoveAt(index);

        for (var i = index; i < _layers.Count; i++)
            _layers[i].Index = i;

        if (ActiveLayerIndex >= _layers.Count)
            ActiveLayerIndex = _layers.Count - 1;

        UpdateBounds();
    }

    public void MoveLayer(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _layers.Count ||
            toIndex < 0 || toIndex >= _layers.Count ||
            fromIndex == toIndex)
            return;

        var layer = _layers[fromIndex];
        _layers.RemoveAt(fromIndex);
        _layers.Insert(toIndex, layer);

        for (var i = 0; i < _layers.Count; i++)
            _layers[i].Index = i;

        if (ActiveLayerIndex == fromIndex)
            ActiveLayerIndex = toIndex;
        else if (fromIndex < toIndex && ActiveLayerIndex > fromIndex && ActiveLayerIndex <= toIndex)
            ActiveLayerIndex--;
        else if (fromIndex > toIndex && ActiveLayerIndex >= toIndex && ActiveLayerIndex < fromIndex)
            ActiveLayerIndex++;

        UpdateBounds();
    }

    #endregion

    #region Generation

    internal void LoadGeneratedTexture()
    {
        Generation.Dispose();

        if (!Generation.HasImageData) return;

        try
        {
            using var ms = new MemoryStream(Generation.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

            var cs = ConstrainedSize;
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

    private byte[] RasterizeMaskToPng(int targetLayerIndex)
    {
        UpdateBounds();
        var cs = ConstrainedSize;
        if (cs.X <= 0 || cs.Y <= 0)
            return [];

        var w = RasterizeSize;
        var h = RasterizeSize;
        var dpi = (int)(EditorApplication.Config.PixelsPerUnit * ((float)RasterizeSize / cs.X));

        using var pixels = new PixelData<Color32>(w, h);
        pixels.Clear(new Color32(0, 0, 0, 255));
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = new Vector2Int(w / 2, h / 2);
        var white = new Color32(255, 255, 255, 255);

        var positivePaths = new Clipper2Lib.PathsD();
        var negativePaths = new Clipper2Lib.PathsD();

        var shape = _layers[targetLayerIndex].Shape;
        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
            pathShape = Msdf.ShapeClipper.Union(pathShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
            if (contours.Count == 0) continue;

            if (path.IsSubtract)
                negativePaths.AddRange(contours);
            else
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
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_mask_L{targetLayerIndex}.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    private byte[] RasterizeAllPathsMaskToPng()
    {
        UpdateBounds();
        var cs = ConstrainedSize;
        if (cs.X <= 0 || cs.Y <= 0)
            return [];

        var w = RasterizeSize;
        var h = RasterizeSize;
        var dpi = (int)(EditorApplication.Config.PixelsPerUnit * ((float)RasterizeSize / cs.X));

        using var pixels = new PixelData<Color32>(w, h);
        pixels.Clear(new Color32(0, 0, 0, 255));
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = new Vector2Int(w / 2, h / 2);
        var white = new Color32(255, 255, 255, 255);

        var positivePaths = new Clipper2Lib.PathsD();
        var negativePaths = new Clipper2Lib.PathsD();

        for (int li = 0; li < _layers.Count; li++)
        {
            var shape = _layers[li].Shape;
            for (ushort pi = 0; pi < shape.PathCount; pi++)
            {
                ref readonly var path = ref shape.GetPath(pi);
                if (path.AnchorCount < 3) continue;

                var pathShape = new Msdf.Shape();
                Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
                pathShape = Msdf.ShapeClipper.Union(pathShape);
                var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
                if (contours.Count == 0) continue;

                if (path.IsSubtract)
                    negativePaths.AddRange(contours);
                else
                    positivePaths.AddRange(contours);
            }
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
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_mask_all.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    private byte[] RasterizeColorLayerToPng(int targetLayerIndex)
    {
        UpdateBounds();
        var cs = ConstrainedSize;
        if (cs.X <= 0 || cs.Y <= 0)
            return [];

        var w = RasterizeSize;
        var h = RasterizeSize;
        var dpi = (int)(EditorApplication.Config.PixelsPerUnit * ((float)RasterizeSize / cs.X));

        using var pixels = new PixelData<Color32>(w, h);
        pixels.Clear(new Color32(255, 255, 255, 255));
        var targetRect = new RectInt(0, 0, w, h);
        var sourceOffset = new Vector2Int(w / 2, h / 2);

        var shape = _layers[targetLayerIndex].Shape;

        // Collect subtract paths for this layer
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
        for (ushort pi = 0; pi < shape.PathCount; pi++)
        {
            ref readonly var path = ref shape.GetPath(pi);
            if (path.IsSubtract || path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, shape, pi);
            pathShape = Msdf.ShapeClipper.Union(pathShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(pathShape, 8);
            if (contours.Count == 0) continue;

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
                    // Stroke as outer ring, fill as inner
                    Rasterizer.Fill(contours, pixels, targetRect, sourceOffset, dpi, path.StrokeColor);
                    if (contractedPaths is { Count: > 0 })
                        Rasterizer.Fill(contractedPaths, pixels, targetRect, sourceOffset, dpi, fillColor);
                }
                else
                {
                    // Stroke ring only
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
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_color_L{targetLayerIndex}.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    public void GenerateAsync()
    {
        var genLayers = new List<(int Index, GenSpriteLayer Layer)>();
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].HasPrompt)
                genLayers.Add((i, _layers[i]));
        }

        if (genLayers.Count == 0)
        {
            Log.Error($"No generated layers with prompts found for '{Name}'");
            return;
        }

        if (Generation.IsGenerating)
            return;

        var globalPrompt = Style?.Prompt ?? "";
        var globalNegPrompt = Style?.NegativePrompt ?? "";
        var layerSteps = Style?.DefaultSteps ?? 30;
        var layerStrength = Style?.DefaultStrength ?? 0.8f;
        var layerStyle = Style?.StyleInpaintStrength ?? 0.5f;
        var layerGuidance = Style?.DefaultGuidanceScale ?? 6.0f;

        var shapes = new List<GenerationShape>();
        var layerPrompts = new List<string>();
        var layerNegPrompts = new List<string>();

        var workflow = Style?.Workflow ?? GenerationWorkflow.Sprite;

        // Rasterize combined mask of all paths (white on black)
        var allPathsMask = RasterizeAllPathsMaskToPng();
        var allPathsMaskUri = allPathsMask.Length > 0
            ? $"data:image/png;base64,{Convert.ToBase64String(allPathsMask)}"
            : null;

        foreach (var (layerIndex, layer) in genLayers)
        {
            var maskBytes = workflow == GenerationWorkflow.SpriteV2
                ? RasterizeColorLayerToPng(layerIndex)
                : RasterizeMaskToPng(layerIndex);

            var prompt = string.IsNullOrEmpty(globalPrompt) ? layer.Prompt : $"{layer.Prompt}, {globalPrompt}";
            var negPrompt = layer.NegativePrompt;
            if (!string.IsNullOrEmpty(globalNegPrompt))
                negPrompt = string.IsNullOrEmpty(negPrompt) ? globalNegPrompt : $"{negPrompt}, {globalNegPrompt}";

            var hashedSeed = HashSeed(layer.Seed);
            var seed = hashedSeed == 0 ? Random.Shared.NextInt64(1, long.MaxValue) : hashedSeed;

            shapes.Add(new GenerationShape
            {
                Image = maskBytes.Length > 0 ? $"data:image/png;base64,{Convert.ToBase64String(maskBytes)}" : null,
                Mask = allPathsMaskUri,
                Prompt = prompt,
                NegativePrompt = string.IsNullOrEmpty(negPrompt) ? null : negPrompt,
                Strength = layerStrength,
                Steps = layerSteps,
                Style = layerStyle,
                GuidanceScale = layerGuidance,
                Seed = seed,
            });

            if (layer.HasPrompt)
                layerPrompts.Add(layer.Prompt);
            if (!string.IsNullOrEmpty(layer.NegativePrompt))
                layerNegPrompts.Add(layer.NegativePrompt);
        }

        // Compute a stable refine seed from the sum of all shape seeds
        long refineSeed = 0;
        foreach (var s in shapes)
            refineSeed += s.Seed ?? 0;

        // Auto-build refine: concatenate layer prompts + style refine prompt
        var refinePromptParts = new List<string>(layerPrompts);
        if (!string.IsNullOrEmpty(Style?.RefinePrompt))
            refinePromptParts.Add(Style.RefinePrompt);
        var refinePrompt = string.Join(", ", refinePromptParts.Where(p => !string.IsNullOrEmpty(p)));

        var refineNegParts = new List<string>(layerNegPrompts);
        if (!string.IsNullOrEmpty(Style?.RefineNegativePrompt))
            refineNegParts.Add(Style.RefineNegativePrompt);
        var refineNegPrompt = string.Join(", ", refineNegParts.Where(p => !string.IsNullOrEmpty(p)));

        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";

        // Build style block
        GenerationStyleBlock? styleBlock = null;
        if (Style != null)
        {
            var refs = GenerationClient.LoadStyleReferences(Style.StyleReferences);
            if (refs != null)
            {
                styleBlock = new GenerationStyleBlock
                {
                    Strength = Style.StyleStrength,
                    InpaintStrength = Style.StyleInpaintStrength,
                    References = refs,
                };
            }
        }

        // Build loras from style
        List<GenerationLora>? loras = null;
        if (!string.IsNullOrEmpty(Style?.LoraName))
            loras = [new GenerationLora { Name = Style.LoraName, Strength = Style.LoraStrength }];

        var request = new GenerationRequest
        {
            Server = server,
            Workflow = (Style?.Workflow ?? GenerationWorkflow.Sprite).ToString().ToLowerInvariant(),
            Layers = shapes,
            Detail = Style?.Detail ?? 1f,
            Refine = new GenerationRefine
            {
                Prompt = refinePrompt,
                NegativePrompt = string.IsNullOrEmpty(refineNegPrompt) ? null : refineNegPrompt,
                Strength = Style?.RefineStrength ?? 0.64f,
                Steps = Style?.RefineSteps ?? 10,
                GuidanceScale = Style?.RefineGuidanceScale ?? 6.0f,
                Seed = refineSeed != 0 ? refineSeed : null,
            },
            Style = styleBlock,
            Loras = loras,
        };

        Log.Info($"Starting generation for '{Name}' ({shapes.Count} shapes) on {server}...");

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
                        var imageBytes = Convert.FromBase64String(status.Result.Image);

                        using var ms = new MemoryStream(imageBytes);
                        using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                        var cs = ConstrainedSize;
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

                        // Update layer seeds from server response
                        if (status.Result.Seed != 0)
                        {
                            foreach (var (_, layer) in genLayers)
                            {
                                if (string.IsNullOrEmpty(layer.Seed))
                                    layer.Seed = status.Result.Seed.ToString();
                            }
                        }

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

    #region Drawing

    public override void Draw()
    {
        DrawOrigin();

        var texture = Generation.Texture;
        if (texture != null)
        {
            var ppu = EditorApplication.Config.PixelsPerUnitInv;
            var cs = ConstrainedSize;
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
                Graphics.SetColor(Color.White.WithAlpha(Workspace.XrayAlpha));
                Graphics.Draw(rect);
            }
        }
        else
        {
            DrawBounds();
        }

        if (Generation.IsGenerating)
        {
            var angle = Time.TotalTime * 4f;
            var rotation = Matrix3x2.CreateRotation(angle);
            var scale = Matrix3x2.CreateScale(0.5f);

            using (Graphics.PushState())
            {
                Graphics.SetTransform(scale * rotation * Transform);
                Graphics.SetSortGroup(7);
                Graphics.SetLayer(EditorLayer.DocumentEditor);
                Graphics.SetColor(Color.White);
                Graphics.Draw(EditorAssets.Sprites.IconAi);
            }
        }
    }

    #endregion

    public override void Dispose()
    {
        Generation.Dispose();
        foreach (var layer in _layers)
            layer.Dispose();
        base.Dispose();
    }

    public override void Clone(Document source)
    {
        if (source is not GenSpriteDocument src) return;

        _layers.Clear();
        foreach (var layer in src._layers)
            _layers.Add(layer.Clone());

        ActiveLayerIndex = src.ActiveLayerIndex;
        ConstrainedSize = src.ConstrainedSize;
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;

        if (src.Generation.HasImageData)
            Generation.ImageData = (byte[])src.Generation.ImageData!.Clone();
    }
}
