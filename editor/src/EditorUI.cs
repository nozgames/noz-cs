//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static class EditorUI
{
    private static void ShortcutText(string text, bool selected = false)
    {
        UI.Label(text, style: selected ? EditorStyle.Control.Text : EditorStyle.Shortcut.Text);
    }

    private static void ShortcutText(InputCode code, bool selected = false) =>
        ShortcutText(code.ToDisplayString(), selected: selected);
    
    public static void Shortcut(Command command, bool selected=false)
    {
        using (UI.BeginRow(EditorStyle.Shortcut.ListContainer))
        {
            if (command.Ctrl)
            {
                ShortcutText(InputCode.KeyLeftCtrl, selected);
                ShortcutText("+", selected);
            }
            if (command.Alt)
            {
                ShortcutText(InputCode.KeyLeftAlt, selected);
                ShortcutText("+", selected);
            }
            if (command.Shift)
            {
                ShortcutText(InputCode.KeyLeftShift, selected);
                ShortcutText("+", selected);
            }
            ShortcutText(command.Key, selected);
        }
    }
}
