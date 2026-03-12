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

    public readonly Shape Shape = new();
    public string Prompt = "";
    public string NegativePrompt = "";
    public string Seed = "";

    public Vector2Int ConstrainedSize { get; set; } = new(256, 256);
    public GenerationImage Generation { get; } = new();
    public string? StyleName;
    public GenStyleDocument? Style;
    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);

    public bool IsActiveLayerLocked => false;

    public bool HasGeneration => !string.IsNullOrEmpty(Prompt);
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
    }

    private static void NewFile(StreamWriter writer)
    {
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
        Shape.Clear();
        Prompt = "";
        NegativePrompt = "";
        Seed = "";

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
            {
                // Backward compat: skip layer name, parse contents into our single shape
                tk.ExpectQuotedString();
                if (tk.ExpectIdentifier("generate"))
                    ParseGenerationConfig(ref tk);
                while (!tk.IsEOF && tk.ExpectIdentifier("path"))
                    ParsePath(Shape, ref tk);
            }
            else if (tk.ExpectIdentifier("generate"))
            {
                ParseGenerationConfig(ref tk);
            }
            else if (tk.ExpectIdentifier("path"))
            {
                ParsePath(Shape, ref tk);
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
            else if (tk.ExpectIdentifier("refine"))
                ConsumeGenerationConfig(ref tk);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"GenSpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }
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

    private void ParseGenerationConfig(ref Tokenizer tk)
    {
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
        // Generation config
        writer.WriteLine("generate");
        if (!string.IsNullOrEmpty(Prompt))
            writer.WriteLine($"prompt \"{Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(NegativePrompt))
            writer.WriteLine($"prompt_neg \"{NegativePrompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(Seed))
            writer.WriteLine($"seed \"{Seed}\"");

        // Mask paths
        SaveMaskPaths(Shape, writer);

        // Document-level generation image
        if (Generation.HasImageData)
        {
            writer.WriteLine();
            writer.WriteLine($"gen_image \"{Convert.ToBase64String(Generation.ImageData!)}\"");
        }
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

        Shape.UpdateSamples();
        Shape.UpdateBounds();
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

    private byte[] RasterizeMaskToPng()
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

        for (ushort pi = 0; pi < Shape.PathCount; pi++)
        {
            ref readonly var path = ref Shape.GetPath(pi);
            if (path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, Shape, pi);
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
            File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_mask.png"), pngBytes);
        }
        catch { }

        return pngBytes;
    }

    private byte[] RasterizeColorToPng()
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

        // Collect subtract paths
        var negativePaths = new Clipper2Lib.PathsD();
        for (ushort pi = 0; pi < Shape.PathCount; pi++)
        {
            ref readonly var path = ref Shape.GetPath(pi);
            if (!path.IsSubtract || path.AnchorCount < 3) continue;

            var subShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(subShape, Shape, pi);
            subShape = Msdf.ShapeClipper.Union(subShape);
            var contours = Msdf.ShapeClipper.ShapeToPaths(subShape, 8);
            if (contours.Count > 0)
                negativePaths.AddRange(contours);
        }

        // Render each normal/clip path with its fill and stroke colors
        for (ushort pi = 0; pi < Shape.PathCount; pi++)
        {
            ref readonly var path = ref Shape.GetPath(pi);
            if (path.IsSubtract || path.AnchorCount < 3) continue;

            var pathShape = new Msdf.Shape();
            Msdf.ShapeClipper.AppendContour(pathShape, Shape, pi);
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

        var globalPrompt = Style?.Prompt ?? "";
        var globalNegPrompt = Style?.NegativePrompt ?? "";
        var steps = Style?.DefaultSteps ?? 30;
        var strength = Style?.DefaultStrength ?? 0.8f;
        var styleStrength = Style?.StyleInpaintStrength ?? 0.5f;
        var guidance = Style?.DefaultGuidanceScale ?? 6.0f;

        var workflow = Style?.Workflow ?? GenerationWorkflow.Sprite;

        // Rasterize image (color or mask depending on workflow)
        var imageBytes = RasterizeColorToPng();
        var maskBytes = RasterizeMaskToPng();

        var prompt = string.IsNullOrEmpty(globalPrompt) ? Prompt : $"{Prompt}, {globalPrompt}";
        var negPrompt = NegativePrompt;
        if (!string.IsNullOrEmpty(globalNegPrompt))
            negPrompt = string.IsNullOrEmpty(negPrompt) ? globalNegPrompt : $"{negPrompt}, {globalNegPrompt}";

        var hashedSeed = HashSeed(Seed);
        var seed = hashedSeed == 0 ? Random.Shared.NextInt64(1, long.MaxValue) : hashedSeed;

        // Build refine prompt
        var refinePromptParts = new List<string> { Prompt };
        if (!string.IsNullOrEmpty(Style?.RefinePrompt))
            refinePromptParts.Add(Style.RefinePrompt);
        var refinePrompt = string.Join(", ", refinePromptParts.Where(p => !string.IsNullOrEmpty(p)));

        var refineNegParts = new List<string>();
        if (!string.IsNullOrEmpty(NegativePrompt))
            refineNegParts.Add(NegativePrompt);
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

        // Build lora from style
        GenerationLora? lora = null;
        if (!string.IsNullOrEmpty(Style?.LoraName))
            lora = new GenerationLora { Name = Style.LoraName, Strength = Style.LoraStrength };

        var request = new GenerationRequest
        {
            Server = server,
            Workflow = (Style?.Workflow ?? GenerationWorkflow.Sprite).ToString().ToLowerInvariant(),
            Image = imageBytes.Length > 0 ? $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}" : null,
            Mask = maskBytes.Length > 0 ? $"data:image/png;base64,{Convert.ToBase64String(maskBytes)}" : null,
            Prompt = prompt,
            NegativePrompt = string.IsNullOrEmpty(negPrompt) ? null : negPrompt,
            Strength = strength,
            Steps = steps,
            GuidanceScale = guidance,
            Seed = seed,
            StyleStrength = styleStrength,
            Refine = new GenerationRefine
            {
                Prompt = refinePrompt,
                NegativePrompt = string.IsNullOrEmpty(refineNegPrompt) ? null : refineNegPrompt,
                Strength = Style?.RefineStrength ?? 0.64f,
                Steps = Style?.RefineSteps ?? 10,
                GuidanceScale = Style?.RefineGuidanceScale ?? 6.0f,
                Seed = seed,
            },
            Style = styleBlock,
            Lora = lora,
        };

        Log.Info($"Starting generation for '{Name}' on {server}...");

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
                        var imageResultBytes = Convert.FromBase64String(status.Result.Image);

                        using var ms = new MemoryStream(imageResultBytes);
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
        Shape.Dispose();
        base.Dispose();
    }

    public override void Clone(Document source)
    {
        if (source is not GenSpriteDocument src) return;

        Shape.CopyFrom(src.Shape);
        Prompt = src.Prompt;
        NegativePrompt = src.NegativePrompt;
        Seed = src.Seed;
        ConstrainedSize = src.ConstrainedSize;
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;

        if (src.Generation.HasImageData)
            Generation.ImageData = (byte[])src.Generation.ImageData!.Clone();
    }
}
