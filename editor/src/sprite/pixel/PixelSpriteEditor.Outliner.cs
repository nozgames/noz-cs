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
        public static partial WidgetId AddLayerButton { get; }
    }

    private int _outlinerIndex;

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

        using (UI.BeginColumn(WidgetIds.OutlinerPanel, OutlinerPanelStyle))
        {
            // Header
            using (UI.BeginRow(new ContainerStyle { Height = 22, Spacing = 4, AlignY = Align.Center }))
            {
                UI.Text("Layers", EditorStyle.Text.Secondary);
                using (UI.BeginFlex()) { }
                if (UI.Button(WidgetIds.AddLayerButton, EditorAssets.Sprites.IconAdd, OutlinerIconButtonStyle))
                    AddLayer();
            }

            // Layer rows (bottom-up so topmost layer appears first)
            for (var i = Document.RootLayer.Children.Count - 1; i >= 0; i--)
            {
                if (Document.RootLayer.Children[i] is SpriteLayer layer)
                    OutlinerLayerUI(layer);
            }
        }
    }

    private void OutlinerLayerUI(SpriteLayer layer)
    {
        var index = _outlinerIndex++;
        var rowId = WidgetIds.OutlinerLayer + index;
        var isActive = layer == ActiveLayer;

        var rowStyle = new ContainerStyle
        {
            Height = EditorStyle.Control.Height,
            Spacing = 2,
            AlignY = Align.Center,
            Padding = EdgeInsets.LeftRight(4),
            Background = isActive ? EditorStyle.Palette.Active : Color.Transparent,
        };

        using (UI.BeginRow(rowId, rowStyle))
        {
            // Visibility toggle
            var visId = WidgetIds.OutlinerVisibility + index;
            var visIcon = layer.Visible ? EditorAssets.Sprites.IconPreview : EditorAssets.Sprites.IconHidden;
            var visStyle = layer.Visible ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(visId, visIcon, visStyle))
            {
                Undo.Record(Document);
                layer.Visible = !layer.Visible;
                InvalidateComposite();
            }

            // Lock toggle
            var lockId = WidgetIds.OutlinerLock + index;
            var lockIcon = layer.Locked ? EditorAssets.Sprites.IconLock : EditorAssets.Sprites.IconUnlock;
            var lockStyle = layer.Locked ? OutlinerIconButtonStyle : OutlinerIconDimButtonStyle;
            if (UI.Button(lockId, lockIcon, lockStyle))
            {
                Undo.Record(Document);
                layer.Locked = !layer.Locked;
            }

            // Layer name
            UI.Text(layer.Name, OutlinerLayerNameStyle);

            using (UI.BeginFlex()) { }
        }

        // Select on click
        if (UI.WasPressed(rowId))
        {
            ActiveLayer = layer;
        }
    }
}
