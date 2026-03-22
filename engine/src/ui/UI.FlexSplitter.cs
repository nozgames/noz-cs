//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct FlexSplitterStyle()
{
    public float BarSize = 6;
    public float MinSize = 50;
    public float MaxSize = float.MaxValue;
    public float MinSize2 = 50;
    public float MaxSize2 = float.MaxValue;
    public Color BarColor = new(0.3f, 0.3f, 0.3f, 1f);
    public Color BarHoverColor = new(0.5f, 0.5f, 0.5f, 1f);
    public Color BarDragColor = new(0.4f, 0.6f, 1f, 1f);
    public Func<FlexSplitterStyle, WidgetFlags, FlexSplitterStyle>? Resolve;

    public static readonly FlexSplitterStyle Default = new();
}

public static partial class UI
{
    public static bool FlexSplitter(WidgetId id, ref float size, in FlexSplitterStyle style, int fixedPane = 1)
    {
        var isRow = IsRow();
        var flags = ElementTree.GetWidgetFlags(id);
        var s = style.Resolve != null ? style.Resolve(style, flags) : style;

        var barColor = s.BarColor;
        if (HasCapture(id))
            barColor = s.BarDragColor;
        else if (IsHovered(id))
            barColor = s.BarHoverColor;

        ref var state = ref ElementTree.BeginWidget<FlexSplitterState>(id);

        if (state.Initialized == 0)
        {
            state.FixedSize = size;
            state.Initialized = 1;
        }

        // Recompute ratio from pixel size each frame
        if (state.AvailableSpace > 0)
            state.Ratio = Math.Clamp(state.FixedSize / state.AvailableSpace, 0.001f, 0.999f);

        state.FixedPane = fixedPane;

        var barWidth = isRow ? new Size(s.BarSize) : Size.Percent(1);
        var barHeight = isRow ? Size.Percent(1) : new Size(s.BarSize);

        ElementTree.BeginFlexSplitter(ref state, id, s.BarSize,
            s.MinSize, s.MaxSize, s.MinSize2, s.MaxSize2);

        var cursorType = isRow ? SystemCursor.ResizeEW : SystemCursor.ResizeNS;
        ElementTree.BeginCursor(cursorType);

        ElementTree.BeginSize(barWidth, barHeight);
        ElementTree.BeginFill(barColor);
        ElementTree.EndFill();
        ElementTree.EndSize();

        ElementTree.EndCursor();
        ElementTree.EndFlexSplitter();
        ElementTree.EndWidget();

        // Sync pixel size back to caller
        var newSize = state.FixedSize;
        var changed = newSize != size;
        size = newSize;

        SetLastElement(id);
        return changed;
    }

    public static bool FlexSplitter(WidgetId id, ref float size, int fixedPane = 1) =>
        FlexSplitter(id, ref size, FlexSplitterStyle.Default, fixedPane);
}
