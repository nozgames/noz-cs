//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class GenStyleEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId LayerPromptPrefix { get; }
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

        var model = GenerationClient.GetModel(Document.ModelName);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.PromptPrefix = UI.TextInput(WidgetIds.LayerPromptPrefix, Document.PromptPrefix, EditorStyle.TextArea, "Prompt Prefix", Document, multiLine: true);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.Prompt = UI.TextInput(WidgetIds.LayerPrompt, Document.Prompt, EditorStyle.TextArea, "Prompt", Document, multiLine: true);

        if (model != null && model.HasControl("negative_prompt"))
        {
            using (Inspector.BeginRow())
            using (UI.BeginFlex())
                Document.NegativePrompt = UI.TextInput(WidgetIds.LayerNegativePrompt, Document.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document, multiLine: true);
        }
    }

    private void StyleUI()
    {
        using var _ = Inspector.BeginSection("STYLE");
        if (Inspector.IsSectionCollapsed) return;

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
