//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    public static bool ToggleButton(int id, Sprite icon, bool isChecked, bool isEnabled = true, string? tooltip = null, bool small = false)
    {
        var isHovered = UI.IsHovered(id);
        var isPressed = false;

        using (UI.BeginContainer(id, isHovered ? EditorStyle.Button.ToggleHovered : EditorStyle.Button.Toggle))
        {
            if (isChecked)
                UI.Container(EditorStyle.Button.ToggleChecked);

            UI.Image(icon, EditorStyle.Icon.Primary);

            isPressed = UI.WasPressed();
        }

        return isPressed;
    }

    public static bool SmallToggleButton(int id, Sprite icon, bool isChecked, bool isEnabled=true, string? tooltip = null)
    {
        var hovered = UI.IsHovered(id);
        var pressed = false;

        using (UI.BeginContainer(id,
            hovered ? EditorStyle.Button.SmallToggleHovered : EditorStyle.Button.SmallToggle))
        {
            if (isChecked)
                UI.Container(EditorStyle.Button.SmallToggleChecked);

            UI.Image(icon, EditorStyle.Icon.PrimarySmall);

            pressed = UI.WasPressed();
        }

        return pressed;
    }

    public static bool Button(int id, Sprite icon, bool isEnabled=true, string? tooltip = null)
    {
        var isHovered = UI.IsHovered(id);
        var isPressed = false;

        using (UI.BeginContainer(id, isHovered ? EditorStyle.Button.IconHovered : EditorStyle.Button.Icon))
        {
            UI.Image(icon, EditorStyle.Icon.Primary);

            isPressed = UI.WasPressed();
        }

        return isPressed;
    }

    public static bool SmallButton(int id, Sprite icon, bool isEnabled = true, string? tooltip = null)
    {
        var pressed = false;

        using (UI.BeginContainer(id, EditorStyle.Button.SmallIcon))
        {
            UI.Image(icon, EditorStyle.Icon.PrimarySmall);
            pressed = UI.WasPressed();
        }

        return pressed;
    }
}
