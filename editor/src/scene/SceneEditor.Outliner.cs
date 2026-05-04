//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SceneEditor
{
    private const float DragThreshold = 4f;
    private const float DropLineHeight = 2;

    private struct OutlinerRowInfo
    {
        public SceneNode Node;
        public int Index;
        public int Depth;
    }

    private enum DropZone { Before, After, FirstChild, LastChild }

    private static partial class OutlinerWidgetIds
    {
        public static partial WidgetId OutlinerLayer { get; }
        public static partial WidgetId OutlinerVisibility { get; }
        public static partial WidgetId OutlinerLock { get; }
        public static partial WidgetId OutlinerExpand { get; }
        public static partial WidgetId OutlinerRename { get; }
    }

    protected static readonly ButtonStyle OutlinerIconButtonStyle = new()
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

    protected static readonly ButtonStyle OutlinerIconDimButtonStyle = OutlinerIconButtonStyle with
    {
        ContentColor = EditorStyle.Palette.SecondaryText,
    };

    private int _outlinerIndex;
    private SceneNode? _renameNode;
    private string _renameText = "";
    private readonly List<OutlinerRowInfo> _outlinerRows = [];
    private bool _outlinerDragging;
    private readonly List<SceneNode> _dragNodes = [];
    private SceneNode? _deferredClickNode;
    private Vector2 _dragStartPos;
    private int _dropTargetIndex = -1;
    private DropZone _dropZone;

    public override void OutlinerUI()
    {
        _outlinerIndex = 0;

        void AddButton()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(WidgetIds.AddButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
            {
                AssetPalette.OpenSprites(onPicked: doc =>
                {
                    if (doc is SpriteDocument sd)
                        AddSpriteInstance(sd);
                });
            }
            ElementTree.EndAlign();
        }

        using (Outliner.BeginSection("SCENE", content: AddButton, collapsible: false))
        {
            DrawNodeTree(Document.Root);
        }

        UpdateOutlinerDrag();
        HandleRenameInput();
    }

    private void AddSpriteInstance(SpriteDocument spriteDoc)
    {
        Undo.Record(Document);

        var node = new SceneSprite
        {
            Name = spriteDoc.Name,
            Sprite = spriteDoc,
        };

        // Insert into the most recently selected group, or root
        SceneGroup parent = Document.Root;
        if (_selectedNodes.Count == 1 && _selectedNodes[0] is SceneGroup g)
            parent = g;

        parent.Add(node);
        parent.Expanded = true;

        Document.Root.ClearSelection();
        node.IsSelected = true;
        node.ExpandAncestors();
        RebuildSelection();
    }

    private void DrawNodeTree(SceneNode root)
    {
        _outlinerRows.Clear();

        ElementTree.BeginColumn();

        foreach (var child in root.Children)
            OutlinerNodeUI(child, 0);

        ElementTree.EndColumn();
    }

    private void OutlinerNodeUI(SceneNode node, int depth)
    {
        var index = _outlinerIndex++;
        var rowId = OutlinerWidgetIds.OutlinerLayer + index;
        var isExpandable = node.IsExpandable;
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

        var isSelected = node.IsSelected;
        var isHovered = !_outlinerDragging && UI.IsHovered(rowId);
        var bg = isSelected || isHovered ? EditorStyle.Palette.Active : Color.Transparent;

        if (dropLastChild)
            ElementTree.BeginFill(bg, borderWidth: 1, borderColor: EditorStyle.Palette.Primary);
        else
            ElementTree.BeginFill(bg);

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

        if (depth > 0)
            ElementTree.Spacer(depth * (EditorStyle.Icon.SmallSize + EditorStyle.Control.Spacing) - EditorStyle.Control.Spacing);

        if (isExpandable && node.Children.Count > 0)
        {
            ElementTree.BeginTree();
            ElementTree.BeginWidget(OutlinerWidgetIds.OutlinerExpand + index);
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

        // Icon: folder for groups, sprite preview for sprites
        Sprite? preview = null;
        if (node is SceneSprite ss && ss.Sprite.Value is { } doc)
            preview = doc.Sprite;

        if (preview != null)
        {
            var previewSize = EditorStyle.Control.Height - 2;
            ElementTree.Image(
                image: preview,
                size: new Size2(previewSize, previewSize),
                stretch: ImageStretch.Uniform,
                align: new Align2(Align.Center, Align.Center));
        }
        else
        {
            ElementTree.Image(
                image: node is SceneGroup ? EditorAssets.Sprites.IconFolder : EditorAssets.Sprites.IconPath,
                size: new Size2(EditorStyle.Icon.Size, Size.Default),
                align: Align.Center,
                color: EditorStyle.Palette.SecondaryText);
        }

        ElementTree.BeginFlex();
        if (node == _renameNode)
        {
            ElementTree.BeginMargin(EdgeInsets.TopLeft(2, -2));
            _renameText = UI.TextInput(OutlinerWidgetIds.OutlinerRename, _renameText, EditorStyle.SpriteEditor.OutlinerRename);
            ElementTree.EndMargin();

            if (UI.HotExit())
                CommitRename();
        }
        else
        {
            var displayName = !string.IsNullOrEmpty(node.Name) ? node.Name : (node is SceneGroup ? "Group" : "Sprite");
            ElementTree.Text(
                value: displayName,
                font: UI.DefaultFont,
                fontSize: EditorStyle.Text.Size,
                color: EditorStyle.Palette.SecondaryText,
                align: new Align2(Align.Min, Align.Center)
            );
        }
        ElementTree.EndFlex();

        var showVisibility = isHovered || !node.Visible;
        if (showVisibility)
        {
            var visIcon = node.Visible ? EditorAssets.Sprites.IconPreview : EditorAssets.Sprites.IconHidden;
            var visStyle = node.Visible ? OutlinerIconDimButtonStyle : OutlinerIconButtonStyle;
            if (UI.Button(OutlinerWidgetIds.OutlinerVisibility + index, visIcon, visStyle))
            {
                Undo.Record(Document);
                node.Visible = !node.Visible;
            }
        }

        var showLock = isHovered || node.Locked;
        if (showLock)
        {
            var lockIcon = node.Locked ? EditorAssets.Sprites.IconLock : EditorAssets.Sprites.IconUnlock;
            var lockStyle = node.Locked ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(OutlinerWidgetIds.OutlinerLock + index, lockIcon, lockStyle))
            {
                Undo.Record(Document);
                node.Locked = !node.Locked;
            }
        }
        else
        {
            UI.Spacer(EditorStyle.Icon.Size);
        }

        if (UI.WasPressed(rowId))
        {
            var shift = Input.IsShiftDown(InputScope.All);
            var ctrl = Input.IsCtrlDown(InputScope.All);

            if (node.IsSelected && !shift && !ctrl)
            {
                _deferredClickNode = node;
            }
            else
            {
                if (shift || ctrl)
                    ToggleSelected(node);
                else
                    SelectOnly(node);
                _deferredClickNode = null;
            }

            _dragNodes.Clear();
            _dragNodes.Add(node);
            _dragStartPos = Input.MousePosition;
        }

        ElementTree.EndRow();
        ElementTree.EndPadding();
        ElementTree.EndFill();
        ElementTree.EndTree();

        if (isDragSource)
            UI.EndOpacity();

        if (isExpandable && node.Expanded)
        {
            foreach (var child in node.Children)
                OutlinerNodeUI(child, depth + 1);
        }
    }

    protected void HandleRenameInput()
    {
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
    }

    internal void BeginRename(SceneNode node)
    {
        _renameNode = node;
        _renameText = node.Name ?? "";
        UI.SetHot(OutlinerWidgetIds.OutlinerRename);
    }

    private void CommitRename()
    {
        if (_renameNode != null && !string.IsNullOrWhiteSpace(_renameText) && _renameText != _renameNode.Name)
        {
            Undo.Record(Document);
            _renameNode.Name = _renameText;
        }
        _renameNode = null;
    }

    private void CancelRename()
    {
        _renameNode = null;
    }

    protected void UpdateOutlinerDrag()
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
            if (_deferredClickNode != null)
            {
                SelectOnly(_deferredClickNode);
                _deferredClickNode = null;
            }
            _dragNodes.Clear();
            return false;
        }

        var dist = Vector2.Distance(Input.MousePosition, _dragStartPos);
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
        var firstIdx = -1;
        var lastIdx = -1;

        for (var i = 0; i < _outlinerRows.Count; i++)
        {
            var row = _outlinerRows[i];
            var rect = UI.GetElementWorldRect(OutlinerWidgetIds.OutlinerLayer + row.Index);
            if (rect.Width <= 0) continue;

            if (_dragNodes.Contains(row.Node))
            {
                if (mouseWorld.Y >= rect.Y && mouseWorld.Y < rect.Bottom)
                {
                    _dropTargetIndex = row.Index;
                    _dropZone = DropZone.Before;
                    NormalizeDropBefore(i, row);
                    return;
                }
                continue;
            }

            if (firstIdx < 0) firstIdx = i;
            lastIdx = i;

            if (mouseWorld.Y >= rect.Y && mouseWorld.Y < rect.Bottom)
            {
                _dropTargetIndex = row.Index;
                var relY = (mouseWorld.Y - rect.Y) / rect.Height;

                if (row.Node.IsExpandable)
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
                return;
            }
        }

        if (firstIdx < 0) return;
        var firstRect = UI.GetElementWorldRect(OutlinerWidgetIds.OutlinerLayer + _outlinerRows[firstIdx].Index);
        if (mouseWorld.Y < firstRect.Y)
        {
            var row = _outlinerRows[firstIdx];
            _dropTargetIndex = row.Index;
            _dropZone = DropZone.Before;
            NormalizeDropBefore(firstIdx, row);
        }
        else
        {
            var row = _outlinerRows[lastIdx];
            _dropTargetIndex = row.Index;
            _dropZone = row.Node.IsExpandable ? DropZone.LastChild : DropZone.After;
        }
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
        else if (prevRow.Depth == row.Depth - 1 && prevRow.Node.IsExpandable && prevRow.Node.Expanded)
        {
            _dropTargetIndex = prevRow.Index;
            _dropZone = DropZone.FirstChild;
        }
    }

    private void CommitOutlinerDrop()
    {
        if (_dragNodes.Count == 0) return;

        SceneNode? targetNode = null;
        foreach (var row in _outlinerRows)
        {
            if (row.Index == _dropTargetIndex)
            {
                targetNode = row.Node;
                break;
            }
        }
        if (targetNode == null) return;

        foreach (var dragNode in _dragNodes)
        {
            if (dragNode == targetNode) return;
            if (IsDescendant(dragNode, targetNode)) return;
        }

        var ordered = new List<SceneNode>(_dragNodes.Count);
        foreach (var row in _outlinerRows)
        {
            if (_dragNodes.Contains(row.Node))
                ordered.Add(row.Node);
        }

        Undo.Record(Document);

        foreach (var node in ordered)
            node.RemoveFromParent();

        switch (_dropZone)
        {
            case DropZone.LastChild when targetNode.IsExpandable:
                foreach (var node in ordered)
                    targetNode.Add(node);
                targetNode.Expanded = true;
                break;

            case DropZone.FirstChild when targetNode.IsExpandable:
            {
                var insertIdx = 0;
                foreach (var node in ordered)
                    targetNode.Insert(insertIdx++, node);
                targetNode.Expanded = true;
                break;
            }

            case DropZone.Before:
            case DropZone.After:
            {
                var targetParent = targetNode.Parent;
                if (targetParent == null) return;
                var targetIdx = targetParent.Children.IndexOf(targetNode);
                var after = _dropZone == DropZone.After;
                if (after) targetIdx++;
                for (var i = 0; i < ordered.Count; i++)
                    targetParent.Insert(targetIdx + i, ordered[i]);
                break;
            }
        }

        RebuildSelection();
    }

    private static bool IsDescendant(SceneNode parent, SceneNode candidate)
    {
        foreach (var child in parent.Children)
        {
            if (child == candidate || IsDescendant(child, candidate))
                return true;
        }
        return false;
    }
}
