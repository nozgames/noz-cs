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

            // Layer tree
            foreach (var child in Document.RootLayer.Children)
                DrawLayerRow(child, 0);
        }
    }

    private void DrawLayerRow(SpriteLayer layer, int depth)
    {
        var index = _outlinerIndex++;
        var isActive = layer == Document.ActiveLayer;
        var rowStyle = isActive ? OutlinerActiveLayerRowStyle : OutlinerLayerRowStyle;

        using (UI.BeginRow(WidgetIds.OutlinerLayer + index, rowStyle))
        {
            // Indent
            if (depth > 0)
                UI.Spacer(depth * 14);

            // Expand/collapse
            if (layer.Children.Count > 0)
            {
                var expandIcon = layer.Expanded
                    ? EditorAssets.Sprites.IconFoldoutOpen
                    : EditorAssets.Sprites.IconFoldoutClosed;
                if (UI.Button(WidgetIds.OutlinerExpand + index, expandIcon, OutlinerIconButtonStyle))
                    layer.Expanded = !layer.Expanded;
            }
            else
            {
                UI.Spacer(18);
            }

            // Visibility toggle
            var visIcon = layer.Visible
                ? EditorAssets.Sprites.IconPreview
                : EditorAssets.Sprites.IconHidden;
            var visStyle = layer.Visible ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(WidgetIds.OutlinerVisibility + index, visIcon, visStyle))
            {
                Undo.Record(Document);
                layer.Visible = !layer.Visible;
                Document.IncrementVersion();
            }

            // Lock toggle
            var lockIcon = layer.Locked
                ? EditorAssets.Sprites.IconLock
                : EditorAssets.Sprites.IconUnlock;
            var lockStyle = layer.Locked ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(WidgetIds.OutlinerLock + index, lockIcon, lockStyle))
            {
                Undo.Record(Document);
                layer.Locked = !layer.Locked;
                Document.IncrementVersion();
            }

            // Layer name
            using (UI.BeginFlex())
            {
                var nameStyle = layer.Visible ? OutlinerLayerNameStyle : OutlinerLayerNameDimStyle;
                UI.Text(layer.Name, nameStyle);
            }

            // Click to select layer
            if (UI.WasPressed(WidgetIds.OutlinerLayer + index))
                Document.ActiveLayer = layer;
        }

        // Draw children if expanded
        if (layer.Expanded)
        {
            foreach (var child in layer.Children)
                DrawLayerRow(child, depth + 1);
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
