//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class GenStyleEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId LayerPrompt { get; }
        public static partial WidgetId LayerNegativePrompt { get; }
        public static partial WidgetId LayerStrength { get; }
        public static partial WidgetId LayerGuidance { get; }
        public static partial WidgetId LayerSteps { get; }
        public static partial WidgetId StyleInpaintStrength { get; }
        public static partial WidgetId RefinePrompt { get; }
        public static partial WidgetId RefineNegativePrompt { get; }
        public static partial WidgetId RefineStrength { get; }
        public static partial WidgetId RefineGuidance { get; }
        public static partial WidgetId RefineSteps { get; }
        public static partial WidgetId StyleStrength { get; }
        public static partial WidgetId LoraDropDown { get; }
        public static partial WidgetId LoraStrength { get; }
        public static partial WidgetId Detail { get; }
        public static partial WidgetId StyleRefDelete { get; }
        public static partial WidgetId AddStyleRefDropDown { get; }
    }

    public new GenStyleDocument Document => (GenStyleDocument)base.Document;

    public override bool ShowInspector => true;

    public GenStyleEditor(GenStyleDocument doc) : base(doc)
    {
        Commands =
        [
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
        ];
    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.Draw();
    }

    public override void InspectorUI()
    {
        StyleUI();
        LayerDefaultsUI();
        RefineDefaultsUI();
    }

    private void LayerDefaultsUI()
    {
        using var _ = Inspector.BeginSection("LAYER DEFAULTS");
        if (Inspector.IsSectionCollapsed) return;

        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            using (UI.BeginFlex())
            {
                var steps = Document.DefaultSteps;
                if (UI.NumberInput(WidgetIds.LayerSteps, ref steps, EditorStyle.TextInput, min: 1, max: 100, icon: EditorAssets.Sprites.IconSort))
                {
                    Undo.Record(Document);
                    Document.DefaultSteps = steps;
                }
            }

            using (UI.BeginFlex())
            {
                var strength = Document.DefaultStrength;
                if (UI.NumberInput(WidgetIds.LayerStrength, ref strength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00", icon: EditorAssets.Sprites.IconOpacity))
                {
                    Undo.Record(Document);
                    Document.DefaultStrength = strength;
                }
            }

            using (UI.BeginFlex())
            {
                var guidance = Document.DefaultGuidanceScale;
                if (UI.NumberInput(WidgetIds.LayerGuidance, ref guidance, EditorStyle.TextInput, min: 0f, max: 30f, step: 0.1f, format: "0.0", icon: EditorAssets.Sprites.IconConstraint))
                {
                    Undo.Record(Document);
                    Document.DefaultGuidanceScale = guidance;
                }
            }
        }

        using (Inspector.BeginRow())
        {
            UI.Text("Style", EditorStyle.Text.Secondary with { AlignY = Align.Center });
            UI.Flex();
            using (UI.BeginContainer(new ContainerStyle { Width = 60 }))
            {
                var inpaintStrength = Document.StyleInpaintStrength;
                if (UI.NumberInput(WidgetIds.StyleInpaintStrength, ref inpaintStrength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00"))
                {
                    Undo.Record(Document);
                    Document.StyleInpaintStrength = inpaintStrength;
                }
            }
        }

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.Prompt = UI.TextInput(WidgetIds.LayerPrompt, Document.Prompt, EditorStyle.TextArea, "Prompt", Document, multiLine: true);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.NegativePrompt = UI.TextInput(WidgetIds.LayerNegativePrompt, Document.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document, multiLine: true);
    }

    private void RefineDefaultsUI()
    {
        using var _ = Inspector.BeginSection("REFINE DEFAULTS");
        if (Inspector.IsSectionCollapsed) return;

        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            using (UI.BeginFlex())
            {
                var steps = Document.RefineSteps;
                if (UI.NumberInput(WidgetIds.RefineSteps, ref steps, EditorStyle.TextInput, min: 1, max: 100, icon: EditorAssets.Sprites.IconSort))
                {
                    Undo.Record(Document);
                    Document.RefineSteps = steps;
                }
            }

            using (UI.BeginFlex())
            {
                var strength = Document.RefineStrength;
                if (UI.NumberInput(WidgetIds.RefineStrength, ref strength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00", icon: EditorAssets.Sprites.IconOpacity))
                {
                    Undo.Record(Document);
                    Document.RefineStrength = strength;
                }
            }

            using (UI.BeginFlex())
            {
                var guidance = Document.RefineGuidanceScale;
                if (UI.NumberInput(WidgetIds.RefineGuidance, ref guidance, EditorStyle.TextInput, min: 0f, max: 30f, step: 0.1f, format: "0.0", icon: EditorAssets.Sprites.IconConstraint))
                {
                    Undo.Record(Document);
                    Document.RefineGuidanceScale = guidance;
                }
            }
        }

        using (Inspector.BeginRow())
        {
            UI.Text("Style", EditorStyle.Text.Secondary with { AlignY = Align.Center });
            UI.Flex();
            using (UI.BeginContainer(new ContainerStyle { Width = 60 }))
            {
                var styleStrength = Document.StyleStrength;
                if (UI.NumberInput(WidgetIds.StyleStrength, ref styleStrength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00"))
                {
                    Undo.Record(Document);
                    Document.StyleStrength = styleStrength;
                }
            }
        }

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.RefinePrompt = UI.TextInput(WidgetIds.RefinePrompt, Document.RefinePrompt, EditorStyle.TextArea, "Refine Prompt", Document, multiLine: true);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.RefineNegativePrompt = UI.TextInput(WidgetIds.RefineNegativePrompt, Document.RefineNegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document, multiLine: true);
    }

    private void StyleUI()
    {
        using var _ = Inspector.BeginSection("STYLE");
        if (Inspector.IsSectionCollapsed) return;

        // LoRA
        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";
        GenerationClient.FetchLoras(server);

        var loras = GenerationClient.CachedLoras;

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
        {
            var detail = Document.Detail;
            if (UI.NumberInput(WidgetIds.Detail, ref detail, EditorStyle.TextInput, min: 0, max: 1.0f, step: 0.05f, format: "0.00", icon: EditorAssets.Sprites.IconAi))
            {
                Undo.Record(Document);
                Document.Detail = detail;
            }
        }

        using (Inspector.BeginRow())
        {
            using (UI.BeginFlex())
                UI.DropDown(WidgetIds.LoraDropDown, () =>
                {
                    var loraItems = new List<PopupMenuItem>
                    {
                        PopupMenuItem.Item("None", () =>
                        {
                            Undo.Record(Document);
                            Document.LoraName = null;
                            Document.IncrementVersion();
                        })
                    };
                    if (loras != null)
                    {
                        foreach (var lora in loras)
                            loraItems.Add(PopupMenuItem.Item(lora.Name, () =>
                            {
                                Undo.Record(Document);
                                Document.LoraName = lora.Name;
                                Document.LoraStrength = lora.DefaultStrength;
                                Document.IncrementVersion();
                            }));
                    }
                    return loraItems.ToArray();
                }, Document.LoraName ?? "None", icon: EditorAssets.Sprites.IconPalette);

            if (!string.IsNullOrEmpty(Document.LoraName))
            {
                using (UI.BeginContainer(new ContainerStyle { Width = 60 }))
                {
                    var strength = Document.LoraStrength;
                    if (UI.NumberInput(WidgetIds.LoraStrength, ref strength, EditorStyle.TextInput, min: 0f, max: 2f, step: 0.05f, format: "0.00"))
                    {
                        Undo.Record(Document);
                        Document.LoraStrength = strength;
                    }
                }
            }
        }

        // Style references
        for (int i = 0; i < Document.StyleReferences.Count; i++)
        {
            var name = Document.StyleReferences[i];
            using (Inspector.BeginRow())
            {
                UI.Text(name, EditorStyle.Text.Primary with { AlignY = Align.Center });
                UI.Flex();

                if (UI.Button(WidgetIds.StyleRefDelete + i, EditorAssets.Sprites.IconDelete, EditorStyle.Button.SmallIconOnly))
                {
                    Undo.Record(Document);
                    Document.StyleReferences.RemoveAt(i);
                    Document.IncrementVersion();
                }
            }
        }

        // Add style ref
        var existing = new HashSet<string>(Document.StyleReferences);
        var names = new List<string>();

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is not TextureDocument) continue;
            if (existing.Contains(doc.Name)) continue;
            names.Add(doc.Name);
        }

        if (names.Count > 0)
        {
            names.Sort(StringComparer.OrdinalIgnoreCase);

            using (Inspector.BeginRow())
            using (UI.BeginFlex())
            {
                var selected = AssetBrowser.Show(WidgetIds.AddStyleRefDropDown, names.ToArray());
                if (selected != null)
                {
                    Undo.Record(Document);
                    Document.StyleReferences.Add(selected);
                    Document.IncrementVersion();
                }
            }
        }
    }
}
