//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Threading;

namespace NoZ.Editor;

public class GenerationImage : IDisposable
{
    public bool IsGenerating;
    public GenerationState GenerationState;
    public int QueuePosition;
    public float GenerationProgress;
    public string? GenerationError;
    public CancellationTokenSource? CancellationSource;

    public byte[]? ImageData;
    public Texture? Texture;

    public bool HasImageData => ImageData is { Length: > 0 };

    public void CancelGeneration()
    {
        CancellationSource?.Cancel();
        CancellationSource = null;
        IsGenerating = false;
        GenerationState = default;
        GenerationProgress = 0f;
        GenerationError = null;
    }

    public void Dispose()
    {
        Texture?.Dispose();
        Texture = null;
    }
}

public class GenStyleDocument : Document
{
    public static readonly AssetType AssetTypeGenStyle = AssetType.FromString("GNST");

    public override bool CanSave => true;

    // Layer defaults
    public string Prompt = "";
    public string NegativePrompt = "";
    public int DefaultSteps = 30;
    public float DefaultStrength = 0.8f;
    public float DefaultGuidanceScale = 6.0f;
    public float StyleInpaintStrength = 0.2f;

    // Refine defaults
    public string RefinePrompt = "";
    public string RefineNegativePrompt = "";
    public int RefineSteps = 30;
    public float RefineStrength = 0.64f;
    public float RefineGuidanceScale = 6.0f;
    public float StyleStrength = 0.5f;

    // Workflow
    public GenerationWorkflow Workflow = GenerationWorkflow.Sprite;

    // LoRA
    public string? LoraName;
    public float LoraStrength = 0.8f;
    public float Detail = 1.0f;

    // Style references
    public List<string> StyleReferences = new();

    public GenStyleDocument()
    {
        IsEditorOnly = true;
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetTypeGenStyle,
            Name = "GenStyle",
            Extension = ".genstyle",
            Factory = () => new GenStyleDocument(),
            EditorFactory = doc => new GenStyleEditor((GenStyleDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconGenstyle
        });
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("layer");
        writer.WriteLine("prompt \"\"");
    }

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Parse(ref tk);
        Loaded = true;
    }

    private void Parse(ref Tokenizer tk)
    {
        StyleReferences.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
                ParseLayer(ref tk);
            else if (tk.ExpectIdentifier("refine"))
                ParseRefine(ref tk);
            else if (tk.ExpectIdentifier("workflow"))
            {
                var wf = tk.ExpectQuotedString() ?? "sprite";
                Workflow = Enum.TryParse<GenerationWorkflow>(wf, ignoreCase: true, out var parsed)
                    ? parsed : GenerationWorkflow.Sprite;
            }
            else if (tk.ExpectIdentifier("detail"))
                Detail = tk.ExpectFloat(1.0f);
            else if (tk.ExpectIdentifier("lora"))
                LoraName = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("lora_strength"))
                LoraStrength = tk.ExpectFloat(0.8f);
            else if (tk.ExpectIdentifier("style_ref"))
            {
                var name = tk.ExpectQuotedString() ?? "";
                // Backward compat: consume old per-ref strength if present
                tk.ExpectFloat(out _);
                if (!string.IsNullOrEmpty(name))
                    StyleReferences.Add(name);
            }
            // Backward compat: old flat format tokens
            else if (tk.ExpectIdentifier("prompt"))
                Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("steps"))
                DefaultSteps = tk.ExpectInt(30);
            else if (tk.ExpectIdentifier("strength"))
                DefaultStrength = tk.ExpectFloat(0.8f);
            else if (tk.ExpectIdentifier("guidance") || tk.ExpectIdentifier("guidance_scale"))
                DefaultGuidanceScale = tk.ExpectFloat(6.0f);
            else if (tk.ExpectIdentifier("refine_prompt"))
                RefinePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("refine_prompt_neg"))
                RefineNegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("refine_steps"))
                RefineSteps = tk.ExpectInt(30);
            else if (tk.ExpectIdentifier("refine_strength"))
                RefineStrength = tk.ExpectFloat(0.64f);
            else if (tk.ExpectIdentifier("refine_guidance") || tk.ExpectIdentifier("refine_guidance_scale"))
                RefineGuidanceScale = tk.ExpectFloat(6.0f);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"GenStyleDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }
    }

    private void ParseLayer(ref Tokenizer tk)
    {
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("steps"))
                DefaultSteps = tk.ExpectInt(30);
            else if (tk.ExpectIdentifier("strength"))
                DefaultStrength = tk.ExpectFloat(0.8f);
            else if (tk.ExpectIdentifier("guidance") || tk.ExpectIdentifier("guidance_scale"))
                DefaultGuidanceScale = tk.ExpectFloat(6.0f);
            else if (tk.ExpectIdentifier("style") || tk.ExpectIdentifier("style_strength"))
                StyleInpaintStrength = tk.ExpectFloat(0.2f);
            else
                break;
        }
    }

    private void ParseRefine(ref Tokenizer tk)
    {
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                RefinePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                RefineNegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("steps"))
                RefineSteps = tk.ExpectInt(30);
            else if (tk.ExpectIdentifier("strength"))
                RefineStrength = tk.ExpectFloat(0.64f);
            else if (tk.ExpectIdentifier("guidance") || tk.ExpectIdentifier("guidance_scale"))
                RefineGuidanceScale = tk.ExpectFloat(6.0f);
            else if (tk.ExpectIdentifier("style") || tk.ExpectIdentifier("style_strength"))
                StyleStrength = tk.ExpectFloat(0.5f);
            else
                break;
        }
    }

    public override void Save(StreamWriter writer)
    {
        // Workflow
        writer.WriteLine($"workflow \"{Workflow.ToString().ToLowerInvariant()}\"");
        writer.WriteLine();

        // Layer section
        writer.WriteLine("layer");
        if (!string.IsNullOrEmpty(Prompt))
            writer.WriteLine($"prompt \"{Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(NegativePrompt))
            writer.WriteLine($"prompt_neg \"{NegativePrompt.Replace("\"", "\\\"")}\"");
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "steps {0}", DefaultSteps));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "strength {0}", DefaultStrength));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "guidance {0}", DefaultGuidanceScale));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "style {0}", StyleInpaintStrength));

        // Refine section
        writer.WriteLine();
        writer.WriteLine("refine");
        if (!string.IsNullOrEmpty(RefinePrompt))
            writer.WriteLine($"prompt \"{RefinePrompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(RefineNegativePrompt))
            writer.WriteLine($"prompt_neg \"{RefineNegativePrompt.Replace("\"", "\\\"")}\"");
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "steps {0}", RefineSteps));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "strength {0}", RefineStrength));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "guidance {0}", RefineGuidanceScale));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "style {0}", StyleStrength));

        // LoRA
        if (!string.IsNullOrEmpty(LoraName))
        {
            writer.WriteLine();
            writer.WriteLine($"lora \"{LoraName}\"");
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "lora_strength {0}", LoraStrength));
        }

        if (Detail < 1.0f)
            writer.WriteLine($"detail {Detail}");

        // Style references
        if (StyleReferences.Count > 0)
        {
            writer.WriteLine();
            foreach (var name in StyleReferences)
                writer.WriteLine($"style_ref \"{name}\"");
        }
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconGenstyle);
        }
    }
}
