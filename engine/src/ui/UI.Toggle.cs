//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct ToggleStyle()
{
    public float Size = 18;
    public float BorderRadius = 3;
    public float BorderWidth = 1;
    public Color BorderColor = Style.Palette.Border;
    public Color Background = Style.Palette.Background;
    public float CheckSize = 14;
    public Color CheckColor = Color.White;
    public Sprite? CheckSprite = null;
    public Align2 Align = new Align2(NoZ.Align.Min, NoZ.Align.Center);
    public Func<ToggleStyle, WidgetFlags, ToggleStyle>? Resolve;
}

public static partial class UI
{
    public static bool Toggle(WidgetId id, bool value, in ToggleStyle style)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);
        ElementTree.SetWidgetFlag(id, WidgetFlags.Checked, value);

        var flags = ElementTree.GetWidgetFlags();
        var s = style.Resolve != null
            ? style.Resolve(style, flags)
            : style;

        ElementTree.BeginAlign(s.Align);
        ElementTree.BeginSize(s.Size);
        ElementTree.BeginFill(s.Background, s.BorderRadius, s.BorderWidth, s.BorderColor);

        if (value && s.CheckSprite != null)
        {
            ElementTree.BeginPadding((s.Size - s.CheckSize) / 2);
            ElementTree.Image(s.CheckSprite, color: s.CheckColor, align: Align.Center);
            ElementTree.EndPadding();
        }

        ElementTree.EndTree();

        var pressed = flags.HasFlag(WidgetFlags.Pressed);
        if (pressed)
        {
            SetHot(id, value ? 1 : 0);
            NotifyChanged(value ? 0 : 1);
            ClearHot();
            value = !value;
        }

        return value;
    }
}
