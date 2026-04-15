//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public struct ButtonStyle()
{
    public Size Width = Size.Fit;
    public Size Height = Style.Widget.Height;
    public float MinWidth = 0;
    public BackgroundStyle Background = Style.Palette.Background;
    public Color ContentColor = Style.Palette.Content;
    public float FontSize = Style.Widget.FontSize;
    public float IconSize = Style.Widget.IconSize;
    public float Spacing = Style.Widget.Spacing;
    public float BorderRadius = Style.Widget.BorderRadius;
    public float BorderWidth = 0;
    public Color BorderColor = Style.Palette.Border;
    public EdgeInsets Padding = EdgeInsets.Zero;
    public Font? Font = null;
    public Color TextOutlineColor = Color.Transparent;
    public float TextOutlineWidth = 0;
    public float TextOutlineSoftness = 0;
    public float Scale = 1f;
    public Func<ButtonStyle, WidgetFlags, ButtonStyle>? Resolve;
    public float AnimationTime = 0f;
    public Easing AnimationEasing = Easing.CubicOut;
}

public static partial class UI
{
    public static bool Button(WidgetId id, string text, in ButtonStyle style, bool isSelected = false) =>
        Button(id, text, null, style, isSelected);

    public static bool Button(WidgetId id, Sprite icon, in ButtonStyle style, bool isSelected = false) =>
        Button(id, null, icon, style, isSelected);

    public static bool Button(WidgetId id, string? text, Sprite? icon, in ButtonStyle style, bool isSelected = false)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);
        ElementTree.SetWidgetFlag(id, WidgetFlags.Checked, isSelected);

        var flags = ElementTree.GetWidgetFlags();
        var s = ResolveAnimated(id, style, flags);

        if (s.Scale != 1f)
            ElementTree.BeginTransform(new Vector2(0.5f, 0.5f), Vector2.Zero, 0f, new Vector2(s.Scale, s.Scale));

        ElementTree.BeginSize(new Size2(s.Width, s.Height), minWidth: s.MinWidth);

        ElementTree.BeginFill(s.Background, s.BorderRadius, s.BorderWidth, s.BorderColor);

        var hasPadding = !s.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(s.Padding);

        ElementTree.BeginAlign(Align.Center);
        ElementTree.BeginRow(s.Spacing);

        if (icon != null)
        {
            ElementTree.Image(
                icon,
                s.IconSize,
                ImageStretch.Uniform,
                s.ContentColor,
                1.0f,
                new Align2(Align.Min, Align.Center));
        }

        if (text != null)
        {
            var font = s.Font ?? _defaultFont!;
            ElementTree.Text(
                text,
                font,
                s.FontSize,
                s.ContentColor,
                new Align2(Align.Center, Align.Center),
                TextOverflow.Overflow,
                s.TextOutlineColor,
                s.TextOutlineWidth,
                s.TextOutlineSoftness);
        }

        ElementTree.EndTree();

        return flags.HasFlag(WidgetFlags.Pressed);
    }

    private static ButtonStyle ResolveAnimated(WidgetId id, in ButtonStyle style, WidgetFlags flags)
    {
        var post = style.Resolve != null ? style.Resolve(style, flags) : style;
        if (style.AnimationTime <= 0f)
            return post;

        ref var tween = ref ElementTree.GetTween(id);
        if (ElementTree.HoverChanged(id))
            tween = Tween.Start(style.AnimationTime, easing: style.AnimationEasing);

        if (tween.IsComplete)
            return post;

        var prevFlags = flags ^ WidgetFlags.Hovered;
        var pre = style.Resolve != null ? style.Resolve(style, prevFlags) : style;
        var t = tween.Update(0f, 1f);
        return LerpButtonStyle(pre, post, t);
    }

    private static ButtonStyle LerpButtonStyle(in ButtonStyle a, in ButtonStyle b, float t)
    {
        var result = b;
        result.Background = new BackgroundStyle
        {
            Color = Color.Mix(a.Background.Color, b.Background.Color, t),
            GradientColor = Color.Mix(a.Background.GradientColor, b.Background.GradientColor, t),
            GradientAngle = MathEx.Mix(a.Background.GradientAngle, b.Background.GradientAngle, t),
            Image = b.Background.Image,
            ImageColor = Color.Mix(a.Background.ImageColor, b.Background.ImageColor, t),
            ImageStretch = b.Background.ImageStretch,
        };
        result.ContentColor = Color.Mix(a.ContentColor, b.ContentColor, t);
        result.BorderColor = Color.Mix(a.BorderColor, b.BorderColor, t);
        result.TextOutlineColor = Color.Mix(a.TextOutlineColor, b.TextOutlineColor, t);
        result.FontSize = MathEx.Mix(a.FontSize, b.FontSize, t);
        result.IconSize = MathEx.Mix(a.IconSize, b.IconSize, t);
        result.Spacing = MathEx.Mix(a.Spacing, b.Spacing, t);
        result.BorderRadius = MathEx.Mix(a.BorderRadius, b.BorderRadius, t);
        result.BorderWidth = MathEx.Mix(a.BorderWidth, b.BorderWidth, t);
        result.TextOutlineWidth = MathEx.Mix(a.TextOutlineWidth, b.TextOutlineWidth, t);
        result.TextOutlineSoftness = MathEx.Mix(a.TextOutlineSoftness, b.TextOutlineSoftness, t);
        result.Scale = MathEx.Mix(a.Scale, b.Scale, t);
        return result;
    }

    public static bool Button(WidgetId id, Action content, in ButtonStyle style, bool isSelected = false)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);
        ElementTree.SetWidgetFlag(id, WidgetFlags.Checked, isSelected);

        var flags = ElementTree.GetWidgetFlags();
        var s = ResolveAnimated(id, style, flags);

        if (s.Scale != 1f)
            ElementTree.BeginTransform(new Vector2(0.5f, 0.5f), Vector2.Zero, 0f, new Vector2(s.Scale, s.Scale));

        ElementTree.BeginSize(new Size2(s.Width, s.Height), minWidth: s.MinWidth);

        ElementTree.BeginFill(s.Background, s.BorderRadius, s.BorderWidth, s.BorderColor);

        var hasPadding = !s.Padding.IsZero;
        if (hasPadding)
            ElementTree.BeginPadding(s.Padding);

        ElementTree.BeginAlign(Align.Center);
        ElementTree.BeginRow(s.Spacing);

        content();

        ElementTree.EndTree();

        return flags.HasFlag(WidgetFlags.Pressed);
    }
}
