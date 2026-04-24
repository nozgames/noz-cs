//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class GeneratedSpriteEditor : SpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId GenerateButton { get; }
        public static partial WidgetId CancelButton { get; }
        public static partial WidgetId StyleDropDown { get; }
        public static partial WidgetId GenerationPrompt { get; }
        public static partial WidgetId GenerationNegativePrompt { get; }
        public static partial WidgetId Seed { get; }
        public static partial WidgetId RandomizeSeed { get; }
    }

    public override bool ShowInspector => true;

    public new GeneratedSpriteDocument Document => (GeneratedSpriteDocument)base.Document;

    public GeneratedSpriteEditor(GeneratedSpriteDocument doc) : base(doc)
    {

        Commands =
        [
            new Command("Exit Edit Mode",         Workspace.EndEdit,          [InputCode.KeyTab]),
            new Command("Generate",               Document.GenerateAsync,     [new KeyBinding(InputCode.KeyG, ctrl:true)]),
            new Command("Generate (Random Seed)",  GenerateWithRandomSeed,    [new KeyBinding(InputCode.KeyG, ctrl:true, shift:true)]),
        ];
    }

    protected override bool IsNodeSelected(SpriteNode node) => false;
    protected override void OnNodeClicked(SpriteNode node) { }
    protected override void OnOutlinerChanged() { }

    public override void Update()
    {
        Document.DrawBounds(true);

        var gen = Document.Generation;
        var texture = gen?.Job.Texture;
        if (texture != null)
        {
            var cs = Document.ConstrainedSize ?? new Vector2Int(256, 256);
            var ppu = 1.0f / Document.PixelsPerUnit;
            var rect = new Rect(
                cs.X * ppu * -0.5f,
                cs.Y * ppu * -0.5f,
                cs.X * ppu,
                cs.Y * ppu);

            using (Graphics.PushState())
            {
                Graphics.SetTransform(Document.Transform);
                Graphics.SetTexture(texture);
                Graphics.SetShader(EditorAssets.Shaders.Texture);
                var alpha = gen!.IsGenerating ? 0.3f : 1.0f;
                Graphics.SetColor(Color.White.WithAlpha(alpha));
                Graphics.Draw(rect);
            }
        }
        else if (Document.Sprite != null)
        {
            Document.DrawSprite();
        }

        if (gen is { IsGenerating: true })
        {
            var angle = Time.TotalTime * 3f;
            var rotation = Matrix3x2.CreateRotation(angle);
            var pulse = 0.7f + 0.3f * (0.5f + 0.5f * MathF.Sin(Time.TotalTime * 3f));
            var scale = Matrix3x2.CreateScale(pulse);

            using (Graphics.PushState())
            {
                Graphics.SetShader(EditorAssets.Shaders.Sprite);
                Graphics.SetTransform(scale * rotation * Document.Transform);
                Graphics.SetSortGroup(7);
                Graphics.SetLayer(EditorLayer.DocumentEditor);
                Graphics.SetColor(Color.White);
                Graphics.Draw(EditorAssets.Sprites.IconGenerating);
            }
        }
    }

    public override void UpdateUI() { }
    public override void LateUpdate() { }

    public override void UpdateOverlayUI()
    {
        var gen = Document.Generation;
        if (gen == null) return;

        using (FloatingToolbar.Begin())
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                var prompt = UI.TextInput(WidgetIds.GenerationPrompt, gen.Prompt, EditorStyle.TextInput with { Width = 300 }, "Prompt");
                if (prompt != gen.Prompt)
                {
                    gen.Prompt = prompt;
                    UI.HandleChange(Document);
                }

                if (gen.Job.IsGenerating)
                {
                    if (UI.Button(WidgetIds.CancelButton, EditorAssets.Sprites.IconClose, EditorStyle.FloatingToolbar.ToolButton))
                        gen.Job.CancelGeneration();
                }
                else
                {
                    using (UI.BeginEnabled(!string.IsNullOrWhiteSpace(gen.Prompt) && gen.Config.Value != null))
                    {
                        if (UI.Button(WidgetIds.GenerateButton, EditorAssets.Sprites.IconAi, EditorStyle.FloatingToolbar.ToolButton))
                            Document.GenerateAsync();
                    }
                }
            }
        }
    }

    public override void InspectorUI()
    {
        GenerationInspectorUI();
    }

    private void GenerationInspectorUI()
    {
        var gen = Document.Generation;
        if (gen == null) return;

        using (Inspector.BeginSection("GENERATION"))
        {
            if (Inspector.IsSectionCollapsed) return;

            using (Inspector.BeginProperty("Style"))
                UI.DropDown(
                    WidgetIds.StyleDropDown,
                    text: gen.Config.Name ?? "None",
                    icon: EditorAssets.Sprites.AssetIconGenstyle,
                    getItems: () =>
                    {
                        var items = new List<PopupMenuItem>
                        {
                            PopupMenuItem.Item("None", () => SetStyle(null))
                        };
                        foreach (var doc in Project.Documents)
                        {
                            if (doc is GenerationConfig styleDoc)
                                items.Add(PopupMenuItem.Item(styleDoc.Name, () => SetStyle(styleDoc)));
                        }
                        return [.. items];
                    });

            using (Inspector.BeginProperty("Prompt"))
            {
                gen.Prompt = UI.TextInput(WidgetIds.GenerationPrompt, gen.Prompt, EditorStyle.TextArea, "Prompt", multiLine: true);
                UI.HandleChange(Document);
            }

            using (Inspector.BeginProperty("Negative Prompt"))
            {
                gen.NegativePrompt = UI.TextInput(WidgetIds.GenerationNegativePrompt, gen.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", multiLine: true);
                UI.HandleChange(Document);
            }

            using (Inspector.BeginProperty("Seed"))
            using (UI.BeginRow())
            {
                using (UI.BeginFlex())
                {
                    gen.Seed = UI.TextInput(WidgetIds.Seed, gen.Seed, EditorStyle.TextInput, "Seed", icon: EditorAssets.Sprites.IconSeed);
                    UI.HandleChange(Document);
                }

                if (UI.Button(WidgetIds.RandomizeSeed, EditorAssets.Sprites.IconRandom, EditorStyle.Button.IconOnly))
                {
                    Undo.Record(Document);
                    gen.Seed = SpriteGeneration.GenerateRandomSeed();
                }
            }

            var job = gen.Job;
            if (job.IsGenerating)
                GenerationProgressUI(job);
            else
                GenerateButtonUI(job);
        }
    }

    private void SetStyle(GenerationConfig? style)
    {
        Undo.Record(Document);
        Document.Generation!.Config = style;
    }

    private void GenerationProgressUI(GenerationJob genImage)
    {
        using (UI.BeginColumn(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
            Spacing = 10,
        }))
        {
            var progressText = genImage.GenerationState switch
            {
                GenerationState.Queued when genImage.QueuePosition > 0 =>
                    $"Queued (position {genImage.QueuePosition})",
                GenerationState.Queued => "Queued...",
                GenerationState.Running => $"Generating {(int)(genImage.GenerationProgress * 100)}%",
                _ => "Starting..."
            };

            using (UI.BeginRow(new ContainerStyle { Spacing = 8 }))
            {
                UI.Text(progressText, EditorStyle.Text.Primary with { FontSize = EditorStyle.Control.TextSize });
                UI.Flex();
                if (UI.Button(WidgetIds.CancelButton, EditorAssets.Sprites.IconClose, EditorStyle.Button.IconOnly))
                    genImage.CancelGeneration();
            }

            using (UI.BeginContainer(new ContainerStyle
            {
                Width = Size.Percent(1),
                Height = 4f,
                Background = EditorStyle.Palette.Active,
                BorderRadius = 2f
            }))
            {
                UI.Container(new ContainerStyle
                {
                    Width = Size.Percent(genImage.GenerationProgress),
                    Height = 4f,
                    Background = EditorStyle.Palette.Primary,
                    BorderRadius = 2f
                });
            }
        }
    }

    private void GenerateButtonUI(GenerationJob genImage)
    {
        if (genImage.GenerationError != null)
            UI.Text(genImage.GenerationError, EditorStyle.Text.Secondary with { Color = EditorStyle.ErrorColor });

        using (UI.BeginContainer(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
        }))
        {
            using (UI.BeginEnabled(!string.IsNullOrWhiteSpace(Document.Generation!.Prompt) && Document.Generation.Config.Value != null))
                if (UI.Button(WidgetIds.GenerateButton, "Generate", EditorAssets.Sprites.IconAi, EditorStyle.Button.Primary with { Width = Size.Percent(1) }))
                    Document.GenerateAsync();
        }
    }

    private void GenerateWithRandomSeed()
    {
        Document.Generation.Seed = SpriteGeneration.GenerateRandomSeed();
        Document.GenerateAsync();
    }
}
