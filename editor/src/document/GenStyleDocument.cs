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

    // Layer defaults
    public string PromptPrefix = "";
    public string Prompt = "";
    public string NegativePrompt = "";

    // Model
    public string? ModelName;

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
            else if (tk.ExpectIdentifier("prompt_prefix"))
                PromptPrefix = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt"))
                Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                NegativePrompt = tk.ExpectQuotedString() ?? "";
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
        if (!string.IsNullOrEmpty(PromptPrefix))
            writer.WriteLine($"prompt_prefix \"{PromptPrefix.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(Prompt))
            writer.WriteLine($"prompt \"{Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(NegativePrompt))
            writer.WriteLine($"prompt_neg \"{NegativePrompt.Replace("\"", "\\\"")}\"");
        // Model
        if (!string.IsNullOrEmpty(ModelName))
        {
            writer.WriteLine();
            writer.WriteLine($"model \"{ModelName}\"");
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
