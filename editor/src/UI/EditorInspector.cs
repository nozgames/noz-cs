//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

/// <summary>
/// Stable public facade for custom editor inspectors. Internal inspector layout
/// and control implementations can evolve without becoming part of the custom
/// editor API surface.
/// </summary>
public static class EditorInspector
{
    public readonly struct AutoSection : IDisposable
    {
        public void Dispose() => Inspector.EndSection();
    }

    public readonly struct AutoProperty : IDisposable
    {
        public void Dispose() => Inspector.EndProperty();
    }

    public static bool IsSectionCollapsed => Inspector.IsSectionCollapsed;

    public static AutoSection BeginSection(
        string name,
        Sprite? icon = null,
        Action? content = null,
        bool isActive = false,
        bool collapsed = false,
        bool empty = false)
    {
        Inspector.BeginSection(name, icon, content, isActive, collapsed, empty);
        return new AutoSection();
    }

    public static AutoProperty BeginProperty(string name)
    {
        Inspector.BeginProperty(name);
        return new AutoProperty();
    }

    public static string TextField(WidgetId id, string value, Document? undoDocument = null)
    {
        var result = UI.TextInput(id, value, EditorStyle.Inspector.TextBox);
        RecordChangeStart(undoDocument);
        return result;
    }

    public static Color ColorField(WidgetId id, Color value, Document? undoDocument = null, bool showAlpha = true)
    {
        var result = EditorUI.ColorButton(id, value, new ColorButtonStyle { ShowAlpha = showAlpha });
        RecordChangeStart(undoDocument);
        return result;
    }

    public static float FloatField(
        WidgetId id,
        float value,
        Document? undoDocument = null,
        float step = 1f,
        float fineStep = 0.1f,
        float min = float.MinValue,
        float max = float.MaxValue,
        string format = "0.##",
        string? placeholder = null)
    {
        var result = EditorUI.FloatInput(
            id,
            value,
            EditorStyle.Inspector.TextBox,
            step,
            fineStep,
            min,
            max,
            format,
            placeholder);
        RecordChangeStart(undoDocument);
        return result;
    }

    public static Vector3 Vector3Field(
        WidgetId id,
        Vector3 value,
        Document? undoDocument = null,
        float step = 0.1f,
        float fineStep = 0.01f,
        float min = float.MinValue,
        float max = float.MaxValue,
        string format = "0.##")
    {
        using var _ = UI.BeginRow(EditorStyle.Control.Spacing);
        float x, y, z;
        using (UI.BeginFlex())
            x = FloatField(id, value.X, undoDocument, step, fineStep, min, max, format);
        using (UI.BeginFlex())
            y = FloatField(id + 2, value.Y, undoDocument, step, fineStep, min, max, format);
        using (UI.BeginFlex())
            z = FloatField(id + 4, value.Z, undoDocument, step, fineStep, min, max, format);
        return new Vector3(x, y, z);
    }

    private static void RecordChangeStart(Document? document)
    {
        if (document != null && UI.WasChangeStarted())
            Undo.Record(document);
    }
}
