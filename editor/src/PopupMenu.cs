//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class PopupMenu
{
    private static Vector2 _worldPosition;

    public static bool IsVisible => UI.PopupHelper.IsVisible;
    public static Vector2 WorldPosition => _worldPosition;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
    }

    public static void Open(
        WidgetId id,
        ReadOnlySpan<PopupMenuItem> items,
        string? title = null)
    {
        _worldPosition = Workspace.MouseWorldPosition;
        UI.OpenPopupMenu(id, items, EditorStyle.ContextMenu.Style, title: title);
    }

    public static void Open(
        WidgetId id,
        ReadOnlySpan<PopupMenuItem> items,
        PopupStyle popupStyle,
        string? title = null)
    {
        _worldPosition = Workspace.MouseWorldPosition;
        UI.OpenPopupMenu(id, items, EditorStyle.ContextMenu.Style, popupStyle, title: title);
    }

    public static void Close() => UI.ClosePopupMenu();
    public static bool IsOpen(WidgetId id) => UI.IsPopupMenuOpen(id);

    public static void Update() => UI.PopupHelper.Update();
    public static void UpdateUI() => UI.PopupHelper.UpdateUI();
}
