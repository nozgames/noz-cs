//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteEditor
{
    private const float DragThreshold = 4f;

    private struct OutlinerRowInfo
    {
        public SpriteNode Node;
        public int Index;
        public int Depth;
    }

    private enum DropZone { Before, After, FirstChild, LastChild }

    private static partial class WidgetIds
    {
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId OutlinerLayer { get; }
        public static partial WidgetId OutlinerVisibility { get; }
        public static partial WidgetId OutlinerLock { get; }
        public static partial WidgetId OutlinerExpand { get; }
        public static partial WidgetId AddLayerButton { get; }
        public static partial WidgetId OutlinerRename { get; }
    }

    private int _outlinerIndex;
    private SpriteNode? _renameNode;
    private string _renameText = "";
    private readonly List<OutlinerRowInfo> _outlinerRows = [];
    private bool _outlinerDragging;
    private readonly List<SpriteNode> _dragNodes = [];
    private SpriteNode? _deferredClickNode;
    private Vector2 _dragStartPos;
    private int _dropTargetIndex = -1;
    private DropZone _dropZone;

    private static readonly ContainerStyle OutlinerPanelStyle = EditorStyle.Panel with
    {
        Width = Size.Percent(1),
        Height = Size.Percent(1),
        Padding = EdgeInsets.All(4),
        Spacing = 0,
    };

    private static readonly TextStyle OutlinerLayerNameStyle = new()
    {
        FontSize = 10,
        Color = EditorStyle.Palette.Content,
        AlignY = Align.Center,
    };

    private static readonly TextStyle OutlinerLayerNameDimStyle = OutlinerLayerNameStyle with
    {
        Color = EditorStyle.Palette.SecondaryText,
    };

    private static readonly ButtonStyle OutlinerIconButtonStyle = new()
    {
        Width = EditorStyle.Icon.Size,
        Height = EditorStyle.Control.Height,
        Background = Color.Transparent,
        ContentColor = EditorStyle.Palette.Content,
        IconSize = EditorStyle.Icon.Size,
        Resolve = (s, f) =>
        {
            if ((f & WidgetFlags.Hovered) != 0) s.Background = EditorStyle.Palette.Active;
            return s;
        },
    };

    private static readonly ButtonStyle OutlinerIconDimButtonStyle = OutlinerIconButtonStyle with
    {
        ContentColor = EditorStyle.Palette.SecondaryText,
    };

    public override void OutlinerUI()
    {
        if (!Document.IsMutable) return;

        _outlinerIndex = 0;

        // Handle rename input before anything else
        if (_renameNode != null)
        {
            if (Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
            {
                Input.ConsumeButton(InputCode.KeyEnter);
                CommitRename();
            }
            else if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
            {
                Input.ConsumeButton(InputCode.KeyEscape);
                CancelRename();
            }
        }

        // Update drag state (uses previous frame's row data for hit-testing)
        UpdateOutlinerDrag();

        _outlinerRows.Clear();

        using (UI.BeginColumn(WidgetIds.OutlinerPanel, OutlinerPanelStyle))
        {
            // Header
            using (UI.BeginRow(new ContainerStyle { Height = 22, Spacing = 4, AlignY = Align.Center }))
            {
                UI.Text("Layers",  EditorStyle.Text.Secondary);
                using (UI.BeginFlex()) { }
                if (UI.Button(WidgetIds.AddLayerButton, EditorAssets.Sprites.IconAdd, OutlinerIconButtonStyle))
                    AddLayer();
            }

            // Node tree
            foreach (var child in Document.RootLayer.Children)
                OutlinerNodeUI(child, 0);
        }
    }

    private const float DropLineHeight = 2;

    private void OutlinerNodeUI(SpriteNode node, int depth)
    {
        var index = _outlinerIndex++;
        var rowId = WidgetIds.OutlinerLayer + index;
        var isLayer = node is SpriteLayer;
        var isPath = node is SpritePath;
        var isActive = isLayer && node.IsSelected;
        var isPathSelected = isPath && ((SpritePath)node).IsSelected;
        var isDragTarget = _outlinerDragging && _dropTargetIndex == index;
        var isDragSource = _outlinerDragging && _dragNodes.Contains(node);
        var dropBefore = isDragTarget && _dropZone == DropZone.Before;
        var dropAfter = isDragTarget && (_dropZone == DropZone.After || _dropZone == DropZone.FirstChild);
        var dropLastChild = isDragTarget && _dropZone == DropZone.LastChild;

        _outlinerRows.Add(new OutlinerRowInfo { Node = node, Index = index, Depth = depth });

        if (isDragSource)
            UI.BeginOpacity(0.35f);

        ElementTree.BeginTree();
        ElementTree.BeginWidget(rowId);
        ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);

        // Determine background (must be after BeginWidget so hover state is available)
        var bg = (isActive || isPathSelected) ? EditorStyle.Palette.Active : Color.Transparent;
        var isHovered = !_outlinerDragging && UI.IsHovered(rowId);
        if (isHovered) bg = EditorStyle.Palette.Active;

        // Content area
        if (dropLastChild)
            ElementTree.BeginFill(bg, borderWidth: 1, borderColor: EditorStyle.Palette.Primary);
        else
            ElementTree.BeginFill(bg);

        // drag indicators
        if (!dropLastChild && (dropBefore || dropAfter))
        {
            ElementTree.BeginAlign(Align.Min, dropAfter ? Align.Max : Align.Min);
            ElementTree.BeginSize(Size.Default, new Size(DropLineHeight));
            ElementTree.BeginFill(EditorStyle.Palette.Primary);
            ElementTree.EndFill();
            ElementTree.EndSize();
            ElementTree.EndAlign();
        }

        ElementTree.BeginPadding(EditorStyle.Item.Padding);
        ElementTree.BeginRow(EditorStyle.Control.Spacing);

        // indent
        if (depth > 0)
            ElementTree.Spacer(depth * (EditorStyle.Icon.SmallSize + EditorStyle.Control.Spacing) - EditorStyle.Control.Spacing);

        // expand
        if (isLayer && node.Children.Count > 0)
        {
            ElementTree.BeginTree();
            ElementTree.BeginWidget(WidgetIds.OutlinerExpand + index);
            ElementTree.BeginSize(EditorStyle.Icon.SmallSize, Size.Default);
            ElementTree.Image(
                image: node.Expanded
                    ? EditorAssets.Sprites.IconFoldoutClosed
                    : EditorAssets.Sprites.IconFoldoutOpen,
                size: new Size2(EditorStyle.Icon.SmallSize, Size.Default),
                align: Align.Center,
                color: EditorStyle.Palette.SecondaryText);

            if (ElementTree.WasPressed())
                node.Expanded = !node.Expanded;

            ElementTree.EndTree();
        }
        else
        {
            UI.Spacer(EditorStyle.Icon.SmallSize);
        }

        // icon
        ElementTree.Image(
            image: isLayer
                ? EditorAssets.Sprites.IconPathLayer
                : EditorAssets.Sprites.IconPath,
            size: new Size2(EditorStyle.Icon.Size, Size.Default),
            align: Align.Center,
            color: EditorStyle.Palette.SecondaryText);

        // name (inline rename or static text)
        ElementTree.BeginFlex();
        if (node == _renameNode)
        {
            ElementTree.BeginMargin(EdgeInsets.TopLeft(2, -2));
            _renameText = UI.TextInput(WidgetIds.OutlinerRename, _renameText, EditorStyle.SpriteEditor.OutlinerRename);
            ElementTree.EndMargin();

            if (UI.HotExit())
                CommitRename();
        }
        else
        {
            ElementTree.Text(
                value: !string.IsNullOrEmpty(node.Name) ? node.Name : (isLayer ? "Group" : "Path"),
                font: UI.DefaultFont,
                fontSize: EditorStyle.Text.Size,
                color: EditorStyle.Palette.SecondaryText,
                align: new Align2(Align.Min, Align.Center)
            );
        }
        ElementTree.EndFlex();

        // Visibility + Lock
        var showVisibility = isHovered || !node.Visible;
        var showLock = isHovered || node.Locked;

        if (showVisibility)
        {
            var visIcon = node.Visible ? EditorAssets.Sprites.IconPreview : EditorAssets.Sprites.IconHidden;
            var visStyle = node.Visible ? OutlinerIconDimButtonStyle : OutlinerIconButtonStyle;
            if (UI.Button(WidgetIds.OutlinerVisibility + index, visIcon, visStyle))
            {
                Undo.Record(Document);
                node.Visible = !node.Visible;

                var fi = CurrentFrameIndex;
                if (fi < Document.AnimFrames.Count && node is SpriteLayer layer)
                    Document.AnimFrames[fi].SetLayerVisible(layer, node.Visible);

                MarkDirty();
            }
        }

        if (showLock)
        {
            var lockIcon = node.Locked ? EditorAssets.Sprites.IconLock : EditorAssets.Sprites.IconUnlock;
            var lockStyle = node.Locked ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(WidgetIds.OutlinerLock + index, lockIcon, lockStyle))
            {
                Undo.Record(Document);
                node.Locked = !node.Locked;
                MarkDirty();
            }
        }
        else
        {
            UI.Spacer(EditorStyle.Icon.Size);
        }

        if (UI.WasPressed(rowId))
        {
            var nodeIsSelected = (node is SpriteLayer && node.IsSelected) ||
                                 (node is SpritePath sp && sp.IsSelected);
            var shift = Input.IsShiftDown(InputScope.All);
            var ctrl = Input.IsCtrlDown(InputScope.All);

            if (nodeIsSelected && !shift && !ctrl)
            {
                // Defer selection change so we can drag the multi-selection
                _deferredClickNode = node;
            }
            else
            {
                HandleOutlinerClick(node);
                _deferredClickNode = null;
            }

            // Populate drag candidates from current selection
            _dragNodes.Clear();
            if (HasLayerSelection)
            {
                foreach (var l in _selectedLayers)
                    _dragNodes.Add(l);
            }
            else
            {
                foreach (var p in _selectedPaths)
                    _dragNodes.Add(p);
            }

            // Ensure the clicked node is always included
            if (!_dragNodes.Contains(node))
            {
                _dragNodes.Clear();
                _dragNodes.Add(node);
            }

            _dragStartPos = Input.MousePosition;
        }

        // Right-click context menu
        if (isHovered && Input.WasButtonReleased(InputCode.MouseRight))
        {
            var nodeIsAlreadySelected = (node is SpriteLayer sl && sl.IsSelected) ||
                                        (node is SpritePath sp2 && sp2.IsSelected);
            if (!nodeIsAlreadySelected)
            {
                Document.RootLayer.ClearSelection();
                Document.RootLayer.ClearLayerSelections();
                if (node is SpritePath p)
                    p.SelectPath();
                else if (node is SpriteLayer l)
                    l.IsSelected = true;
                RebuildSelectedPaths();
            }

            OpenContextMenu(WidgetIds.ContextMenu);
        }

        // End Row + Padding + Fill + Flex
        ElementTree.EndRow();
        ElementTree.EndPadding();
        ElementTree.EndFill();

        // EndTree closes Column + Size + Widget
        ElementTree.EndTree();

        if (isDragSource)
            UI.EndOpacity();

        // Draw children if expanded (layers only)
        if (isLayer && node.Expanded)
        {
            foreach (var child in node.Children)
                OutlinerNodeUI(child, depth + 1);
        }
    }

    private void UpdateOutlinerDrag()
    {
        if (_dragNodes.Count == 0)
        {
            _outlinerDragging = false;
            _dropTargetIndex = -1;
            return;
        }

        if (!CheckDragThreshold())
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) || Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            CancelOutlinerDrag();
            return;
        }

        FindDropTarget();

        if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
        {
            if (_dropTargetIndex >= 0)
                CommitOutlinerDrop();

            CancelOutlinerDrag();
        }
    }

    private bool CheckDragThreshold()
    {
        if (_outlinerDragging)
            return true;

        if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
        {
            // Mouse released before drag threshold — apply deferred click
            if (_deferredClickNode != null)
            {
                HandleOutlinerClick(_deferredClickNode);
                _deferredClickNode = null;
            }
            _dragNodes.Clear();
            return false;
        }

        var dist = System.Numerics.Vector2.Distance(Input.MousePosition, _dragStartPos);
        if (dist < DragThreshold)
            return false;

        _deferredClickNode = null;
        _outlinerDragging = true;
        return true;
    }

    private void CancelOutlinerDrag()
    {
        _outlinerDragging = false;
        _dragNodes.Clear();
        _deferredClickNode = null;
        _dropTargetIndex = -1;
    }

    private void FindDropTarget()
    {
        _dropTargetIndex = -1;
        var mouseWorld = UI.MouseWorldPosition;
        var closestDist = float.MaxValue;
        var closestRow = -1;

        for (var i = 0; i < _outlinerRows.Count; i++)
        {
            var row = _outlinerRows[i];
            if (_dragNodes.Contains(row.Node))
            {
                var srcRect = UI.GetElementWorldRect(WidgetIds.OutlinerLayer + row.Index);
                if (srcRect.Width > 0 && mouseWorld.Y >= srcRect.Y && mouseWorld.Y <= srcRect.Bottom)
                {
                    closestRow = -1;
                    break;
                }
                continue;
            }

            var rect = UI.GetElementWorldRect(WidgetIds.OutlinerLayer + row.Index);
            if (rect.Width <= 0) continue;

            if (mouseWorld.Y >= rect.Y && mouseWorld.Y <= rect.Bottom)
            {
                _dropTargetIndex = row.Index;
                var relY = (mouseWorld.Y - rect.Y) / rect.Height;

                if (row.Node is SpriteLayer)
                {
                    if (relY < 0.33f) _dropZone = DropZone.Before;
                    else if (relY > 0.67f) _dropZone = DropZone.FirstChild;
                    else _dropZone = DropZone.LastChild;
                }
                else
                {
                    _dropZone = relY < 0.5f ? DropZone.Before : DropZone.After;
                }

                NormalizeDropBefore(i, row);
                break;
            }

            var dist = mouseWorld.Y < rect.Y ? rect.Y - mouseWorld.Y : mouseWorld.Y - rect.Bottom;
            if (dist < closestDist)
            {
                closestDist = dist;
                closestRow = i;
            }
        }

        if (_dropTargetIndex < 0 && closestRow >= 0)
            FindDropTargetFallback(closestRow, mouseWorld);
    }

    private void NormalizeDropBefore(int rowIndex, OutlinerRowInfo row)
    {
        if (_dropZone != DropZone.Before || rowIndex <= 0)
            return;

        var pi = rowIndex - 1;
        while (pi >= 0 && _dragNodes.Contains(_outlinerRows[pi].Node)) pi--;

        if (pi < 0) return;

        var prevRow = _outlinerRows[pi];
        if (prevRow.Depth == row.Depth)
        {
            _dropTargetIndex = prevRow.Index;
            _dropZone = DropZone.After;
        }
        else if (prevRow.Depth == row.Depth - 1 && prevRow.Node is SpriteLayer parentLayer && parentLayer.Expanded)
        {
            _dropTargetIndex = prevRow.Index;
            _dropZone = DropZone.FirstChild;
        }
    }

    private void FindDropTargetFallback(int closestRow, Vector2 mouseWorld)
    {
        var row = _outlinerRows[closestRow];
        var rect = UI.GetElementWorldRect(WidgetIds.OutlinerLayer + row.Index);

        if (mouseWorld.Y > rect.Bottom)
        {
            for (var j = _outlinerRows.Count - 1; j >= 0; j--)
            {
                if (_outlinerRows[j].Depth == 0 && !_dragNodes.Contains(_outlinerRows[j].Node))
                {
                    _dropTargetIndex = _outlinerRows[j].Index;
                    _dropZone = DropZone.After;
                    break;
                }
            }
        }
        else
        {
            _dropTargetIndex = row.Index;
            _dropZone = mouseWorld.Y < rect.Y + rect.Height * 0.5f ? DropZone.Before : DropZone.After;
        }
    }

    private void CommitOutlinerDrop()
    {
        if (_dragNodes.Count == 0) return;

        // Find the target node
        SpriteNode? targetNode = null;
        foreach (var row in _outlinerRows)
        {
            if (row.Index == _dropTargetIndex)
            {
                targetNode = row.Node;
                break;
            }
        }
        if (targetNode == null) return;

        // Validate all drag nodes
        foreach (var dragNode in _dragNodes)
        {
            if (dragNode == targetNode) return;
            if (IsDescendant(dragNode, targetNode)) return;
        }

        // Sort drag nodes by outliner order to preserve relative ordering
        var ordered = new List<SpriteNode>(_dragNodes.Count);
        foreach (var row in _outlinerRows)
        {
            if (_dragNodes.Contains(row.Node))
                ordered.Add(row.Node);
        }

        Undo.Record(Document);

        // Remove all from parents first
        foreach (var node in ordered)
            node.RemoveFromParent();

        // Insert all at the drop location
        switch (_dropZone)
        {
            case DropZone.LastChild when targetNode is SpriteLayer layer:
                foreach (var node in ordered)
                    layer.Add(node);
                layer.Expanded = true;
                break;

            case DropZone.FirstChild when targetNode is SpriteLayer layer2:
                for (var i = ordered.Count - 1; i >= 0; i--)
                    layer2.Insert(0, ordered[i]);
                layer2.Expanded = true;
                break;

            case DropZone.Before:
            case DropZone.After:
            {
                var targetParent = targetNode.Parent;
                if (targetParent == null) return;
                var targetIdx = targetParent.Children.IndexOf(targetNode);
                if (_dropZone == DropZone.After) targetIdx++;
                for (var i = 0; i < ordered.Count; i++)
                    targetParent.Insert(targetIdx + i, ordered[i]);
                break;
            }
        }

        MarkDirty();
    }

    private static bool IsDescendant(SpriteNode parent, SpriteNode candidate)
    {
        foreach (var child in parent.Children)
        {
            if (child == candidate || IsDescendant(child, candidate))
                return true;
        }
        return false;
    }

    private void HandleOutlinerClick(SpriteNode node)
    {
        var shift = Input.IsShiftDown(InputScope.All);
        var ctrl = Input.IsCtrlDown(InputScope.All);

        if (node is SpriteLayer layer)
        {
            // Layer selection is mutually exclusive with path selection
            if (ctrl)
            {
                // Ctrl+click: toggle this layer
                if (layer.IsSelected)
                {
                    layer.IsSelected = false;
                }
                else
                {
                    Document.RootLayer.ClearSelection();
                    layer.IsSelected = true;
                }
            }
            else if (shift)
            {
                // Shift+click: add layer, deselect all paths
                Document.RootLayer.ClearSelection();
                layer.IsSelected = true;
            }
            else
            {
                // Plain click: clear everything, select this layer
                Document.RootLayer.ClearSelection();
                Document.RootLayer.ClearLayerSelections();
                layer.IsSelected = true;
            }

            RebuildSelectedPaths();
            return;
        }

        if (node is SpritePath path)
        {
            // Path selection is mutually exclusive with layer selection
            Document.RootLayer.ClearLayerSelections();

            if (ctrl)
            {
                // Ctrl+click: toggle this path's selection
                if (path.IsSelected)
                    path.DeselectPath();
                else
                    path.SelectPath();
            }
            else if (shift)
            {
                // Shift+click: add to selection
                path.SelectPath();
            }
            else
            {
                // Plain click: clear all, select this path
                Document.RootLayer.ClearSelection();
                path.SelectPath();
            }

            RebuildSelectedPaths();
        }
    }

    private void AddLayer()
    {
        Undo.Record(Document);
        var name = $"Layer {Document.RootLayer.Children.Count + 1}";
        var layer = new SpriteLayer { Name = name };
        Document.RootLayer.Add(layer);
        MarkDirty();
    }

    #region Rename

    private void BeginRename()
    {
        // Layer and path selection are mutually exclusive; when a layer is selected,
        // _selectedPaths contains its child paths for bounds, so check them separately.
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

        _renameNode = node;
        _renameText = node.Name ?? "";
        UI.SetHot(WidgetIds.OutlinerRename);
    }

    private void CommitRename()
    {
        if (_renameNode != null && !string.IsNullOrWhiteSpace(_renameText) && _renameText != _renameNode.Name)
        {
            Undo.Record(Document);
            _renameNode.Name = _renameText;
            MarkDirty();
        }
        _renameNode = null;
    }

    private void CancelRename()
    {
        _renameNode = null;
    }

    #endregion
}
