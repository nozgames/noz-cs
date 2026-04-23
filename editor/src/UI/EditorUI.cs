//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private struct ColorButtonState
    {
        public Color OriginalColor;
    }

    private static void DrawColorButtonContent(Color color, bool alpha)
    {
        if (color.A == 0)
        {
            ElementTree.BeginPadding(4);
            ElementTree.Image(EditorAssets.Sprites.IconNofill);
            ElementTree.EndPadding();
            return;
        }

        var maxComponent = MathF.Max(color.R, MathF.Max(color.G, color.B));
        if (maxComponent > 1f)
        {
            var baseColor = new Color(color.R / maxComponent, color.G / maxComponent, color.B / maxComponent);
            var brightColor = new Color(
                MathF.Min(color.R, 1f),
                MathF.Min(color.G, 1f),
                MathF.Min(color.B, 1f));

            ElementTree.BeginRow();
            ElementTree.BeginFlex();
            ElementTree.Fill(new BackgroundStyle
            {
                Color = baseColor,
                GradientColor = brightColor,
                GradientAngle = 0
            }, BorderRadius.Only(topLeft:EditorStyle.Control.BorderRadius, bottomLeft: EditorStyle.Control.BorderRadius));
            ElementTree.EndFlex();

            ElementTree.BeginFlex();
            ElementTree.BeginMargin(EdgeInsets.Left(-1));
            ElementTree.Fill(new BackgroundStyle
            {
                Color = brightColor,
                GradientColor = baseColor,
                GradientAngle = 0
            }, BorderRadius.Only(bottomRight: EditorStyle.Control.BorderRadius, topRight: EditorStyle.Control.BorderRadius));
            ElementTree.EndMargin();
            ElementTree.EndFlex();
            ElementTree.EndRow();

            ElementTree.Fill(Color.Transparent, EditorStyle.Control.BorderRadius, 4, EditorStyle.Palette.Control);
        }
        else
        {
            ElementTree.Fill(color.WithAlpha(1f), EditorStyle.Control.BorderRadius, 4, EditorStyle.Palette.Control);
        }

        if (alpha)
        {
            ElementTree.BeginPadding(3.9f);
            ElementTree.BeginAlign(Align.Min, Align.Max);

            ElementTree.BeginSize(Size.Default, 4);
            ElementTree.Fill(Color.Black);
            ElementTree.EndSize();

            ElementTree.BeginSize(Size.Percent(color.A), 4);
            ElementTree.Fill(Color.White);
            ElementTree.EndSize();

            ElementTree.EndAlign();
            ElementTree.EndPadding();
        }
    }

    public static Color ColorButton(
        WidgetId id,
        Color color,                
        ColorButtonStyle? style = null)
    {
        var resolved = style ??= new ColorButtonStyle();

        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<ColorButtonState>(id);
        var flags = ElementTree.GetWidgetFlags();

        if (resolved.FillWidth)
            ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);
        else
            ElementTree.BeginSize(EditorStyle.Control.Height);

        DrawColorButtonContent(color, resolved.ShowAlpha);
        ElementTree.EndTree();

        if (flags.HasFlag(WidgetFlags.Pressed))
        {
            state.OriginalColor = color;
            UI.SetHot(id, color);
            ColorPicker.Open(id, color, style);
        }

        ElementTree.SetLastWidget(id);

        var prev = color;
        ColorPicker.Popup(id, ref color);

        if (color != prev)
            UI.NotifyChanged(color);

        if (!ColorPicker.IsOpen(id) && UI.IsHot())
            UI.ClearHot();

        return color;
    }

    public static void PanelSeparator()
    {
        if (UI.IsRow())
            UI.Container(new ContainerStyle { Width = 1, Background = EditorStyle.Palette.Separator });
        else
            UI.Container(new ContainerStyle { Height = 1, Background = EditorStyle.Palette.Separator });
    }

    public static void SortOrderDropDown(WidgetId id, string? sortOrderId, Action<string?> onChanged)
    {
        var config = EditorApplication.Config;
        var label = "None";
        if (config != null && !string.IsNullOrEmpty(sortOrderId) && config.TryGetSortOrder(sortOrderId, out var current))
            label = current.Label;

        UI.DropDown(id, () =>
        {
            var items = new List<PopupMenuItem>();
            if (config != null)
            {
                foreach (var def in config.SortOrders)
                {
                    var defId = def.Id;
                    items.Add(new PopupMenuItem { Label = $"{def.Label} {def.SortOrderLabel}", Handler = () => onChanged(defId) });
                }
            }
            items.Add(new PopupMenuItem { Label = "None", Handler = () => onChanged(null) });
            return [.. items];
        }, label);
    }
}
