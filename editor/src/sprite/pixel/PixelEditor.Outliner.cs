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
        public static partial WidgetId ContextMenu { get; }
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

    protected override void OnNodeRightClicked(SpriteNode node, bool isHovered)
    {
        if (!node.IsSelected)
        {
            Document.Root.ClearSelection();
            ClearSelection();
            node.IsSelected = true;
            if (node is PixelLayer pl)
                ActiveLayer = pl;
            _selectedNode = node;
        }
        OpenContextMenu(WidgetIds.ContextMenu);
    }

    public override void OpenContextMenu(WidgetId popupId)
    {
        var anchor = _selectedNode;
        var hideLabel = anchor != null && anchor.Visible ? "Hide" : "Show";
        var hideIcon = anchor != null && anchor.Visible
            ? EditorAssets.Sprites.IconHidden
            : EditorAssets.Sprites.IconPreview;
        var lockLabel = anchor != null && anchor.Locked ? "Unlock" : "Lock";
        var lockIcon = anchor != null && anchor.Locked
            ? EditorAssets.Sprites.IconUnlock
            : EditorAssets.Sprites.IconLock;

        var items = new List<PopupMenuItem>
        {
            PopupMenuItem.Item(hideLabel, ToggleSelectedVisibility,
                enabled: () => _selectedNode != null, icon: hideIcon),
            PopupMenuItem.Item(lockLabel, ToggleSelectedLock,
                enabled: () => _selectedNode != null, icon: lockIcon),
            PopupMenuItem.Separator(),
            PopupMenuItem.Item("Merge Down", MergeDownActiveLayer,
                enabled: () => CanMergeDown()),
        };
        UI.OpenPopupMenu(popupId, items.ToArray(), EditorStyle.ContextMenu.Style);
    }

    private void ToggleSelectedVisibility()
    {
        if (_selectedNode == null) return;
        Undo.Record(Document);
        var target = !_selectedNode.Visible;
        var nodes = new List<SpriteNode>();
        Document.Root.Collect<SpriteNode>(nodes, n => n.IsSelected);
        foreach (var node in nodes)
        {
            node.Visible = target;
            OnVisibilityChanged(node);
        }
        OnOutlinerChanged();
    }

    private void ToggleSelectedLock()
    {
        if (_selectedNode == null) return;
        Undo.Record(Document);
        var target = !_selectedNode.Locked;
        var nodes = new List<SpriteNode>();
        Document.Root.Collect<SpriteNode>(nodes, n => n.IsSelected);
        foreach (var node in nodes)
            node.Locked = target;
        OnOutlinerChanged();
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
