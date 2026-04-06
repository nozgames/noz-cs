//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId OutlinerPanel { get; }
        public static partial WidgetId OutlinerLayer { get; }
        public static partial WidgetId OutlinerVisibility { get; }
        public static partial WidgetId OutlinerLock { get; }
        public static partial WidgetId OutlinerExpand { get; }
        public static partial WidgetId OutlinerRename { get; }
        public static partial WidgetId AddLayerButton { get; }
        public static partial WidgetId AddGroupButton { get; }
        public static partial WidgetId DeleteLayerButton { get; }
    }

    private int _outlinerIndex;
    private SpriteNode? _renameNode;
    private string _renameText = "";

    private static readonly ContainerStyle OutlinerPanelStyle = EditorStyle.Panel with
    {
        Width = Size.Percent(1),
        Height = Size.Percent(1),
        Padding = EdgeInsets.All(4),
        Spacing = 0,
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

            // Layer tree (bottom-up so topmost layer appears first)
            for (var i = Document.RootLayer.Children.Count - 1; i >= 0; i--)
            {
                if (Document.RootLayer.Children[i] is SpriteLayer layer)
                    OutlinerNodeUI(layer, 0);
            }
        }
    }

    private void OutlinerNodeUI(SpriteLayer layer, int depth)
    {
        var index = _outlinerIndex++;
        var rowId = WidgetIds.OutlinerLayer + index;
        var isGroup = layer.Pixels == null;
        var isActive = !isGroup && layer == ActiveLayer;

        ElementTree.BeginTree();
        ElementTree.BeginWidget(rowId);
        ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);

        // Must be after BeginWidget so hover state is available
        var isHovered = UI.IsHovered(rowId);
        var bg = isActive ? EditorStyle.Palette.Active : Color.Transparent;
        if (isHovered) bg = EditorStyle.Palette.Active;

        ElementTree.BeginFill(bg);
        ElementTree.BeginPadding(EditorStyle.Item.Padding);
        ElementTree.BeginRow(EditorStyle.Control.Spacing);

        // Indentation
        if (depth > 0)
            ElementTree.Spacer(depth * (EditorStyle.Icon.SmallSize + EditorStyle.Control.Spacing) - EditorStyle.Control.Spacing);

        // Expand/collapse chevron
        if (isGroup && layer.Children.Count > 0)
        {
            ElementTree.BeginTree();
            ElementTree.BeginWidget(WidgetIds.OutlinerExpand + index);
            ElementTree.BeginSize(EditorStyle.Icon.SmallSize, Size.Default);
            ElementTree.Image(
                image: layer.Expanded
                    ? EditorAssets.Sprites.IconFoldoutClosed
                    : EditorAssets.Sprites.IconFoldoutOpen,
                size: new Size2(EditorStyle.Icon.SmallSize, Size.Default),
                align: Align.Center,
                color: EditorStyle.Palette.SecondaryText);

            if (ElementTree.WasPressed())
                layer.Expanded = !layer.Expanded;

            ElementTree.EndTree();
        }
        else if (isGroup)
        {
            UI.Spacer(EditorStyle.Icon.SmallSize);
        }

        // Icon
        ElementTree.Image(
            image: isGroup
                ? EditorAssets.Sprites.IconPathLayer
                : EditorAssets.Sprites.IconPath,
            size: new Size2(EditorStyle.Icon.Size, Size.Default),
            align: Align.Center,
            color: EditorStyle.Palette.SecondaryText);

        // Name (inline rename or static text)
        ElementTree.BeginFlex();
        if (layer == _renameNode)
        {
            ElementTree.BeginMargin(EdgeInsets.TopLeft(2, -2));
            _renameText = UI.TextInput(WidgetIds.OutlinerRename, _renameText, EditorStyle.SpriteEditor.OutlinerRename);
            ElementTree.EndMargin();

            if (UI.HotExit())
                CommitRename();
        }
        else
        {
            var displayName = !string.IsNullOrEmpty(layer.Name) ? layer.Name : (isGroup ? "Group" : "Layer");
            ElementTree.Text(
                value: displayName,
                font: UI.DefaultFont,
                fontSize: EditorStyle.Text.Size,
                color: EditorStyle.Palette.SecondaryText,
                align: new Align2(Align.Min, Align.Center)
            );
        }
        ElementTree.EndFlex();

        // Visibility toggle (show on hover or when hidden)
        var showVisibility = isHovered || !layer.Visible;
        if (showVisibility)
        {
            var visIcon = layer.Visible ? EditorAssets.Sprites.IconPreview : EditorAssets.Sprites.IconHidden;
            var visStyle = layer.Visible ? OutlinerIconDimButtonStyle : OutlinerIconButtonStyle;
            if (UI.Button(WidgetIds.OutlinerVisibility + index, visIcon, visStyle))
            {
                Undo.Record(Document);
                layer.Visible = !layer.Visible;
                InvalidateComposite();
            }
        }

        // Lock toggle (show on hover or when locked)
        var showLock = isHovered || layer.Locked;
        if (showLock)
        {
            var lockIcon = layer.Locked ? EditorAssets.Sprites.IconLock : EditorAssets.Sprites.IconUnlock;
            var lockStyle = layer.Locked ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(WidgetIds.OutlinerLock + index, lockIcon, lockStyle))
            {
                Undo.Record(Document);
                layer.Locked = !layer.Locked;
            }
        }
        else
        {
            UI.Spacer(EditorStyle.Icon.Size);
        }

        // End Row + Padding + Fill + Size + Widget + Tree
        ElementTree.EndRow();
        ElementTree.EndPadding();
        ElementTree.EndFill();
        ElementTree.EndTree();

        // Click handling
        if (UI.WasPressed(rowId))
        {
            if (isGroup)
                layer.Expanded = !layer.Expanded;
            else
                ActiveLayer = layer;
        }

        // Recurse into children when expanded
        if (isGroup && layer.Expanded)
        {
            for (var i = layer.Children.Count - 1; i >= 0; i--)
            {
                if (layer.Children[i] is SpriteLayer child)
                    OutlinerNodeUI(child, depth + 1);
            }
        }
    }

    #region Rename

    private void BeginRename()
    {
        if (ActiveLayer == null) return;
        _renameNode = ActiveLayer;
        _renameText = ActiveLayer.Name ?? "";
        UI.SetHot(WidgetIds.OutlinerRename);
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

    #endregion
}
