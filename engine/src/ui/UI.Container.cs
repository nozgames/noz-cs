//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct ContainerStyle()
{
    public Size2 Size = new(NoZ.Size.Default, NoZ.Size.Default);
    public float MinWidth = 0;
    public float MinHeight = 0;
    public float MaxWidth = float.MaxValue;
    public float MaxHeight = float.MaxValue;
    public Align2 Align = NoZ.Align.Min;
    public EdgeInsets Margin = EdgeInsets.Zero;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public BackgroundStyle Background = Color.Transparent;
    public BorderRadius BorderRadius = BorderRadius.Zero;
    public float BorderWidth;
    public Color BorderColor = Color.Transparent;
    public float Spacing = 0;
    public bool Clip = false;
    public ushort Order = 0;
    public Func<ContainerStyle, WidgetFlags, ContainerStyle>? Resolve;

    public Size Width { readonly get => Size.Width; set => Size.Width = value; }
    public Size Height { readonly get => Size.Height; set => Size.Height = value; }
    public Align AlignX { readonly get => Align.X; set => Align.X = value; }
    public Align AlignY { readonly get => Align.Y; set => Align.Y = value; }

    public static readonly ContainerStyle Default = new();
    public static readonly ContainerStyle Fit = new() { Size = Size2.Fit };
    public static readonly ContainerStyle Center = new() { Size = Size2.Fit, Align = NoZ.Align.Center };
}


public static partial class UI
{
    private static void BeginContainerImpl(WidgetId id, in ContainerStyle style, int axis)
    {
        ElementTree.BeginTree();

        var flags = id != 0 ? ElementTree.GetPrevWidgetFlags(id) : WidgetFlags.None;
        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;

        if (!style.Margin.IsZero)
            ElementTree.BeginMargin(style.Margin);

        if (style.Align.X != Align.Min || style.Align.Y != Align.Min)
            ElementTree.BeginAlign(style.Align);

        ElementTree.BeginSize(style.Size, style.MinWidth, style.MaxWidth, style.MinHeight, style.MaxHeight);

        if (id != 0)
            ElementTree.BeginWidget(id);

        if (!resolved.Background.IsTransparent || resolved.BorderWidth > 0)
            ElementTree.BeginFill(resolved.Background, style.BorderRadius, resolved.BorderWidth, resolved.BorderColor, style.Order);

        if (style.Clip)
            ElementTree.BeginClip(style.BorderRadius);

        if (!style.Padding.IsZero)
            ElementTree.BeginPadding(style.Padding);

        if (axis == 0)
            ElementTree.BeginRow(style.Spacing);
        else if (axis == 1)
            ElementTree.BeginColumn(style.Spacing);
    }

    private static void EndContainerImpl() 
    {
        ElementTree.EndTree();
    }

    #region Container

    public static AutoContainer BeginContainer(WidgetId id = default) =>
        BeginContainer(id, ContainerStyle.Default);

    public static AutoContainer BeginContainer(WidgetId id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, -1);
        return new AutoContainer();
    }

    public static AutoContainer BeginContainer(in ContainerStyle style) =>
        BeginContainer(WidgetId.None, style);

    public static void EndContainer() => EndContainerImpl();

    public static void Container(WidgetId id, ContainerStyle style)
    {
        using var _ = BeginContainer(id, style);
    }

    public static void Container(WidgetId id) =>
         Container(id, ContainerStyle.Default);

    public static void Container(ContainerStyle style) =>
        Container(WidgetId.None, style);

    #endregion

    #region Column

    public static AutoColumn BeginColumn(WidgetId id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, 1);
        return new AutoColumn();
    }

    public static AutoColumn BeginColumn(WidgetId id) =>
        BeginColumn(id, ContainerStyle.Default);

    public static AutoColumn BeginColumn(in ContainerStyle style) =>
        BeginColumn(WidgetId.None, style);

    public static AutoColumn BeginColumn() =>
        BeginColumn(WidgetId.None, ContainerStyle.Default);

    public static void EndColumn() => EndContainerImpl();

    #endregion

    #region Row

    public static AutoRow BeginRow(WidgetId id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, 0);
        return new AutoRow();
    }

    public static AutoRow BeginRow(WidgetId id) =>
        BeginRow(id, ContainerStyle.Default);

    public static AutoRow BeginRow(in ContainerStyle style) =>
        BeginRow(WidgetId.None, style);

    public static AutoRow BeginRow() =>
        BeginRow(WidgetId.None, ContainerStyle.Default);

    public static AutoRow BeginRow(float spacing) =>
        BeginRow(WidgetId.None, ContainerStyle.Default with { Spacing = spacing });

    public static void EndRow() => EndContainerImpl();

    #endregion
}
