//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public enum ScrollBarVisibility : byte
{
    Auto,
    Always,
    Never
}

public struct ScrollBarStyle()
{
    public float Width = 8f;
    public float MinThumbHeight = 20f;
    public Color TrackColor = new(0.15f, 0.15f, 0.15f, 0.5f);
    public Color ThumbColor = new(0.5f, 0.5f, 0.5f, 0.8f);
    public float BorderRadius = 4f;
    public float Padding = 2f;
    public ScrollBarVisibility Visibility = ScrollBarVisibility.Auto;
    public Func<ScrollBarStyle, WidgetFlags, ScrollBarStyle>? Resolve;

    public static readonly ScrollBarStyle Default = new();
}

public static partial class UI
{
    public static void ScrollBar(WidgetId id, WidgetId scrollableId, in ScrollBarStyle style)
    {
        var scrollRect = GetElementRect(scrollableId);
        var viewportHeight = scrollRect.Height;

        // Read scroll state from linked scrollable
        var offset = 0f;
        var contentHeight = 0f;
        if (ElementTree.IsWidgetValid(scrollableId))
        {
            ref var scrollState = ref ElementTree.GetWidgetState<ScrollState>(scrollableId);
            offset = scrollState.Offset;
            contentHeight = scrollState.ContentHeight;
        }

        var maxScroll = Math.Max(0, contentHeight - viewportHeight);

        // Resolve style
        var flags = ElementTree.GetWidgetFlags(id);
        var s = style.Resolve != null ? style.Resolve(style, flags) : style;

        // Visibility check
        var canScroll = maxScroll > 0;
        if (s.Visibility == ScrollBarVisibility.Never) return;
        if (s.Visibility == ScrollBarVisibility.Auto && !canScroll) return;

        // Compute thumb height
        var trackHeight = viewportHeight - s.Padding * 2;
        if (trackHeight <= 0) return;

        var thumbHeight = canScroll
            ? Math.Max(s.MinThumbHeight, trackHeight * viewportHeight / contentHeight)
            : trackHeight;

        // Sync scroll offset → track value before Track processes input
        var t = canScroll ? MathEx.Clamp01(offset / maxScroll) : 0f;

        ref var trackState = ref ElementTree.BeginWidget<TrackState>(id);
        ElementTree.BeginTrack(ref trackState, id, 0, thumbHeight);

        // Only sync scroll offset → track when not being dragged,
        // otherwise we'd overwrite the value set by HandleTrackInput.
        if (!ElementTree.HasCaptureById(id))
            trackState.Y = t;

        ElementTree.BeginSize(new Size(s.Width), Size.Percent(1));

        // Track background
        ElementTree.BeginAlign(new Align2(Align.Min, Align.Min));
        ElementTree.BeginPadding(EdgeInsets.Symmetric(s.Padding, 0));
        ElementTree.BeginSize(new Size(s.Width), Size.Percent(1));
        ElementTree.BeginFill(s.TrackColor, s.BorderRadius);
        ElementTree.EndFill();
        ElementTree.EndSize();
        ElementTree.EndPadding();
        ElementTree.EndAlign();

        if (trackHeight > 0 && canScroll)
        {
            var usable = trackHeight - thumbHeight;
            var thumbOffset = usable * t;

            // Thumb
            ElementTree.BeginAlign(new Align2(Align.Min, Align.Min));
            ElementTree.BeginMargin(EdgeInsets.Top(s.Padding + thumbOffset));
            ElementTree.BeginSize(new Size(s.Width), new Size(thumbHeight));
            ElementTree.BeginFill(s.ThumbColor, s.BorderRadius);
            ElementTree.EndFill();
            ElementTree.EndSize();
            ElementTree.EndMargin();
            ElementTree.EndAlign();
        }

        ElementTree.EndSize();
        ElementTree.EndTrack();
        ElementTree.EndWidget();

        // Write track value → scroll offset after Track processed input
        if (canScroll && ElementTree.IsWidgetValid(scrollableId))
        {
            var newOffset = trackState.Y * maxScroll;
            newOffset = Math.Clamp(newOffset, 0, maxScroll);
            ref var scrollState = ref ElementTree.GetWidgetState<ScrollState>(scrollableId);
            scrollState.Offset = newOffset;
        }

        SetLastElement(id);
    }

    public static void ScrollBar(WidgetId id, WidgetId scrollableId) =>
        ScrollBar(id, scrollableId, ScrollBarStyle.Default);
}
