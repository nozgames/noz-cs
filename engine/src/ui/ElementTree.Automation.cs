//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;

namespace NoZ;

public readonly record struct WidgetInfo(WidgetId Id, Rect ScreenRect, string Text, WidgetFlags Flags, bool IsInteractive);

public static partial class ElementTree
{
    // Lists the widgets laid out by the last completed frame, with their rects in window
    // pixels and the text drawn inside them. Call between frames, before UI.Begin.
    public static void GetWidgets(List<WidgetInfo> result)
    {
        result.Clear();
        if (_elements.Length == 0) return;

        var texts = new Dictionary<int, StringBuilder>();
        for (var i = 0; i < _elements.Length; i++)
        {
            ref var e = ref GetElement(i);
            if (e.Type != ElementType.Text) continue;

            var owner = FindWidgetAncestor(i);
            if (owner < 0) continue;

            var text = e.Data.Text.Text.AsReadOnlySpan();
            if (text.IsEmpty) continue;

            if (!texts.TryGetValue(owner, out var sb))
                texts[owner] = sb = new StringBuilder();
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }

        var camera = UI.Camera;
        for (var i = 0; i < _elements.Length; i++)
        {
            ref var e = ref GetElement(i);
            if (e.Type != ElementType.Widget) continue;

            var id = e.Data.Widget.Id;
            var rect = GetWidgetWorldRect(id);
            if (camera != null)
                rect = camera.WorldToScreen(rect);

            result.Add(new WidgetInfo(
                id,
                rect,
                texts.TryGetValue(i, out var sb) ? sb.ToString() : string.Empty,
                GetWidgetFlags(id),
                e.Data.Widget.IsInteractive));
        }
    }

    private static int FindWidgetAncestor(int index)
    {
        for (var depth = 0; depth < MaxElementDepth && index > 0; depth++)
        {
            index = GetElement(index).Parent;
            if (GetElement(index).Type == ElementType.Widget)
                return index;
        }
        return -1;
    }
}
