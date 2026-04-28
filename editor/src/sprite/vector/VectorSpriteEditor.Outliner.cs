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

    protected override bool IsNodeActive(SpriteNode node) => node == _pivotNode;

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
            _pivotNode = node;
            RebuildSelectedPaths(expandAncestors: false);
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

        if (ctrl)
            CtrlToggleNode(node);
        else if (shift && _pivotNode != null)
            ShiftRangeSelect(_pivotNode, node);
        else
            SingleSelect(node);

        RebuildSelectedPaths(expandAncestors: false);
    }

    private void SingleSelect(SpriteNode node)
    {
        Document.Root.ClearSelection();
        SetNodeSelected(node, true);
        _pivotNode = node;
    }

    private void CtrlToggleNode(SpriteNode node)
    {
        if (node.IsSelected)
        {
            var ownListCount = node is SpriteGroup ? _selectedLayers.Count : _selectedPaths.Count;
            if (ownListCount <= 1) return;

            SetNodeSelected(node, false);
            if (_pivotNode == node)
                _pivotNode = FirstSelectedOfType(node);
            return;
        }

        // Adding a node of a different type clears the other type to preserve mutual exclusion.
        if (!SameSelectionType(node, null))
            Document.Root.ClearSelection();

        SetNodeSelected(node, true);
        _pivotNode = node;
    }

    private void ShiftRangeSelect(SpriteNode pivot, SpriteNode clicked)
    {
        var visible = new List<SpriteNode>();
        CollectVisibleNodes(Document.Root, visible);

        var pivotIdx = visible.IndexOf(pivot);
        var clickedIdx = visible.IndexOf(clicked);
        if (pivotIdx < 0 || clickedIdx < 0)
        {
            SingleSelect(clicked);
            return;
        }

        Document.Root.ClearSelection();

        var lo = Math.Min(pivotIdx, clickedIdx);
        var hi = Math.Max(pivotIdx, clickedIdx);
        var clickedIsGroup = clicked is SpriteGroup;
        for (var i = lo; i <= hi; i++)
        {
            var n = visible[i];
            if ((n is SpriteGroup) == clickedIsGroup)
                SetNodeSelected(n, true);
        }
        // Pivot stays put.
    }

    private static void SetNodeSelected(SpriteNode node, bool selected)
    {
        if (node is SpritePath p)
        {
            if (selected) p.SelectPath();
            else p.DeselectPath();
        }
        else
        {
            node.IsSelected = selected;
        }
    }

    // When `other` is null, checks against the currently selected type (any).
    private bool SameSelectionType(SpriteNode a, SpriteNode? other)
    {
        var aIsGroup = a is SpriteGroup;
        if (other != null)
            return aIsGroup == (other is SpriteGroup);

        if (_selectedLayers.Count > 0) return aIsGroup;
        if (_selectedPaths.Count > 0) return !aIsGroup;
        return true;
    }

    private SpriteNode? FirstSelectedOfType(SpriteNode like)
    {
        // Caches may be stale mid-click; walk them for a still-selected node.
        if (like is SpriteGroup)
        {
            foreach (var g in _selectedLayers)
                if (g.IsSelected) return g;
            return null;
        }
        foreach (var p in _selectedPaths)
            if (p.IsSelected) return p;
        return null;
    }

    private static void CollectVisibleNodes(SpriteNode root, List<SpriteNode> outList)
    {
        foreach (var child in root.Children)
        {
            outList.Add(child);
            if (child.IsExpandable && child.Expanded)
                CollectVisibleNodes(child, outList);
        }
    }

    private void AddLayer()
    {
        Undo.Record(Document);
        var name = $"Layer {Document.Root.Children.Count + 1}";
        var layer = new SpriteGroup { Name = name };
        Document.Root.Insert(0, layer);

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
