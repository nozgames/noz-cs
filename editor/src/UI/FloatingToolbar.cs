//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class FloatingToolbar
{
    public struct AutoFloatingToolbar : IDisposable
    {
        readonly void IDisposable.Dispose() => End();
    }

    private static bool _rowOpen;

    public static AutoFloatingToolbar Begin()
    {
        UI.BeginColumn();
        UI.Flex();
        UI.BeginColumn(EditorStyle.FloatingToolbar.Root);
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

    public static bool Button(WidgetId id, Sprite icon, bool isSelected = false)
    {
        return UI.Button(id, icon, EditorStyle.FloatingToolbar.ToolButton, isSelected);
    }
}
