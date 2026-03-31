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
        else if (Sprite != null)
            DrawSprite();
        else
            DrawBounds();

        if (gen.IsGenerating)
        {
            var angle = Time.TotalTime * 3f;
            var rotation = Matrix3x2.CreateRotation(angle);
            var pulse = 0.7f + 0.3f * (0.5f + 0.5f * MathF.Sin(Time.TotalTime * 3f));
            var scale = Matrix3x2.CreateScale(pulse);

            using (Graphics.PushState())
            {
                Graphics.SetShader(EditorAssets.Shaders.Sprite);
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

            gen.Job.Texture = CreateTextureFromImage(srcImage, $"{Name}_gen");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create generated texture for '{Name}': {ex.Message}");
        }
    }

    private byte[] RasterizeColorToPng()
    {
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        if (w <= 0 || h <= 0) return [];

        // Scale DPI so paths render at RasterizeSize regardless of actual constraint size
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var scale = (float)RasterizeSize / MathF.Max(w, h);
        var scaledDpi = (int)MathF.Round(dpi * scale);
        var outW = (int)MathF.Round(w * scale);
        var outH = (int)MathF.Round(h * scale);

        using var pixels = new PixelData<Color32>(outW, outH);

        var targetRect = new RectInt(0, 0, outW, outH);
        var sourceOffset = new Vector2Int(
            (int)MathF.Round(-RasterBounds.X * scale),
            (int)MathF.Round(-RasterBounds.Y * scale));

        Rect? clipRect = null;
        if (ConstrainedSize.HasValue)
        {
            float invDpi = 1f / dpi;
            clipRect = new Rect(
                RasterBounds.X * invDpi,
                RasterBounds.Y * invDpi,
                RasterBounds.Width * invDpi,
                RasterBounds.Height * invDpi);
        }

        RasterizeLayer(RootLayer, pixels, targetRect, sourceOffset, scaledDpi, clipRect);

        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(pixels.AsByteSpan(), outW, outH);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public PipelineRequest BuildGenerationRequest()
    {
        var gen = Generation!;
        var config = gen.Config.Value;
        var prompt = GenerationConfig.FormatPrompt(config?.Prompt ?? "", gen.Prompt);
        var negPrompt = GenerationConfig.FormatPrompt(config?.NegativePrompt ?? "", gen.NegativePrompt);
        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";

        var references = new List<Dictionary<string, object>>();

        var imageBytes = RasterizeColorToPng();
        if (imageBytes.Length > 0)
        {
            references.Add(new Dictionary<string, object>
            {
                ["data"] = Convert.ToBase64String(imageBytes)
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
                references.Add(new Dictionary<string, object>
                {
                    ["data"] = Convert.ToBase64String(refBytes)
                });
            }
        }

        var workflow = references.Count > 0 ? "sprite" : "txt2img";
        var genArgs = new Dictionary<string, object> { ["prompt"] = prompt, ["workflow"] = workflow };
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
                gen.Job.Texture = CreateTextureFromImage(srcImage, $"{Name}_gen");
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
