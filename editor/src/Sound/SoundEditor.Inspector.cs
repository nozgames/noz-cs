//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class SoundEditor
{
    private static partial class FieldId
    {
        public static partial WidgetId VolumeMin { get; }
        public static partial WidgetId VolumeMax { get; }
        public static partial WidgetId VolumeRandom { get; }
        public static partial WidgetId VolumeState { get; }

        public static partial WidgetId PitchMin { get; }
        public static partial WidgetId PitchMax { get; }
        public static partial WidgetId PitchRandom { get; }
        public static partial WidgetId PitchState { get; }

        public static partial WidgetId FadeIn { get; }
        public static partial WidgetId FadeOut { get; }

        public static partial WidgetId TrimStart { get; }
        public static partial WidgetId TrimEnd { get; }

        public static partial WidgetId NormalizeToggle { get; }
        public static partial WidgetId NormalizeTarget { get; }
    }

    private struct RangeState
    {
        public bool Initialized;
        public bool IsRange;
    }

    private static readonly ContainerStyle ValueRowStyle = new() { Spacing = 4, Height = Size.Fit, MinHeight = EditorStyle.Control.Height };

    public override void InspectorUI()
    {
        SoundInfoUI();
        VolumeUI();
        PitchUI();
        FadeUI();
        TrimUI();
        NormalizeUI();
    }

    private void SoundInfoUI()
    {
        using (Inspector.BeginSection("SOUND"))
        {
            if (Inspector.IsSectionCollapsed) return;

            using (Inspector.BeginProperty("Duration"))
                UI.Text(FormatTime(Document.Duration), style: EditorStyle.Text.Primary);

            using (Inspector.BeginProperty("Sample Rate"))
                UI.Text($"{Document.SampleRate} Hz", style: EditorStyle.Text.Primary);

            using (Inspector.BeginProperty("Channels"))
                UI.Text(Document.ChannelCount == 1 ? "Mono" : "Stereo", style: EditorStyle.Text.Primary);

            using (Inspector.BeginProperty("Bit Depth"))
                UI.Text($"{Document.BitsPerSample}-bit", style: EditorStyle.Text.Primary);
        }
    }

    private void RangePropertyUI(
        string sectionName,
        WidgetId stateId, WidgetId minId, WidgetId maxId, WidgetId randomId,
        string singleLabel,
        Func<float> getMin, Action<float> setMin,
        Func<float> getMax, Action<float> setMax)
    {
        using (Inspector.BeginSection(sectionName))
        {
            if (Inspector.IsSectionCollapsed) return;

            ref var state = ref BeginRangeState(stateId, getMin() != getMax());

            using (Inspector.BeginProperty(state.IsRange ? "Min" : singleLabel))
            using (UI.BeginRow(ValueRowStyle))
            {
                var valMin = FloatInput(minId, getMin());
                if (valMin != getMin())
                {
                    setMin(valMin);
                    if (!state.IsRange) setMax(valMin);
                    Document.ApplyChanges();
                }

                if (RandomToggle(randomId, ref state.IsRange))
                {
                    Undo.Record(Document);
                    if (!state.IsRange) setMax(getMin());
                    Document.ApplyChanges();
                }

                if (state.IsRange)
                {
                    var valMax = FloatInput(maxId, getMax());
                    if (valMax != getMax())
                    {
                        setMax(valMax);
                        Document.ApplyChanges();
                    }
                }
                else
                {
                    using (UI.BeginFlex()) { }
                }
            }
        }
    }

    private void VolumeUI()
    {
        RangePropertyUI("VOLUME",
            FieldId.VolumeState, FieldId.VolumeMin, FieldId.VolumeMax, FieldId.VolumeRandom,
            "Volume",
            () => Document.VolumeMin, v => Document.VolumeMin = v,
            () => Document.VolumeMax, v => Document.VolumeMax = v);
    }

    private void PitchUI()
    {
        RangePropertyUI("PITCH",
            FieldId.PitchState, FieldId.PitchMin, FieldId.PitchMax, FieldId.PitchRandom,
            "Pitch",
            () => Document.PitchMin, v => Document.PitchMin = v,
            () => Document.PitchMax, v => Document.PitchMax = v);
    }

    private void FadeUI()
    {
        using (Inspector.BeginSection("FADE"))
        {
            if (Inspector.IsSectionCollapsed) return;

            using (Inspector.BeginProperty("Fade In"))
            {
                var fadeIn = FloatInput(FieldId.FadeIn, Document.FadeIn);
                if (fadeIn != Document.FadeIn)
                {
                    Document.FadeIn = Math.Clamp(fadeIn, 0f, 1f);
                    Document.ApplyChanges();
                }
            }

            using (Inspector.BeginProperty("Fade Out"))
            {
                var fadeOut = FloatInput(FieldId.FadeOut, Document.FadeOut);
                if (fadeOut != Document.FadeOut)
                {
                    Document.FadeOut = Math.Clamp(fadeOut, 0f, 1f);
                    Document.ApplyChanges();
                }
            }
        }
    }

    private void TrimUI()
    {
        using (Inspector.BeginSection("TRIM"))
        {
            if (Inspector.IsSectionCollapsed) return;

            using (Inspector.BeginProperty("Start"))
            {
                var trimStart = FloatInput(FieldId.TrimStart, Document.TrimStart);
                if (trimStart != Document.TrimStart)
                {
                    Document.TrimStart = MathF.Max(0f, trimStart);
                    Document.ApplyChanges();
                }
            }

            using (Inspector.BeginProperty("End"))
            {
                var trimEnd = FloatInput(FieldId.TrimEnd, Document.TrimEnd);
                if (trimEnd != Document.TrimEnd)
                {
                    Document.TrimEnd = MathF.Max(0f, trimEnd);
                    Document.ApplyChanges();
                }
            }
        }
    }

    private void NormalizeUI()
    {
        using (Inspector.BeginSection("NORMALIZE"))
        {
            if (Inspector.IsSectionCollapsed) return;

            var enabled = Document.NormalizeTarget > 0f;

            using (Inspector.BeginProperty("Enable"))
            {
                if (UI.Toggle(FieldId.NormalizeToggle, "", enabled, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    Undo.Record(Document);
                    Document.NormalizeTarget = enabled ? 0f : 0.9f;
                    Document.ApplyChanges();
                }
            }

            if (Document.NormalizeTarget > 0f)
            {
                using (Inspector.BeginProperty("Target"))
                {
                    var normTarget = FloatInput(FieldId.NormalizeTarget, Document.NormalizeTarget);
                    if (normTarget != Document.NormalizeTarget)
                    {
                        Document.NormalizeTarget = Math.Clamp(normTarget, 0.01f, 1f);
                        Document.ApplyChanges();
                    }
                }
            }
        }
    }

    private static ref RangeState BeginRangeState(WidgetId id, bool dataIsRange)
    {
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<RangeState>(id, interactive: false);
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.IsRange = dataIsRange;
        }
        ElementTree.EndTree();
        return ref state;
    }

    private static bool RandomToggle(WidgetId id, ref bool isExpanded)
    {
        ElementTree.BeginTree();
        ElementTree.SetWidgetFlag(WidgetFlags.Checked, isExpanded);
        ElementTree.BeginWidget(id);
        var flags = ElementTree.GetWidgetFlags();
        var style = EditorStyle.Button.ToggleIcon.Resolve!(EditorStyle.Button.ToggleIcon, flags);

        ElementTree.BeginSize(new Size2(style.Width, style.Height));
        ElementTree.BeginFill(style.Background, style.BorderRadius);
        ElementTree.BeginAlign(Align.Center);
        ElementTree.Image(EditorAssets.Sprites.IconRandomRange, style.IconSize, ImageStretch.Uniform, style.ContentColor);
        ElementTree.EndTree();

        var pressed = flags.HasFlag(WidgetFlags.Pressed);
        if (pressed)
            isExpanded = !isExpanded;

        return pressed;
    }

    private float FloatInput(WidgetId id, float value)
    {
        float result;
        using (UI.BeginFlex())
            result = EditorUI.FloatInput(id, value, EditorStyle.Inspector.TextBox, step: 0.01f, fineStep: 0.001f);
        UI.HandleChange(Document);
        return result;
    }

    private static string FormatFloat(float v) =>
        v % 1 == 0 ? v.ToString("F0") : v.ToString("G4");

    private static string FormatTime(float seconds)
    {
        if (seconds < 1f)
            return $"{seconds * 1000f:F0} ms";
        return $"{seconds:F2} s";
    }
}
