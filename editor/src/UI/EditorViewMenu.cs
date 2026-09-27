namespace NoZ.Editor;

public static class EditorViewMenu
{
    public static void Draw(WidgetId id, ref bool isOpen, Action content)
    {
        UI.Flex();
        if (UI.Button(id, DrawContent, EditorStyle.Button.ToggleIcon with { Width = 42, Spacing = 4 }, isSelected: isOpen))
            isOpen = !isOpen;
        EditorUI.Tooltip(id, "View");
        if (!isOpen) return;
        using var popup = UI.BeginPopup(id + 1, EditorStyle.PopupBelow with { AnchorRect = UI.GetElementWorldRect(id) });
        if (UI.IsClosed()) { isOpen = false; return; }
        using var column = UI.BeginColumn(EditorStyle.Popup.Root with { Width = 300, Height = Size.Fit, Padding = 8, Spacing = 4 });
        content();
    }

    public static void Draw(WidgetId id, PopupMenuItem[] items)
    {
        UI.Flex();
        if (UI.Button(id, DrawContent, EditorStyle.Button.ToggleIcon with { Width = 42, Spacing = 4 },
                isSelected: UI.IsPopupMenuOpen(id)))
            UI.OpenPopupMenu(id, items, style: EditorStyle.ContextMenu.Style,
                popupStyle: EditorStyle.PopupBelow with { AnchorRect = UI.GetElementWorldRect(id), ShowChecked = true });
        EditorUI.Tooltip(id, "View");
    }

    private static void DrawContent()
    {
        UI.Image(EditorAssets.Sprites.IconPreview, EditorStyle.Icon.Primary with { Size = EditorStyle.Icon.LargeSize });
        UI.Image(EditorAssets.Sprites.IconDropdown, EditorStyle.Icon.Primary with { Size = 8 });
    }
}
