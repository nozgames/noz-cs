//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class FloatingToolbar
{
    private static partial class ElementId
    {
        public static partial WidgetId Root { get; }
    }

    public struct AutoFloatingToolbar : IDisposable
    {
        readonly void IDisposable.Dispose() => End();
    }

    private static bool _rowOpen;

    public static AutoFloatingToolbar Begin()
    {
        UI.BeginColumn();
        UI.Flex();
        UI.BeginColumn(ElementId.Root, EditorStyle.FloatingToolbar.Root);
        UI.BeginRow(EditorStyle.FloatingToolbar.Row);
        _rowOpen = true;
        return new AutoFloatingToolbar();
    }

    public static void Row()
    {
        if (_rowOpen)
            UI.EndRow();

        UI.BeginRow(EditorStyle.FloatingToolbar.Row);
        _rowOpen = true;
    }

    public static void End()
    {
        if (_rowOpen)
        {
            UI.EndRow();
            _rowOpen = false;
        }

        UI.EndColumn();
        UI.EndColumn();
    }

    public static void Divider()
    {
        UI.Container(EditorStyle.FloatingToolbar.Divider);
    }

    public static bool Button(WidgetId id, Sprite icon)
    {
        return UI.Button(id, icon, EditorStyle.FloatingToolbar.ToolButton);
    }
}
