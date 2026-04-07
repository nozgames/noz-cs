//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId AddLayerButton { get; }
        public static partial WidgetId AddGroupButton { get; }
        public static partial WidgetId DeleteLayerButton { get; }
    }

    protected override bool ReverseChildren => true;

    protected override bool IsNodeSelected(SpriteNode node) => node == _selectedNode;
    protected override bool IsNodeActive(SpriteNode node) => node == _activeLayer;

    protected override void OnNodeClicked(SpriteNode node)
    {
        SelectedNode = node;
    }

    protected override void OnOutlinerChanged() => InvalidateComposite();

    protected override void OnVisibilityChanged(SpriteNode node)
    {
        var fi = CurrentFrameIndex;
        if (fi < Document.AnimFrames.Count)
            Document.AnimFrames[fi].SetLayerVisible(node, node.Visible);
    }

    protected override string GetNodeFallbackName(SpriteNode node) =>
        node is SpriteGroup ? "Group" : "Layer";

    protected override Sprite GetNodeIcon(SpriteNode node) =>
        node is SpriteGroup
            ? EditorAssets.Sprites.IconPathLayer
            : EditorAssets.Sprites.IconPath;

    public override void OutlinerUI()
    {
        _outlinerIndex = 0;

        HandleRenameInput();
        UpdateOutlinerDrag();

        using (UI.BeginColumn(WidgetIds.OutlinerPanel, OutlinerPanelStyle))
        {
            // Header
            using (UI.BeginRow(new ContainerStyle { Height = 22, Spacing = 4, AlignY = Align.Center }))
            {
                UI.Text("Layers", EditorStyle.Text.Secondary);
                using (UI.BeginFlex()) { }
                if (UI.Button(WidgetIds.DeleteLayerButton, EditorAssets.Sprites.IconDelete, OutlinerIconButtonStyle))
                    DeleteActiveLayer();
                if (UI.Button(WidgetIds.AddGroupButton, EditorAssets.Sprites.IconPathLayer, OutlinerIconButtonStyle))
                    AddGroup();
                if (UI.Button(WidgetIds.AddLayerButton, EditorAssets.Sprites.IconAdd, OutlinerIconButtonStyle))
                    AddLayer();
            }

            DrawNodeTree(Document.Root);
        }
    }

    private new void BeginRename()
    {
        if (ActiveLayer == null) return;
        BeginRename(ActiveLayer);
    }
}
