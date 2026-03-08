//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    public static bool Slider(WidgetId id, ref float value, float min = 0f, float max = 1f)
    {
        var changed = false;
        var t = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;
        var rect = UI.GetElementRect(id);
        var trackWidth = rect.Width;

        using (UI.BeginContainer(id, EditorStyle.Slider.Root))
        {
            UI.Container(EditorStyle.Slider.Track);

            if (trackWidth > 0)
            {
                var thumbSize = EditorStyle.Slider.ThumbSize;
                var usable = trackWidth - thumbSize;
                var thumbOffset = usable * t;

                UI.Container(EditorStyle.Slider.Fill with { Width = thumbOffset + thumbSize / 2 });
                UI.Container(EditorStyle.Slider.Thumb with { Margin = EdgeInsets.Left(thumbOffset) });
            }

            if (UI.IsDown() && !UI.HasCapture())
                UI.SetCapture();

            if (UI.HasCapture())
            {
                UI.SetHot<float>(id, value);

                var worldRect = UI.GetElementWorldRect(id);
                if (worldRect.Width > 0)
                {
                    var mouse = UI.MouseWorldPosition;
                    var thumbHalf = EditorStyle.Slider.ThumbSize / 2;
                    var localX = Math.Clamp(
                        (mouse.X - worldRect.X - thumbHalf) / (worldRect.Width - EditorStyle.Slider.ThumbSize),
                        0f, 1f);
                    var newValue = min + localX * (max - min);
                    newValue = MathF.Round(newValue * 20f) / 20f;
                    newValue = Math.Clamp(newValue, min, max);

                    if (newValue != value)
                    {
                        value = newValue;
                        changed = true;
                        UI.NotifyChanged(newValue.GetHashCode());
                    }
                }
            }
        }

        UI.SetLastElement(id);
        return changed;
    }
}
