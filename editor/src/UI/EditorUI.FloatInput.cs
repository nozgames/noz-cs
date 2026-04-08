//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private struct ScrubState
    {
        public float StartValue;
        public float StartMouseX;
        public byte Active;
        public byte PenInitiated;
    }

    private const float ScrubDeadzone = 3f;
    private const float ScrubPixelsPerStep = 4f;
    private const float ScrubPixelsPerStepFine = 8f;

    public static float FloatInput(
        WidgetId id,
        float value,
        in TextInputStyle style,
        float step = 1f,
        float fineStep = 0.1f,
        float min = float.MinValue,
        float max = float.MaxValue,
        string format = "0.##",
        string? placeholder = null)
    {
        var styleWithPaddth = style;
        styleWithPaddth.Padding = new EdgeInsets(
            style.Padding.T,
            style.Padding.L + EditorStyle.Icon.LargeSize + 8,
            style.Padding.B,
            style.Padding.R);

        var text = value.ToString(format);
        var result = UI.TextInput(id + 1, text, styleWithPaddth, placeholder ?? "0");
        if (result != text && float.TryParse(result, out var parsed))
            value = Math.Clamp(parsed, min, max);

        ElementTree.BeginMargin(EdgeInsets.Left(4));
        value = ScrubHandle(id, value, step, fineStep, min, max, style.TextColor, out var scrubWasActive);
        ElementTree.EndMargin();

        if (scrubWasActive)
            UI.SetLastElement(id);

        return value;
    }

    private static float ScrubHandle(
        WidgetId id,
        float value,
        float step,
        float fineStep,
        float min,
        float max,
        Color color,
        out bool wasActive)
    {
        ElementTree.BeginSize(EditorStyle.Icon.LargeSize, Size.Default);
        ElementTree.BeginCursor(SystemCursor.ResizeEW);
        ref var state = ref ElementTree.BeginWidget<ScrubState>(id);
        var flags = ElementTree.GetWidgetFlags();
        wasActive = state.Active != 0;

        ElementTree.Image(EditorAssets.Sprites.IconScrub, EditorStyle.Icon.LargeSize, ImageStretch.Uniform, color, align: Align.Center);

        // Interaction
        switch (state.Active)
        {
            case 0: // Idle
                if (flags.HasFlag(WidgetFlags.Pressed))
                {
                    state.Active = 1;
                    state.StartValue = value;
                    state.StartMouseX = UI.MouseWorldPosition.X;
                    state.PenInitiated = Input.IsPenDown ? (byte)1 : (byte)0;
                    ElementTree.SetCapture();
                }
                break;

            case 1: // Potential scrub (in deadzone)
                if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
                {
                    ElementTree.ReleaseCapture();
                    state.Active = 0;
                }
                else
                {
                    var delta = UI.MouseWorldPosition.X - state.StartMouseX;
                    if (MathF.Abs(delta) >= ScrubDeadzone)
                    {
                        state.Active = 2;
                        UI.SetHot(id, state.StartValue);
                    }
                }
                break;

            case 2: // Scrubbing
            {
                var delta = UI.MouseWorldPosition.X - state.StartMouseX;

                var precise = Input.IsShiftDown();
                if (state.PenInitiated != 0)
                    precise |= Touch.FingerCount >= 1;
                else
                    precise |= Touch.FingerCount >= 2;

                var activeStep = precise ? fineStep : step;
                var pixelsPerStep = precise ? ScrubPixelsPerStepFine : ScrubPixelsPerStep;
                var newValue = state.StartValue + (delta / pixelsPerStep) * activeStep;
                newValue = Math.Clamp(newValue, min, max);

                if (newValue != value)
                {
                    value = newValue;
                    UI.NotifyChanged(newValue.GetHashCode());
                }

                if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
                {
                    ElementTree.ReleaseCapture();
                    UI.ClearHot();
                    state.Active = 0;
                }
                break;
            }
        }

        ElementTree.EndWidget();
        ElementTree.EndCursor();
        ElementTree.EndSize();
        UI.SetLastElement(id);
        return value;
    }
}
