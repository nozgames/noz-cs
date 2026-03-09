//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private static readonly SliderStyle _sliderStyle = new()
    {
        Height = EditorStyle.Inspector.ControlHeight,
        TrackHeight = EditorStyle.Slider.TrackHeight,
        ThumbSize = EditorStyle.Slider.ThumbSize,
        TrackColor = EditorStyle.Palette.PageBG,
        FillColor = EditorStyle.Palette.Primary,
        ThumbColor = EditorStyle.Palette.Content,
        Step = 0.05f,
    };

    public static bool Slider(WidgetId id, ref float value, float min = 0f, float max = 1f) =>
        UI.Slider(id, ref value, _sliderStyle, min, max);
}
