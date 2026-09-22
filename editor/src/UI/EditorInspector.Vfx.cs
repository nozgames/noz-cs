namespace NoZ.Editor;

public static partial class EditorInspector
{
    /// <summary>Two endpoint fields. Reserves four adjacent widget IDs.</summary>
    public static VfxRange VfxRangeField(WidgetId id, VfxRange value, Document document, float min = float.MinValue)
    {
        using var row = UI.BeginRow(EditorStyle.Control.Spacing);
        float start, end;
        using (UI.BeginFlex()) start = FloatField(id, value.Min, document, .1f, .01f, min);
        using (UI.BeginFlex()) end = FloatField(id + 2, value.Max, document, .1f, .01f, Math.Max(min, start));
        return new(start, Math.Max(start, end));
    }

    /// <summary>Shared lifetime editor including random endpoints and the existing curve popup. Reserves 16 IDs.</summary>
    public static VfxDocFloatCurve VfxCurveField(WidgetId id, VfxDocFloatCurve value, Document document, float min = float.MinValue)
    {
        using (BeginProperty("Start min / max")) value.Start = VfxRangeField(id, value.Start, document, min);
        using (BeginProperty("End min / max")) value.End = VfxRangeField(id + 4, value.End, document, min);
        using (BeginProperty("Curve"))
        {
            if (CurveEditorPopup.Draw(id + 8, ref value)) UI.HandleChange(document);
        }
        return value;
    }

    public static VfxDocColorCurve VfxColorCurveField(WidgetId id, VfxDocColorCurve value, Document document)
    {
        using (BeginProperty("Start min / max"))
        using (UI.BeginRow(EditorStyle.Control.Spacing))
        {
            value.Start.Min = ColorField(id, value.Start.Min, document);
            value.Start.Max = ColorField(id + 1, value.Start.Max, document);
        }
        using (BeginProperty("End min / max"))
        using (UI.BeginRow(EditorStyle.Control.Spacing))
        {
            value.End.Min = ColorField(id + 2, value.End.Min, document);
            value.End.Max = ColorField(id + 3, value.End.Max, document);
        }
        using (BeginProperty("Curve"))
        {
            if (CurveEditorPopup.Draw(id + 4, ref value)) UI.HandleChange(document);
        }
        return value;
    }
}
