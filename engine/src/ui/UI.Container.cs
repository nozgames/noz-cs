//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    private static void BeginContainerImpl(WidgetId id, in ContainerStyle style, int axis)
    {
        ElementTree.BeginTree();

        var flags = WidgetFlags.None;
        if (id != 0)
        {
            ElementTree.BeginWidget(id);
            flags = ElementTree.GetWidgetFlags();
        }

        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;

        if (!style.Margin.IsZero)
            ElementTree.BeginMargin(style.Margin);

        if (resolved.BorderWidth > 0)
            ElementTree.BeginBorder(resolved.BorderWidth, resolved.BorderColor, style.BorderRadius);

        ElementTree.BeginSize(style.Size);

        if (!resolved.Color.IsTransparent)
            ElementTree.BeginFill(resolved.Color, style.BorderRadius);

        if (style.Clip)
            ElementTree.BeginClip(style.BorderRadius);

        if (!style.Padding.IsZero)
            ElementTree.BeginPadding(style.Padding);

        if (style.Align.X != Align.Min || style.Align.Y != Align.Min)
            ElementTree.BeginAlign(style.Align);
        
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
        BeginRow(WidgetId.None, ContainerStyle.Default);

    public static AutoRow BeginRow(in ContainerStyle style) =>
        BeginRow(WidgetId.None, style);

    public static AutoRow BeginRow() =>
        BeginRow(WidgetId.None, ContainerStyle.Default);

    public static void EndRow() => EndContainerImpl();

    #endregion
}
