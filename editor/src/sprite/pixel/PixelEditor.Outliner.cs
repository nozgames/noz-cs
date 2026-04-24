//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId AddLayerButton { get; }
        public static partial WidgetId AddGroupButton { get; }
        public static partial WidgetId DeleteLayerButton { get; }
    }

    protected override bool ReverseChildren => true;

    protected override bool IsNodeSelected(SpriteNode node) => node.IsSelected;
    protected override bool IsNodeActive(SpriteNode node) => node == _activeLayer;

    protected override void OnNodeClicked(SpriteNode node)
    {
        var shift = Input.IsShiftDown(InputScope.All);
        var ctrl = Input.IsCtrlDown(InputScope.All);

        if (ctrl)
        {
            node.IsSelected = !node.IsSelected;
        }
        else if (shift)
        {
            node.IsSelected = true;
        }
        else
        {
            Document.Root.ClearSelection();
            node.IsSelected = true;
            ClearSelection();
        }

        if (node is PixelLayer layer)
            ActiveLayer = layer;

        _selectedNode = node;
    }

    protected override void OnOutlinerChanged() => InvalidateComposite();

    protected override void OnVisibilityChanged(SpriteNode node)
    {
    }

    protected override string GetNodeFallbackName(SpriteNode node) =>
        node is SpriteGroup ? "Group" : "Layer";

    protected override Sprite GetNodeIcon(SpriteNode node) =>
        node is SpriteGroup
            ? EditorAssets.Sprites.IconFolder
            : EditorAssets.Sprites.IconPath;

    public override void OutlinerUI()
    {
        _outlinerIndex = 0;

        HandleRenameInput();
        UpdateOutlinerDrag();

        void HeaderButtons()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                if (UI.Button(WidgetIds.DeleteLayerButton, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
                    DeleteActiveLayer();
                if (UI.Button(WidgetIds.AddGroupButton, EditorAssets.Sprites.IconFolder, EditorStyle.Inspector.SectionButton))
                    AddGroup();
                if (UI.Button(WidgetIds.AddLayerButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                    AddLayer();
            }
            ElementTree.EndAlign();
        }

        using (UI.BeginColumn(WidgetIds.OutlinerPanel, OutlinerPanelStyle))
        using (Outliner.BeginSection("LAYERS", content: HeaderButtons, collapsible: false))
        {
            DrawNodeTree(Document.Root);
        }
    }

    private void BeginRename()
    {
        if (ActiveLayer == null) return;
        BeginRename(ActiveLayer);
    }
}
