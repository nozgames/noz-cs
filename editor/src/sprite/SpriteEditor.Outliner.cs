//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

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

    private static readonly ContainerStyle OutlinerPanelStyle = EditorStyle.Panel with
    {
        Padding = EdgeInsets.All(4),
        Spacing = 1,
    };

    private static readonly ContainerStyle OutlinerLayerRowStyle = new()
    {
        Height = 22,
        Spacing = 2,
        AlignY = Align.Center,
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
                DrawNodeRow(child, 0);
        }
    }

    private void DrawNodeRow(SpriteNode node, int depth)
    {
        var index = _outlinerIndex++;
        var isLayer = node is SpriteLayer;
        var isActive = isLayer && node == Document.ActiveLayer;
        var rowStyle = isActive ? OutlinerActiveLayerRowStyle : OutlinerLayerRowStyle;

        using (UI.BeginRow(WidgetIds.OutlinerLayer + index, rowStyle))
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

            // Visibility toggle
            var visIcon = node.Visible
                ? EditorAssets.Sprites.IconPreview
                : EditorAssets.Sprites.IconHidden;
            var visStyle = node.Visible ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(WidgetIds.OutlinerVisibility + index, visIcon, visStyle))
            {
                Undo.Record(Document);
                node.Visible = !node.Visible;
                Document.IncrementVersion();
            }

            // Lock toggle
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

            // Icon (folder for layers, edit for paths)
            var typeIcon = isLayer ? EditorAssets.Sprites.IconLayer : EditorAssets.Sprites.IconEdit;
            UI.Image(typeIcon, new ImageStyle { Size = new Size2(9, 9), Color = EditorStyle.Palette.SecondaryText });

            // Node name
            using (UI.BeginFlex())
            {
                var displayName = !string.IsNullOrEmpty(node.Name) ? node.Name : (isLayer ? "Group" : "Path");
                var nameStyle = node.Visible ? OutlinerLayerNameStyle : OutlinerLayerNameDimStyle;
                UI.Text(displayName, nameStyle);
            }

            // Click to select
            if (UI.WasPressed(WidgetIds.OutlinerLayer + index))
            {
                if (node is SpriteLayer layer)
                    Document.ActiveLayer = layer;
                else
                {
                    // Clicking a path activates its parent layer
                    var parent = Document.RootLayer.FindParent(node);
                    if (parent is SpriteLayer parentLayer)
                        Document.ActiveLayer = parentLayer;
                }
            }
        }

        // Draw children if expanded (layers only)
        if (isLayer && node.Expanded)
        {
            foreach (var child in node.Children)
                DrawNodeRow(child, depth + 1);
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
