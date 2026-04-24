//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Threading;

namespace NoZ.Editor;

public class GenerationJob : IDisposable
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

public class GenerationConfig : Document
{
    public const string Extension = ".gen";

    public static readonly AssetType AssetTypeGen = AssetType.FromString("GNST");

    public override bool CanSave => true;

    public string Prompt = "";
    public string NegativePrompt = "";
    public string? ModelName;
    public string? StyleKey;
    public bool RemoveBackground = true;
    public Color32 OutlineColor = Color32.Transparent;
    public int OutlineThickness = 3;

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

    public GenerationConfig()
    {
        ShouldExport = false;
    }

    public override void LoadMetadata(PropertySet meta)
    {
        ShouldExport = false;
    }

    public static void RegisterDef()
    {
        DocumentDef<GenerationConfig>.Register(new DocumentDef
        {
            Type = AssetTypeGen,
            Name = "Generation",
            Extensions = [Extension, ".genstyle"],
            Factory = _ => new GenerationConfig(),
            EditorFactory = doc => new GenerationConfigEditor((GenerationConfig)doc),
            Icon = () => EditorAssets.Sprites.AssetIconGenstyle
        });
    }

    public static Document? CreateNew(string? name = null, System.Numerics.Vector2? position = null)
    {
        return Project.New(AssetTypeGen, Extension, name, writer =>
        {
            writer.WriteLine("prompt \"\"");
        }, position);
    }

    public override void Load()
    {
        var tk = new Tokenizer(File.ReadAllText(Path));
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
            else if (tk.ExpectIdentifier("outline_color"))
            {
                if (tk.ExpectColor(out var color))
                    OutlineColor = color.ToColor32();
            }
            else if (tk.ExpectIdentifier("outline_size"))
                OutlineThickness = tk.ExpectInt(3);
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
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}'");
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

        if (OutlineColor.A > 0)
        {
            Span<char> hex = stackalloc char[6];
            Strings.ColorHex(OutlineColor, hex);
            writer.WriteLine($"outline_color #{hex}");
            writer.WriteLine($"outline_size {OutlineThickness}");
        }
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconGenstyle);
        }
    }
}
