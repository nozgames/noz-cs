//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using CrypticWizard.RandomWordGenerator;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NoZ.Editor;

public class SpriteGeneration : IDisposable
{
    private static readonly WordGenerator _wordGenerator = new();

    public string Prompt = "";
    public string NegativePrompt = "";
    public string Seed = "";
    public DocumentRef<GenerationConfig> Config;
    public GenerationJob Job { get; private set; } = new();

    public bool IsGenerating => Job.IsGenerating;
    public bool HasImageData => Job.HasImageData;

    public void RestoreJob(GenerationJob job) => Job = job;

    public void Dispose() => Job.Dispose();

    public static string GenerateRandomSeed()
    {
        var adj = _wordGenerator.GetWord(WordGenerator.PartOfSpeech.adj);
        var noun = _wordGenerator.GetWord(WordGenerator.PartOfSpeech.noun);
        return $"{adj}-{noun}";
    }
}

public partial class GeneratedSpriteDocument : SpriteDocument
{
    public SpriteGeneration Generation { get; set; } = new();
    public string? ImageFilePath { get; set; }

    private GenerationJob? _pendingGenerationJob;

    public override DocumentEditor CreateEditor() => new GeneratedSpriteEditor(this);

    public override Color32 GetPixelAt(Vector2 worldPos)
    {
        var texture = Generation.Job.Texture;
        if (texture == null)
            return default;

        Matrix3x2.Invert(Transform, out var invTransform);
        var local = Vector2.Transform(worldPos, invTransform);

        var nx = (local.X - Bounds.X) / Bounds.Width;
        var ny = (local.Y - Bounds.Y) / Bounds.Height;
        return texture.GetPixel((int)(nx * texture.Width), (int)(ny * texture.Height));
    }

    public static Document? CreateNew(Vector2? position = null)
    {
        return DocumentManager.New(AssetType.Sprite, Extension, null, WriteNewFile, position);
    }

    public static void WriteNewFile(StreamWriter writer)
    {
        writer.WriteLine("type generated");
        writer.WriteLine("generate");
        writer.WriteLine($"seed \"{SpriteGeneration.GenerateRandomSeed()}\"");
    }

    protected override void UpdateContentBounds()
    {
        var cs = ConstrainedSize ?? new Vector2Int(256, 256);
        RasterBounds = new RectInt(-cs.X / 2, -cs.Y / 2, cs.X, cs.Y);
        Bounds = RasterBounds.ToRect().Scale(1.0f / PixelsPerUnit);
    }

    public override void Reload()
    {
        if (Generation.IsGenerating)
        {
            _pendingGenerationJob = Generation.Job;
            Generation = new SpriteGeneration();
        }
        else
        {
            Generation.Dispose();
            Generation = new SpriteGeneration();
        }

        base.Reload();

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
    }

    public override void PostLoad()
    {
        base.PostLoad();
        LoadGeneratedTexture();
        Generation.Config.Resolve();
    }

    public override void Draw()
    {
        DrawOrigin();
        DrawGeneration();
    }

    public override bool DrawThumbnail()
    {
        var texture = Generation.Job.Texture;
        if (texture != null)
        {
            UI.Image(texture, ImageStyle.Center);
            return true;
        }

        return base.DrawThumbnail();
    }

    protected override void CloneContent(SpriteDocument source)
    {
        if (source is not GeneratedSpriteDocument src) return;

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

    protected override void LoadContentMetadata(PropertySet meta)
    {
        if (string.IsNullOrEmpty(Generation.Config.Name))
        {
            var style = meta.GetString("sprite", "style", "");
            if (!string.IsNullOrEmpty(style))
                Generation.Config.Name = style;
        }
    }

    protected override void SaveContentMetadata(PropertySet meta)
    {
        meta.RemoveKey("sprite", "prompt_hash");
        meta.RemoveKey("sprite", "style");
    }

    internal void LoadGeneratedTexture()
    {
        var gen = Generation;
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

    private void DrawGeneration()
    {
        var gen = Generation;
        var texture = gen.Job.Texture;
        if (texture != null)
        {
            var ppu = 1.0f / PixelsPerUnit;
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
            DrawSprite(alpha: gen.IsGenerating ? 0.3f : 1.0f);
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

    public PipelineRequest BuildGenerationRequest()
    {
        var gen = Generation;
        var config = gen.Config.Value;
        var prompt = GenerationConfig.FormatPrompt(config?.Prompt ?? "", gen.Prompt);
        var negPrompt = GenerationConfig.FormatPrompt(config?.NegativePrompt ?? "", gen.NegativePrompt);
        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";

        var references = new List<Dictionary<string, object>>();

        // Use vector paths as reference if available
        var imageBytes = RasterizeColorToPng();
        if (imageBytes.Length > 0)
        {
            references.Add(new Dictionary<string, object>
            {
                ["data"] = Convert.ToBase64String(imageBytes)
            });
        }

        var workflow = "concept";
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

    private byte[] RasterizeColorToPng()
    {
        if (Root.Children.Count == 0)
            return [];

        // Rasterize any vector paths that may exist as references
        var w = RasterBounds.Width;
        var h = RasterBounds.Height;
        if (w <= 0 || h <= 0) return [];

        var dpi = PixelsPerUnit;
        const int rasterizeSize = 1024;
        var scale = (float)rasterizeSize / MathF.Max(w, h);
        var scaledDpi = (int)MathF.Round(dpi * scale);
        var outW = (int)MathF.Round(w * scale);
        var outH = (int)MathF.Round(h * scale);

        using var pixels = new PixelData<Color32>(outW, outH);
        var targetRect = new RectInt(0, 0, outW, outH);
        var sourceOffset = new Vector2Int(
            (int)MathF.Round(-RasterBounds.X * scale),
            (int)MathF.Round(-RasterBounds.Y * scale));

        VectorSpriteDocument.RasterizeLayer(Root, pixels, targetRect, sourceOffset, scaledDpi);

        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(pixels.AsReadonlySpan(), outW, outH);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public void ApplyGenerationResult(GenerationStatus status, bool createTexture = true)
    {
        var gen = Generation;
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
                EditorApplication.Store.CreateDirectory(tmpDir);
                EditorApplication.Store.WriteAllBytes(System.IO.Path.Combine(tmpDir, $"{Name}_gen.png"), gen.Job.ImageData);
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
        var gen = Generation;

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
                    Save();
                    SaveMetadata();
                    DocumentManager.QueueExport(this, force: true);
                    if (Atlas != null)
                        AtlasManager.UpdateSource(this);
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

    public override void Dispose()
    {
        _pendingGenerationJob?.Dispose();
        _pendingGenerationJob = null;
        Generation.Dispose();
        base.Dispose();
    }
}
