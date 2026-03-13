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
        public static partial WidgetId ModelDropDown { get; }
        public static partial WidgetId Detail { get; }
        public static partial WidgetId WorkflowDropDown { get; }
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
        using (UI.BeginFlex())
            Document.Prompt = UI.TextInput(WidgetIds.LayerPrompt, Document.Prompt, EditorStyle.TextArea, "Prompt", Document, multiLine: true);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.NegativePrompt = UI.TextInput(WidgetIds.LayerNegativePrompt, Document.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document, multiLine: true);
    }

    private void StyleUI()
    {
        using var _ = Inspector.BeginSection("STYLE");
        if (Inspector.IsSectionCollapsed) return;

        // Workflow
        using (Inspector.BeginProperty("Workflow"))
        {
            var workflows = Enum.GetValues<GenerationWorkflow>();
            UI.DropDown(WidgetIds.WorkflowDropDown, () =>
            {
                var items = new List<PopupMenuItem>();
                foreach (var wf in workflows)
                    items.Add(PopupMenuItem.Item(wf.ToString(), () =>
                    {
                        Undo.Record(Document);
                        Document.Workflow = wf;
                        Document.IncrementVersion();
                    }));
                return items.ToArray();
            }, Document.Workflow.ToString(), icon: EditorAssets.Sprites.IconAi);
        }

        // Model
        var server = EditorApplication.Config?.GenerationServer ?? "http://127.0.0.1:7860";
        GenerationClient.FetchModels(server);
        var models = GenerationClient.CachedModels;
        using (Inspector.BeginProperty("Model"))
        {
            UI.DropDown(WidgetIds.ModelDropDown, () =>
            {
                var items = new List<PopupMenuItem>
                {
                    PopupMenuItem.Item("None", () =>
                    {
                        Undo.Record(Document);
                        Document.ModelName = null;
                        Document.IncrementVersion();
                    })
                };
                if (models != null)
                {
                    foreach (var model in models)
                        items.Add(PopupMenuItem.Item(model.Name, () =>
                        {
                            Undo.Record(Document);
                            Document.ModelName = model.Name;
                            Document.IncrementVersion();
                        }));
                }
                return items.ToArray();
            }, Document.ModelName ?? "None", icon: EditorAssets.Sprites.IconPalette);
        }

    }
}
