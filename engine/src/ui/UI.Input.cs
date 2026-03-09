//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class UI
{
    public static Vector2 MouseWorldPosition { get; private set; }

    private static void HandleInput()
    {
        // Clear hot when clicking outside the hot element
        var mousePressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        if (mousePressed && ElementTree._hotId != 0)
        {
            var wasPressed = ElementTree.WasPressed(ElementTree._hotId);
            Log.Info($"[UI.HandleInput] hotId={ElementTree._hotId} wasPressed={wasPressed} prevHot={_prevHotId}");
            if (!wasPressed)
                ClearHot();
        }

        // Don't consume mouse buttons when hovering over a Scene element (pass-through),
        // but still consume when popups are open or scrollbar is being used.
        var popupCount = ElementTree.ActivePopupCount;
        if (!ElementTree.MouseOverScene || popupCount > 0)
        {
            if (popupCount > 0 || ElementTree.ScrollbarDragging)
                Input.ConsumeButton(InputCode.MouseLeft);

            if (popupCount > 0)
            {
                Input.ConsumeButton(InputCode.MouseLeftDoubleClick);
                Input.ConsumeButton(InputCode.MouseRight);
            }
        }
    }
}
