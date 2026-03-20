//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class GenerationConfigEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId GenerationPrompt { get; }
        public static partial WidgetId GenerationNegativePrompt { get; }
        public static partial WidgetId ModelDropDown { get; }
        public static partial WidgetId StyleDropDown { get; }
        public static partial WidgetId RefreshModels { get; }
        public static partial WidgetId RemoveBackground { get; }
    }

    public new GenerationConfig Document => (GenerationConfig)base.Document;

    public override bool ShowInspector => true;

    public GenerationConfigEditor(GenerationConfig doc) : base(doc)
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
        GenerationPromptsUI();
    }

    private void GenerationPromptsUI()
    {
        using var _ = Inspector.BeginSection("PROMPTS");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.Prompt = UI.TextInput(WidgetIds.GenerationPrompt, Document.Prompt, EditorStyle.TextArea, "Prompt (use {0} for sprite prompt)", Document, multiLine: true);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.NegativePrompt = UI.TextInput(WidgetIds.GenerationNegativePrompt, Document.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document, multiLine: true);
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
            using (UI.BeginRow())
            {
                using (UI.BeginFlex())
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

                if (UI.Button(WidgetIds.RefreshModels, EditorAssets.Sprites.IconRefresh, EditorStyle.Button.IconOnly))
                    GenerationClient.InvalidateModels();
            }
        }

        var selectedModel = GenerationClient.GetModel(Document.ModelName);
        if (selectedModel != null && selectedModel.Styles.Count > 0)
        {
            using (Inspector.BeginProperty("Style"))
            {
                using (UI.BeginFlex())
                    UI.DropDown(WidgetIds.StyleDropDown, () =>
                    {
                        var items = new List<PopupMenuItem>
                        {
                            PopupMenuItem.Item("None", () =>
                            {
                                Undo.Record(Document);
                                Document.StyleKey = null;
                                Document.IncrementVersion();
                            })
                        };
                        foreach (var style in selectedModel.Styles)
                            items.Add(PopupMenuItem.Item(style.Name, () =>
                            {
                                Undo.Record(Document);
                                Document.StyleKey = style.Key;
                                Document.IncrementVersion();
                            }));
                        return items.ToArray();
                    }, selectedModel.Styles.Find(s => s.Key == Document.StyleKey)?.Name ?? "None");
            }
        }

        using (Inspector.BeginProperty("Remove Background"))
        {
            if (UI.Toggle(WidgetIds.RemoveBackground, "", Document.RemoveBackground, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
            {
                Undo.Record(Document);
                Document.RemoveBackground = !Document.RemoveBackground;
                Document.IncrementVersion();
            }
        }
    }
}
