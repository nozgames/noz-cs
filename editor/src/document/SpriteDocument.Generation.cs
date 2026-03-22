//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public class SpriteGeneration : IDisposable
{
    public string Prompt = "";
    public string NegativePrompt = "";
    public string Seed = "";
    public DocumentRef<GenerationConfig> Config;
    public List<DocumentRef<SpriteDocument>> References = [];

    public GenerationJob Job { get; private set; } = new();

    public bool IsGenerating => Job.IsGenerating;
    public bool HasImageData => Job.HasImageData;

    public void RestoreJob(GenerationJob job) => Job = job;

    public void Dispose() => Job.Dispose();
}

public partial class SpriteDocument
{
    public SpriteGeneration? Generation { get; set; }

    private const int RasterizeSize = 1024;

    private GenerationJob? _pendingGenerationJob;

    private void ReloadGeneration()
    {
        // Preserve the active generation job across reloads so that
        // file-watcher triggered reloads (e.g. after SaveAll on edit exit)
        // don't destroy in-flight generation state.
        if (Generation is { IsGenerating: true })
        {
            _pendingGenerationJob = Generation.Job;
            Generation = null;
        }
        else
        {
            Generation?.Dispose();
            Generation = null;
        }
    }

    private void PostLoadGeneration()
    {
        if (Generation == null)
            return;

        LoadGeneratedTexture();
        Generation.Config.Resolve();

        foreach (ref var r in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Generation.References))
            r.Resolve();
    }

    private void ResolveGeneration()
    {
        if (Generation == null)
        {
            _pendingGenerationJob = null;
            return;
        }

        // Restore an in-flight generation job that was preserved across reload
        if (_pendingGenerationJob != null)
        {
            Generation.Job.Dispose();
            Generation.RestoreJob(_pendingGenerationJob);
            _pendingGenerationJob = null;
        }
        else
        {
            LoadGeneratedTexture();
        }

        Generation.Config.Resolve();

        foreach (ref var r in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Generation.References))
            r.Resolve();
    }

    private void SaveGeneration(StreamWriter writer)
    {
        if (Generation == null)
            return;

        var gen = Generation;
        writer.WriteLine("generate");
        if (!string.IsNullOrEmpty(gen.Prompt))
            writer.WriteLine($"prompt \"{gen.Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(gen.NegativePrompt))
            writer.WriteLine($"prompt_neg \"{gen.NegativePrompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(gen.Seed))
            writer.WriteLine($"seed \"{gen.Seed}\"");
        if (!string.IsNullOrEmpty(gen.Config.Name))
            writer.WriteLine($"style \"{gen.Config.Name}\"");
        foreach (var r in gen.References)
            if (r.HasValue) writer.WriteLine($"reference \"{r.Name}\"");
        if (gen.Job.HasImageData && ImageFilePath != null)
            File.WriteAllBytes(ImageFilePath, gen.Job.ImageData!);
    }

    private void CloneGeneration(SpriteDocument src)
    {
        if (src.Generation == null)
            return;

        Generation = new SpriteGeneration
        {
            Prompt = src.Generation.Prompt,
            NegativePrompt = src.Generation.NegativePrompt,
            Seed = src.Generation.Seed,
            Config = src.Generation.Config,
        };
        ConstrainedSize = src.ConstrainedSize;
        if (src.Generation.Job.HasImageData)
            Generation.Job.ImageData = (byte[])src.Generation.Job.ImageData!.Clone();
    }

    private void DisposeGeneration()
    {
        _pendingGenerationJob?.Dispose();
        _pendingGenerationJob = null;
        Generation?.Dispose();
        Generation = null;
    }

    private void ParseGeneration(ref Tokenizer tk)
    {
        Generation = new SpriteGeneration();
        var gen = Generation;
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                gen.Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                gen.NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("seed"))
            {
                if (tk.ExpectQuotedString(out var seedStr))
                    gen.Seed = seedStr;
                else
                    gen.Seed = tk.ExpectInt().ToString();
            }
            else if (tk.ExpectIdentifier("style"))
                gen.Config.Name = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("prompt_hash"))
                tk.ExpectQuotedString(); // Legacy: skip
            else if (tk.ExpectIdentifier("reference"))
                gen.References.Add(new DocumentRef<SpriteDocument> { Name = tk.ExpectQuotedString() });
            else if (tk.ExpectIdentifier("image"))
            {
                // Legacy migration: extract embedded base64 to companion file
                var base64 = tk.ExpectQuotedString();
                if (!string.IsNullOrEmpty(base64))
                {
                    gen.Job.ImageData = Convert.FromBase64String(base64);

                    if (ImageFilePath == null)
                    {
                        var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
                        var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
                        ImageFilePath = System.IO.Path.Combine(dir, stem + ".png");
                    }

                    if (!File.Exists(ImageFilePath))
                        File.WriteAllBytes(ImageFilePath, gen.Job.ImageData);
                }
            }
            else
                break;
        }

        if (!gen.Job.HasImageData && ImageFilePath != null && File.Exists(ImageFilePath))
            gen.Job.ImageData = File.ReadAllBytes(ImageFilePath);
    }

    private void DrawGeneration()
    {
        var gen = Generation!;
        var texture = gen.Job.Texture;
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
                var alpha = gen.IsGenerating ? 0.3f : Workspace.XrayAlpha;
                Graphics.SetColor(Color.White.WithAlpha(alpha));
                Graphics.Draw(rect);
            }
        }
        else if (!DrawFromAtlas() && !DrawFromPreviewTexture())
        {
            DrawBounds();
        }

        if (gen.IsGenerating)
        {
            var angle = Time.TotalTime * 3f;
            var rotation = Matrix3x2.CreateRotation(angle);
            var pulse = 0.7f + 0.3f * (0.5f + 0.5f * MathF.Sin(Time.TotalTime * 3f));
            var scale = Matrix3x2.CreateScale(pulse);

            using (Graphics.PushState())
            {
                Graphics.SetTransform(scale * rotation * Transform);
                Graphics.SetSortGroup(7);
                if (IsEditing)
                    Graphics.SetLayer(EditorLayer.DocumentEditor);
                Graphics.SetColor(Color.White);
                Graphics.Draw(EditorAssets.Sprites.IconGenerating);
            }
        }
    }

    internal void LoadGeneratedTexture()
    {
        var gen = Generation!;
        gen.Job.Dispose();

        if (!gen.Job.HasImageData) return;

        try
        {
            using var ms = new MemoryStream(gen.Job.ImageData!);
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

            var cs = ConstrainedSize ?? new Vector2Int(256, 256);
            if (srcImage.Width != cs.X || srcImage.Height != cs.Y)
                srcImage.Mutate(x => x.Resize(cs.X, cs.Y));

            var w = srcImage.Width;
            var h = srcImage.Height;
            var pixels = new byte[w * h * 4];
            srcImage.CopyPixelDataTo(pixels);
            gen.Job.Texture = Texture.Create(w, h, pixels, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create generated texture for '{Name}': {ex.Message}");
        }
    }

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

    public PipelineRequest BuildGenerationRequest()
    {
        var gen = Generation!;
        var config = gen.Config.Value;
        var prompt = GenerationConfig.FormatPrompt(config?.Prompt ?? "", gen.Prompt);
        var negPrompt = GenerationConfig.FormatPrompt(config?.NegativePrompt ?? "", gen.NegativePrompt);
        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";

        var inputs = new Dictionary<string, string>();
        var references = new List<Dictionary<string, object>>();

        var imageBytes = RasterizeColorToPng();
        if (imageBytes.Length > 0)
        {
            inputs["composition"] = Convert.ToBase64String(imageBytes);
            references.Add(new Dictionary<string, object>
            {
                ["image"] = "composition",
                ["type"] = "composition"
            });
        }

        for (int i = 0; i < gen.References.Count; i++)
        {
            var refDoc = gen.References[i].Value;
            if (refDoc == null) continue;
            byte[] refBytes = refDoc.Generation is { HasImageData: true }
                ? refDoc.Generation.Job.ImageData!
                : refDoc.RasterizeColorToPng();

            if (refBytes.Length > 0)
            {
                var name = $"ref_{i}";
                inputs[name] = Convert.ToBase64String(refBytes);
                references.Add(new Dictionary<string, object>
                {
                    ["image"] = name,
                    ["type"] = "composition"
                });
            }
        }

        var genArgs = new Dictionary<string, object> { ["prompt"] = prompt, ["workflow"] = "sprite" };
        if (!string.IsNullOrEmpty(negPrompt))
            genArgs["negative"] = negPrompt;
        if (!string.IsNullOrEmpty(config?.ModelName))
            genArgs["model"] = config!.ModelName;
        if (!string.IsNullOrEmpty(config?.StyleKey))
            genArgs["style"] = config!.StyleKey;
        if (!string.IsNullOrEmpty(gen.Seed))
            genArgs["seed"] = gen.Seed;
        if (references.Count > 0)
            genArgs["references"] = references;

        var steps = new List<PipelineStep>
        {
            new() { Type = "generate", Output = "sprite", Args = genArgs },
        };

        if (config?.RemoveBackground == true)
            steps.Add(new() { Type = "rmbg", Output = "clean", Args = new Dictionary<string, object> { ["image"] = "sprite" } });

        if (config?.OutlineColor.A > 0)
        {
            var lastOutput = steps[^1].Output!;
            Span<char> hex = stackalloc char[6];
            Strings.ColorHex(config.OutlineColor, hex);
            steps.Add(new()
            {
                Type = "outline",
                Output = "outlined",
                Args = new Dictionary<string, object>
                {
                    ["image"] = lastOutput,
                    ["thickness"] = config.OutlineThickness,
                    ["color"] = $"#{hex}"
                }
            });
        }

        return new PipelineRequest
        {
            Server = server,
            Inputs = inputs.Count > 0 ? inputs : null,
            Steps = steps,
        };
    }

    public void ApplyGenerationResult(GenerationStatus status, bool createTexture = true)
    {
        var gen = Generation!;
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
            gen.Job.ImageData = outMs.ToArray();

            if (createTexture)
            {
                gen.Job.Dispose();
                var rw = srcImage.Width;
                var rh = srcImage.Height;
                var px = new byte[rw * rh * 4];
                srcImage.CopyPixelDataTo(px);
                gen.Job.Texture = Texture.Create(rw, rh, px, TextureFormat.RGBA8, TextureFilter.Linear, $"{Name}_gen");
            }

            try
            {
                var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
                Directory.CreateDirectory(tmpDir);
                File.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_gen.png"), gen.Job.ImageData);
            }
            catch { }

            if (!string.IsNullOrEmpty(status.Result.Seed) && string.IsNullOrEmpty(gen.Seed))
                gen.Seed = status.Result.Seed;

            if (ImageFilePath == null)
            {
                var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
                var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
                ImageFilePath = System.IO.Path.Combine(dir, stem + ".png");
            }

            Log.Info($"[Gen] '{Name}' ApplyResult: imagePath='{ImageFilePath}' hasImage={gen.HasImageData}");
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
        var gen = Generation!;

        if (string.IsNullOrEmpty(gen.Prompt))
        {
            Log.Error($"No prompt set for '{Name}'");
            return;
        }

        if (string.IsNullOrEmpty(gen.Config.Value?.ModelName))
        {
            Log.Error($"Invalid style for '{Name}': no model specified");
            return;
        }

        if (gen.IsGenerating)
            return;

        var request = BuildGenerationRequest();

        Log.Info($"Starting generation for '{Name}' on {request.Server}...");

        var job = gen.Job;
        job.IsGenerating = true;
        job.GenerationError = null;

        var cts = new System.Threading.CancellationTokenSource();
        job.CancellationSource = cts;

        GenerationClient.Generate(request, status =>
        {
            if (job.CancellationSource == null)
                return;

            switch (status.State)
            {
                case GenerationState.Queued:
                    if (job.GenerationState != GenerationState.Queued || job.QueuePosition != status.QueuePosition)
                        Log.Info($"Generation queued for '{Name}' (position {status.QueuePosition})");
                    job.GenerationState = GenerationState.Queued;
                    job.QueuePosition = status.QueuePosition;
                    job.GenerationProgress = 0f;
                    break;

                case GenerationState.Running:
                    job.GenerationState = GenerationState.Running;
                    job.GenerationProgress = status.Progress;
                    break;

                case GenerationState.Completed:
                    job.IsGenerating = false;
                    job.CancellationSource = null;
                    job.GenerationState = GenerationState.Completed;
                    job.GenerationProgress = 1f;
                    ApplyGenerationResult(status);
                    Log.Info($"[Gen] '{Name}' saving after generation...");
                    Save();
                    SaveMetadata();
                    DocumentManager.QueueExport(this, force: true);
                    if (Atlas != null)
                        AtlasManager.UpdateSource(this);
                    Log.Info($"[Gen] '{Name}' saved and queued for export");
                    break;

                case GenerationState.Failed:
                    job.IsGenerating = false;
                    job.CancellationSource = null;
                    job.GenerationState = GenerationState.Failed;
                    job.GenerationError = status.Error;
                    Log.Error($"Generation failed for '{Name}': {status.Error}");
                    break;
            }
        }, cts.Token);
    }
}
