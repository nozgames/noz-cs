//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

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

    public string Prompt = "";
    public string NegativePrompt = "";
    public string? ModelName;
    public string? StyleKey;
    public bool RemoveBackground = true;

    /// <summary>
    /// Merges a format-string template with a per-sprite input.
    /// If template contains {0}, replaces it. Otherwise appends input before template.
    /// </summary>
    public static string FormatPrompt(string template, string input)
    {
        if (string.IsNullOrEmpty(template))
            return input;
        if (string.IsNullOrEmpty(input))
            return template.Replace("{0}", "");
        if (template.Contains("{0}"))
            return template.Replace("{0}", input);
        return $"{input}, {template}";
    }

    public GenStyleDocument()
    {
        ShouldExport = false;
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetTypeGenStyle,
            Name = "GenStyle",
            Extensions = [".genstyle"],
            Factory = () => new GenStyleDocument(),
            EditorFactory = doc => new GenStyleEditor((GenStyleDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconGenstyle
        });
    }

    private static void NewFile(StreamWriter writer)
    {
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
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("model"))
                ModelName = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("style") || tk.ExpectIdentifier("lora"))
                StyleKey = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("prompt"))
                Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("remove_bg"))
                tk.ExpectBool(out RemoveBackground);
            else if (tk.ExpectIdentifier("prompt_prefix"))
            {
                // Legacy support: convert prompt_prefix to format string
                var prefix = tk.ExpectQuotedString() ?? "";
                if (!string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(Prompt))
                    Prompt = $"{prefix}{{0}}";
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"GenStyleDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }
    }

    public override void Save(StreamWriter writer)
    {
        if (!string.IsNullOrEmpty(Prompt))
            writer.WriteLine($"prompt \"{Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(NegativePrompt))
            writer.WriteLine($"prompt_neg \"{NegativePrompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(ModelName))
        {
            writer.WriteLine();
            writer.WriteLine($"model \"{ModelName}\"");
        }
        if (!string.IsNullOrEmpty(StyleKey))
            writer.WriteLine($"style \"{StyleKey}\"");
        writer.WriteLine();
        writer.WriteLine($"remove_bg {(RemoveBackground ? "true" : "false")}");
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
