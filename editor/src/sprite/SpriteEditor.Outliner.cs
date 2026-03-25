//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId OutlinerSplitter { get; }
        public static partial WidgetId OutlinerLayer { get; }
        public static partial WidgetId OutlinerVisibility { get; }
        public static partial WidgetId OutlinerLock { get; }
        public static partial WidgetId OutlinerExpand { get; }
        public static partial WidgetId AddLayerButton { get; }
    }

    private float _outlinerSize = 180;
    private int _outlinerIndex;

    // Drag-drop state
    private struct OutlinerRowInfo
    {
        public SpriteNode Node;
        public int Index;
        public int Depth;
    }

    private readonly List<OutlinerRowInfo> _outlinerRows = new();
    private bool _outlinerDragging;
    private SpriteNode? _dragNode;
    private Vector2 _dragStartPos;
    private int _dropTargetIndex = -1;

    private enum DropZone { Before, After, FirstChild, LastChild }
    private DropZone _dropZone;
    private const float DragThreshold = 4f;

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

    private void OutlinerUI()
    {
        _outlinerIndex = 0;

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
        var isActive = isLayer && node == Document.ActiveLayer;
        var isPathSelected = isPath && ((SpritePath)node).IsSelected;
        var isDragTarget = _outlinerDragging && _dropTargetIndex == index;
        var isDragSource = _outlinerDragging && _dragNode == node;
        var dropBefore = isDragTarget && _dropZone == DropZone.Before;
        var dropAfter = isDragTarget && (_dropZone == DropZone.After || _dropZone == DropZone.FirstChild);
        var dropLastChild = isDragTarget && _dropZone == DropZone.LastChild;

        _outlinerRows.Add(new OutlinerRowInfo { Node = node, Index = index, Depth = depth });

        // Determine background
        var bg = (isActive || isPathSelected) ? EditorStyle.Palette.Active : Color.Transparent;
        var isHovered = !_outlinerDragging && UI.IsHovered(rowId);
        if (isHovered) bg = EditorStyle.Palette.Active;

        if (isDragSource)
            UI.BeginOpacity(0.35f);

        ElementTree.BeginTree();
        ElementTree.BeginWidget(rowId);
        ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);

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

        // name
        ElementTree.BeginFlex();
        ElementTree.Text(
            value: !string.IsNullOrEmpty(node.Name) ? node.Name : (isLayer ? "Group" : "Path"),
            font: UI.DefaultFont,
            fontSize: EditorStyle.Text.Size,
            color: EditorStyle.Palette.SecondaryText,
            align: new Align2(Align.Min, Align.Center)
        );
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
                Document.IncrementVersion();
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
                Document.IncrementVersion();
            }
        }

        if (UI.WasPressed(rowId))
        {
            HandleOutlinerClick(node);
            _dragNode = node;
            _dragStartPos = Input.MousePosition;
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
        if (_dragNode == null)
        {
            _outlinerDragging = false;
            _dropTargetIndex = -1;
            return;
        }

        var mousePos = Input.MousePosition;
        // Check if we should start dragging (mouse moved beyond threshold)
        if (!_outlinerDragging)
        {
            if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
            {
                _dragNode = null;
                return;
            }

            var dist = System.Numerics.Vector2.Distance(mousePos, _dragStartPos);
            if (dist < DragThreshold)
                return;

            _outlinerDragging = true;
        }

        // Cancel on escape or right-click
        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) || Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
        {
            _outlinerDragging = false;
            _dragNode = null;
            _dropTargetIndex = -1;
            return;
        }

        // Find drop target by checking row rects from previous frame
        _dropTargetIndex = -1;
        var mouseWorld = UI.MouseWorldPosition;
        var closestDist = float.MaxValue;
        var closestRow = -1;

        for (var i = 0; i < _outlinerRows.Count; i++)
        {
            var row = _outlinerRows[i];
            if (row.Node == _dragNode)
            {
                // Check if mouse is over the drag source — no drop target
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

            // Direct hit
            if (mouseWorld.Y >= rect.Y && mouseWorld.Y <= rect.Bottom)
            {
                _dropTargetIndex = row.Index;
                var relY = (mouseWorld.Y - rect.Y) / rect.Height;

                if (row.Node is SpriteLayer layer)
                {
                    if (layer.Expanded)
                    {
                        // Expanded: top=before sibling, mid=last child, bot=first child
                        if (relY < 0.33f) _dropZone = DropZone.Before;
                        else if (relY > 0.67f) _dropZone = DropZone.FirstChild;
                        else _dropZone = DropZone.LastChild;
                    }
                    else
                    {
                        // Collapsed: top=before sibling, mid=last child, bot=first child
                        if (relY < 0.33f) _dropZone = DropZone.Before;
                        else if (relY > 0.67f) _dropZone = DropZone.FirstChild;
                        else _dropZone = DropZone.LastChild;
                    }
                }
                else
                {
                    // Leaf: top=before, bot=after
                    _dropZone = relY < 0.5f ? DropZone.Before : DropZone.After;
                }

                // Normalize "Before" to keep the line on one consistent slot
                if (_dropZone == DropZone.Before && i > 0)
                {
                    // Find previous visible row (skip drag source)
                    var pi = i - 1;
                    while (pi >= 0 && _outlinerRows[pi].Node == _dragNode) pi--;

                    if (pi >= 0)
                    {
                        var prevRow = _outlinerRows[pi];
                        if (prevRow.Depth == row.Depth)
                        {
                            // Same-depth sibling: use "After" on prev row
                            _dropTargetIndex = prevRow.Index;
                            _dropZone = DropZone.After;
                        }
                        else if (prevRow.Depth == row.Depth - 1 && prevRow.Node is SpriteLayer parentLayer && parentLayer.Expanded)
                        {
                            // First child of expanded layer: use "FirstChild" on parent
                            _dropTargetIndex = prevRow.Index;
                            _dropZone = DropZone.FirstChild;
                        }
                    }
                }

                break;
            }

            // Track nearest row for fallback
            var dist = mouseWorld.Y < rect.Y ? rect.Y - mouseWorld.Y : mouseWorld.Y - rect.Bottom;
            if (dist < closestDist)
            {
                closestDist = dist;
                closestRow = i;
            }
        }

        // Fallback: mouse in gap or over drag source — snap to nearest
        if (_dropTargetIndex < 0 && closestRow >= 0)
        {
            var row = _outlinerRows[closestRow];
            var rect = UI.GetElementWorldRect(WidgetIds.OutlinerLayer + row.Index);

            if (mouseWorld.Y > rect.Bottom)
            {
                // Below last visible row — find last root-level item to insert after
                for (var j = _outlinerRows.Count - 1; j >= 0; j--)
                {
                    if (_outlinerRows[j].Depth == 0 && _outlinerRows[j].Node != _dragNode)
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

        // Drop on release
        if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
        {
            if (_dropTargetIndex >= 0)
                CommitOutlinerDrop();

            _outlinerDragging = false;
            _dragNode = null;
            _dropTargetIndex = -1;
        }
    }

    private void CommitOutlinerDrop()
    {
        if (_dragNode == null) return;

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

        // Cycle detection: don't drop a parent into its own descendant
        if (IsDescendant(_dragNode, targetNode))
            return;

        // Don't drop onto self
        if (_dragNode == targetNode)
            return;

        Undo.Record(Document);

        // Remove from current parent
        var dragParent = Document.RootLayer.FindParent(_dragNode);
        if (dragParent == null) return;
        dragParent.Children.Remove(_dragNode);

        switch (_dropZone)
        {
            case DropZone.LastChild when targetNode is SpriteLayer layer:
                layer.Children.Add(_dragNode);
                layer.Expanded = true;
                break;

            case DropZone.FirstChild when targetNode is SpriteLayer layer2:
                layer2.Children.Insert(0, _dragNode);
                layer2.Expanded = true;
                break;

            case DropZone.Before:
            case DropZone.After:
            {
                var targetParent = Document.RootLayer.FindParent(targetNode);
                if (targetParent == null) return;
                var targetIdx = targetParent.Children.IndexOf(targetNode);
                if (_dropZone == DropZone.After) targetIdx++;
                targetParent.Children.Insert(targetIdx, _dragNode);
                break;
            }
        }

        Document.IncrementVersion();
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
            // Layer click: set active layer, clear path selection
            if (!shift && !ctrl)
                Document.RootLayer.ClearAllSelections();
            Document.ActiveLayer = layer;
            RebuildSelectedPaths();
            return;
        }

        if (node is SpritePath path)
        {
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
                Document.RootLayer.ClearAllSelections();
                path.SelectPath();
            }

            // Set active layer from first selection (only if not already set or plain click)
            if (!shift && !ctrl)
            {
                var parent = Document.RootLayer.FindParent(node);
                if (parent is SpriteLayer parentLayer)
                    Document.ActiveLayer = parentLayer;
                else if (parent == Document.RootLayer)
                    Document.ActiveLayer = Document.RootLayer;
            }

            RebuildSelectedPaths();
        }
    }

    private void AddLayer()
    {
        Undo.Record(Document);
        var name = $"Layer {Document.RootLayer.Children.Count + 1}";
        var layer = new SpriteLayer { Name = name };
        Document.RootLayer.Children.Add(layer);
        Document.ActiveLayer = layer;
        Document.IncrementVersion();
    }
}
