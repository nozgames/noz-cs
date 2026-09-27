namespace NoZ.Editor;

public static class EditorViewMenu
{
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
