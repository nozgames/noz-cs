//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private const float Delay = 0.5f;

    private static WidgetId _tooltipHoveredId;
    private static float _tooltipTimer;
    private static bool _tooltipVisible;

    public static void Tooltip(WidgetId id, string text)
    {
        var isHovered = UI.IsHovered(id);

        if (isHovered)
        {
            if (_tooltipHoveredId == id)
            {
                _tooltipTimer += Time.DeltaTime;
            }
            else
            {
                _tooltipHoveredId = id;
                _tooltipTimer = 0;
                _tooltipVisible = false;
            }

            if (_tooltipTimer >= Delay)
                _tooltipVisible = true;
        }
        else if (_tooltipHoveredId == id)
        {
            _tooltipHoveredId = WidgetId.None;
            _tooltipTimer = 0;
            _tooltipVisible = false;
        }

        if (!_tooltipVisible || _tooltipHoveredId != id)
            return;

        var popupStyle = new PopupStyle
        {
            AnchorRect = UI.GetElementWorldRect(id),
            AnchorX = Align.Center,
            AnchorY = Align.Min,
            PopupAlignX = Align.Center,
            PopupAlignY = Align.Max,
            Spacing = EditorStyle.Control.Spacing,
            ClampToScreen = true,
            AutoClose = false,
            Interactive = false,
        };

        using (UI.BeginPopup(WidgetId.None, popupStyle))
        using (UI.BeginContainer(EditorStyle.Tooltip))
            UI.Text(text, EditorStyle.Text.Primary);
    }
}
