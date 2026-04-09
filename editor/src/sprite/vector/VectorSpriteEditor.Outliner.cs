//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class VectorSpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId AddLayerButton { get; }
    }

    protected override bool ReverseChildren => false;

    protected override bool IsNodeSelected(SpriteNode node) =>
        (node is SpriteGroup && node.IsSelected) ||
        (node is SpritePath sp && sp.IsSelected);

    protected override void OnNodeClicked(SpriteNode node) =>
        HandleOutlinerClick(node);

    protected override void OnOutlinerChanged() => MarkDirty();

    protected override void OnVisibilityChanged(SpriteNode node)
    {
    }

    protected override string GetNodeFallbackName(SpriteNode node) =>
        node is SpriteGroup ? "Group" : "Path";

    protected override void OnNodeRightClicked(SpriteNode node, bool isHovered)
    {
        var nodeIsAlreadySelected = (node is SpriteGroup sl && sl.IsSelected) ||
                                    (node is SpritePath sp && sp.IsSelected);
        if (!nodeIsAlreadySelected)
        {
            Document.Root.ClearSelection();
            Document.Root.ClearSelection();
            if (node is SpritePath p)
                p.SelectPath();
            else if (node is SpriteGroup l)
                l.IsSelected = true;
            RebuildSelectedPaths();
        }

        OpenContextMenu(WidgetIds.ContextMenu);
    }

    protected override void PopulateDragNodes(SpriteNode clickedNode, List<SpriteNode> dragNodes)
    {
        dragNodes.Clear();
        if (HasLayerSelection)
        {
            foreach (var l in _selectedLayers)
                dragNodes.Add(l);
        }
        else
        {
            foreach (var p in _selectedPaths)
                dragNodes.Add(p);
        }

        if (!dragNodes.Contains(clickedNode))
        {
            dragNodes.Clear();
            dragNodes.Add(clickedNode);
        }
    }

    public override void OutlinerUI()
    {
        _outlinerIndex = 0;

        HandleRenameInput();
        UpdateOutlinerDrag();

        void AddButton()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(WidgetIds.AddLayerButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                AddLayer();
            ElementTree.EndAlign();
        }

        using (UI.BeginColumn(WidgetIds.OutlinerPanel, OutlinerPanelStyle))
        using (Outliner.BeginSection("LAYERS", content: AddButton, collapsible: false))
        {
            DrawNodeTree(Document.Root);
        }
    }

    private void HandleOutlinerClick(SpriteNode node)
    {
        var shift = Input.IsShiftDown(InputScope.All);
        var ctrl = Input.IsCtrlDown(InputScope.All);

        if (node is SpriteGroup group)
        {
            // Group selection is mutually exclusive with path selection
            if (ctrl)
            {
                if (group.IsSelected)
                {
                    if (_selectedLayers.Count > 1)
                        group.IsSelected = false;
                }
                else
                {
                    Document.Root.ClearSelection();
                    group.IsSelected = true;
                }
            }
            else if (shift)
            {
                // Clear paths but keep other group selections
                foreach (var p in _selectedPaths)
                    p.ClearSelection();
                group.IsSelected = true;
            }
            else
            {
                Document.Root.ClearSelection();
                group.IsSelected = true;
            }

            RebuildSelectedPaths();
            return;
        }

        if (node is SpritePath path)
        {
            // Path selection is mutually exclusive with group selection
            foreach (var g in _selectedLayers)
                g.IsSelected = false;

            if (ctrl)
            {
                if (path.IsSelected)
                {
                    if (_selectedPaths.Count > 1)
                        path.DeselectPath();
                }
                else
                    path.SelectPath();
            }
            else if (shift)
            {
                path.SelectPath();
            }
            else
            {
                Document.Root.ClearSelection();
                path.SelectPath();
            }

            RebuildSelectedPaths();
        }
    }

    private void AddLayer()
    {
        Undo.Record(Document);
        var name = $"Layer {Document.Root.Children.Count + 1}";
        var layer = new SpriteGroup { Name = name };
        Document.Root.Add(layer);

        if (Document.IsAnimated)
        {
            Document.IncrementVersion();
            SetCurrentTimeSlot(TimeSlotForFrame(Document.FrameCount - 1));
        }

        MarkDirty();
    }

    private void BeginRename()
    {
        SpriteNode node;
        if (HasLayerSelection)
        {
            if (_selectedLayers.Count != 1) return;
            node = _selectedLayers[0];
        }
        else
        {
            if (_selectedPaths.Count != 1) return;
            node = _selectedPaths[0];
        }

        BeginRename(node);
    }
}
