//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    public static string TextInput(
        WidgetId id,
        string value,
        TextInputStyle style,
        string? placeholder = null,
        IChangeHandler? handler = null)
    {
        ElementTree.BeginTree();

        ref var state = ref ElementTree.BeginWidget<EditableTextState>(id);

        // Use _prevHotId to resolve style — SetHot happens after defocus check below
        var wasHot = ElementTree._prevHotId == id;
        var flags = ElementTree.GetWidgetFlags();
        if (wasHot)
            flags |= WidgetFlags.Hot;
        var s = style.Resolve != null ? style.Resolve(style, flags) : style;

        var font = s.Font ?? DefaultFont;
        var height = s.Height.IsFixed ? s.Height.Value : s.FontSize * 1.8f;

        ElementTree.BeginSize(Size.Percent(1), new Size(height));

        if (s.BorderWidth > 0)
            ElementTree.BeginBorder(s.BorderWidth, s.BorderColor, s.BorderRadius);

        if (s.BackgroundColor.A > 0)
            ElementTree.BeginFill(s.BackgroundColor, s.BorderRadius);

        var hasPadding = !s.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(s.Padding);

        value = ElementTree.EditableText(
            ref state,
            value,
            font,
            s.FontSize,
            s.TextColor,
            s.TextColor,
            s.SelectionColor,
            placeholder,
            s.PlaceholderColor,
            s.MultiLine,
            false,
            s.Scope);

        // Set hot AFTER EditableText's defocus check runs
        if (state.Focused != 0)
            SetHot(id);

        ElementTree.EndTree();

        return value;
    }
}
