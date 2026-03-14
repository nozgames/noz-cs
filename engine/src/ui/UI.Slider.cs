//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct SliderStyle()
{
    public float Height = 20;
    public float TrackHeight = 4;
    public float ThumbSize = 14;
    public Color TrackColor = new(0.3f, 0.3f, 0.3f, 1f);
    public Color FillColor = new(0.4f, 0.6f, 1f, 1f);
    public Color ThumbColor = Color.White;
    public float BorderRadius = Style.Widget.BorderRadius;
    public float Step = 0;
    public Func<SliderStyle, WidgetFlags, SliderStyle>? Resolve;

    public static readonly SliderStyle Default = new();
}

public static partial class UI
{
    public static bool Slider(WidgetId id, ref float value, in SliderStyle style, float min = 0f, float max = 1f)
    {
        var t = max > min ? MathEx.Clamp01((value - min) / (max - min)) : 0f;
        var rect = GetElementRect(id);
        var trackWidth = rect.Width;

        var flags = ElementTree.GetWidgetFlags(id);
        var s = style.Resolve != null ? style.Resolve(style, flags) : style;

        var trackRadius = s.BorderRadius > 0 ? s.BorderRadius : s.TrackHeight / 2;
        var thumbRadius = s.ThumbSize / 2;

        ref var trackState = ref ElementTree.BeginWidget<TrackState>(id);
        ElementTree.BeginTrack(ref trackState, id, s.ThumbSize);

        ElementTree.BeginSize(Size.Percent(1), new Size(s.Height));

        // Track background
        ElementTree.BeginAlign(new Align2(Align.Min, Align.Center));
        ElementTree.BeginSize(Size.Percent(1), new Size(s.TrackHeight));
        ElementTree.BeginFill(s.TrackColor, trackRadius);
        ElementTree.EndFill();
        ElementTree.EndSize();
        ElementTree.EndAlign();

        if (trackWidth > 0)
        {
            var usable = trackWidth - s.ThumbSize;
            var thumbOffset = usable * t;

            // Fill bar
            ElementTree.BeginAlign(new Align2(Align.Min, Align.Center));
            ElementTree.BeginSize(new Size(thumbOffset + thumbRadius), new Size(s.TrackHeight));
            ElementTree.BeginFill(s.FillColor, trackRadius);
            ElementTree.EndFill();
            ElementTree.EndSize();
            ElementTree.EndAlign();

            // Thumb
            ElementTree.BeginAlign(new Align2(Align.Min, Align.Center));
            ElementTree.BeginMargin(EdgeInsets.Left(thumbOffset));
            ElementTree.BeginSize(new Size(s.ThumbSize), new Size(s.ThumbSize));
            ElementTree.BeginFill(s.ThumbColor, thumbRadius);
            ElementTree.EndFill();
            ElementTree.EndSize();
            ElementTree.EndMargin();
            ElementTree.EndAlign();
        }

        ElementTree.EndSize();
        ElementTree.EndTrack();
        ElementTree.EndWidget();

        var newValue = MathEx.Mix(min, max, trackState.X);

        if (s.Step > 0)
            newValue = MathF.Round(newValue / s.Step) * s.Step;

        newValue = Math.Clamp(newValue, min, max);

        var changed = newValue != value;
        if (changed)
        {
            value = newValue;
            NotifyChanged(newValue.GetHashCode());
        }

        SetLastElement(id);
        return changed;
    }

    public static bool Slider(WidgetId id, ref float value, float min = 0f, float max = 1f) =>
        Slider(id, ref value, SliderStyle.Default, min, max);
}
