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

    private enum DropZone { Before, Into, After }
    private DropZone _dropZone;
    private const float DragThreshold = 4f;

    private static readonly ContainerStyle OutlinerPanelStyle = EditorStyle.Panel with
    {
        Width = Size.Percent(1),
        Height = Size.Percent(1),
        Padding = EdgeInsets.All(4),
        Spacing = 1,
    };

    private static readonly ContainerStyle OutlinerLayerRowStyle = new()
    {
        Height = 22,
        Spacing = 4,
        Padding = EdgeInsets.LeftRight(4),
    };

    private static readonly ContainerStyle OutlinerActiveLayerRowStyle = OutlinerLayerRowStyle with
    {
        Background = EditorStyle.Palette.Active,
        BorderRadius = 3,
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
        Width = 18,
        Height = 18,
        Background = Color.Transparent,
        ContentColor = EditorStyle.Palette.Content,
        IconSize = 9,
        BorderRadius = 2,
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
                UI.Text("Layers", new TextStyle
                {
                    FontSize = 10,
                    Color = EditorStyle.Palette.SecondaryText,
                    AlignY = Align.Center,
                });
                using (UI.BeginFlex()) { }
                if (UI.Button(WidgetIds.AddLayerButton, EditorAssets.Sprites.IconAdd, OutlinerIconButtonStyle))
                    AddLayer();
            }

            // Node tree
            foreach (var child in Document.RootLayer.Children)
                OutlinerNodeUI(child, 0);
        }
    }

    private void OutlinerNodeUI(SpriteNode node, int depth)
    {
        var index = _outlinerIndex++;
        var rowId = WidgetIds.OutlinerLayer + index;
        var isLayer = node is SpriteLayer;
        var isPath = node is SpritePath;
        var isActive = isLayer && node == Document.ActiveLayer;
        var isPathSelected = isPath && ((SpritePath)node).HasSelection();
        var isDragTarget = _outlinerDragging && _dropTargetIndex == index;

        // Highlight: active layer, selected path, or drop-into target
        var rowStyle = OutlinerLayerRowStyle;
        if (isDragTarget && _dropZone == DropZone.Into)
            rowStyle = OutlinerActiveLayerRowStyle;
        else if (isActive || isPathSelected)
            rowStyle = OutlinerActiveLayerRowStyle;

        _outlinerRows.Add(new OutlinerRowInfo { Node = node, Index = index, Depth = depth });

        using (UI.BeginRow(rowId, rowStyle))
        {
            // Indent
            if (depth > 0)
                UI.Spacer(depth * 14);

            // Expand/collapse (layers with children only)
            if (isLayer && node.Children.Count > 0)
            {
                var expandIcon = node.Expanded
                    ? EditorAssets.Sprites.IconFoldoutOpen
                    : EditorAssets.Sprites.IconFoldoutClosed;
                if (UI.Button(WidgetIds.OutlinerExpand + index, expandIcon, OutlinerIconButtonStyle))
                    node.Expanded = !node.Expanded;
            }
            else
            {
                UI.Spacer(18);
            }

            // Type icon (folder for layers, edit for paths)
            var typeIcon = isLayer ? EditorAssets.Sprites.IconLayer : EditorAssets.Sprites.IconEdit;
            var iconColor = node.Visible ? EditorStyle.Palette.SecondaryText : EditorStyle.Palette.SecondaryText.WithAlpha(0.4f);
            UI.Image(typeIcon, new ImageStyle { Size = new Size2(9, 9), Color = iconColor });

            // Node name (fills remaining space)
            using (UI.BeginFlex())
            {
                var displayName = !string.IsNullOrEmpty(node.Name) ? node.Name : (isLayer ? "Group" : "Path");
                var nameStyle = node.Visible ? OutlinerLayerNameStyle : OutlinerLayerNameDimStyle;
                UI.Text(displayName, nameStyle);
            }

            // Visibility + Lock — right-aligned, shown on hover or when non-default
            var isHovered = UI.IsHovered(rowId);
            var showVisibility = isHovered || !node.Visible;
            var showLock = isHovered || node.Locked;

            if (showVisibility)
            {
                var visIcon = node.Visible
                    ? EditorAssets.Sprites.IconPreview
                    : EditorAssets.Sprites.IconHidden;
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
                var lockIcon = node.Locked
                    ? EditorAssets.Sprites.IconLock
                    : EditorAssets.Sprites.IconUnlock;
                var lockStyle = node.Locked ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
                if (UI.Button(WidgetIds.OutlinerLock + index, lockIcon, lockStyle))
                {
                    Undo.Record(Document);
                    node.Locked = !node.Locked;
                    Document.IncrementVersion();
                }
            }

            // Click to select / start drag
            if (UI.WasPressed(rowId))
            {
                HandleOutlinerClick(node);
                _dragNode = node;
                _dragStartPos = Input.MousePosition;
            }

            // Draw drop indicator line (before/after)
            if (isDragTarget && _dropZone != DropZone.Into)
            {
                var rect = UI.GetElementWorldRect(rowId);
                if (rect.Width > 0)
                {
                    var y = _dropZone == DropZone.Before ? rect.Y : rect.Bottom;
                    using (Gizmos.PushState(EditorLayer.Tool))
                    {
                        Gizmos.SetColor(EditorStyle.Palette.Primary);
                        Gizmos.DrawLine(
                            new System.Numerics.Vector2(rect.X + depth * 14, y),
                            new System.Numerics.Vector2(rect.Right, y),
                            0.02f, order: 10);
                    }
                }
            }
        }

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
            if (!Input.IsButtonDown(InputCode.MouseLeft))
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
        if (Input.WasButtonPressed(InputCode.KeyEscape) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            _outlinerDragging = false;
            _dragNode = null;
            _dropTargetIndex = -1;
            return;
        }

        // Find drop target by checking row rects from previous frame
        _dropTargetIndex = -1;
        var mouseWorld = UI.MouseWorldPosition;

        for (var i = 0; i < _outlinerRows.Count; i++)
        {
            var row = _outlinerRows[i];
            if (row.Node == _dragNode) continue;

            var rect = UI.GetElementWorldRect(WidgetIds.OutlinerLayer + row.Index);
            if (rect.Width <= 0) continue;

            if (mouseWorld.Y < rect.Y || mouseWorld.Y > rect.Bottom)
                continue;

            _dropTargetIndex = row.Index;

            var relY = (mouseWorld.Y - rect.Y) / rect.Height;
            if (row.Node is SpriteLayer)
            {
                if (relY < 0.25f) _dropZone = DropZone.Before;
                else if (relY > 0.75f) _dropZone = DropZone.After;
                else _dropZone = DropZone.Into;
            }
            else
            {
                _dropZone = relY < 0.5f ? DropZone.Before : DropZone.After;
            }
            break;
        }

        // Drop on release
        if (!Input.IsButtonDown(InputCode.MouseLeft))
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

        if (_dropZone == DropZone.Into && targetNode is SpriteLayer targetLayer)
        {
            // Drop into layer as child
            targetLayer.Children.Add(_dragNode);
            targetLayer.Expanded = true;
        }
        else
        {
            // Drop before/after target
            var targetParent = Document.RootLayer.FindParent(targetNode);
            if (targetParent == null) return;

            var targetIdx = targetParent.Children.IndexOf(targetNode);
            if (_dropZone == DropZone.After)
                targetIdx++;

            targetParent.Children.Insert(targetIdx, _dragNode);
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
                ClearAllSelections();
            Document.ActiveLayer = layer;
            UpdateSelection();
            return;
        }

        if (node is SpritePath path)
        {
            if (ctrl)
            {
                // Ctrl+click: toggle this path's selection
                if (path.HasSelection())
                    path.ClearSelection();
                else
                    path.SelectAll();
            }
            else if (shift)
            {
                // Shift+click: add to selection
                path.SelectAll();
            }
            else
            {
                // Plain click: clear all, select this path
                ClearAllSelections();
                path.SelectAll();
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

            UpdateSelection();
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
