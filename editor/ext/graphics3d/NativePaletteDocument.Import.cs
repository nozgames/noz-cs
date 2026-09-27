using System.Globalization;

namespace NoZ.Editor.Graphics3D;

public sealed partial class NativePaletteDocument
{
    private static readonly WidgetId ImportId = new(0xC0850000);
    private string? _importError;
    private bool _replaceImport;

    public override void InspectorUI()
    {
        base.InspectorUI();
        DrawImportSettings();
    }
    internal void DrawImportSettings()
    {
        using (EditorInspector.BeginProperty("Generate texture"))
        {
            var enabled = UI.Toggle(ImportId, GenerateTexture, EditorStyle.Inspector.Toggle);
            if (enabled != GenerateTexture) { Undo.Record(this); SetOutputs(enabled, GenerateColors); }
        }
        using (EditorInspector.BeginProperty("Export colors"))
        {
            var enabled = UI.Toggle(ImportId + 1, GenerateColors, EditorStyle.Inspector.Toggle);
            if (enabled != GenerateColors) { Undo.Record(this); SetOutputs(GenerateTexture, enabled); }
        }
        using (EditorInspector.BeginProperty("Import mode"))
        {
            var chosen = AssetBrowser.Show(ImportId + 2, ["Append", "Replace"], _replaceImport ? "Replace" : "Append");
            if (chosen != null) _replaceImport = chosen == "Replace";
        }
        if (UI.Button(ImportId + 3, "Import PAL / GPL…", EditorStyle.Button.Secondary))
        {
            var path = EditorInspector.OpenPaletteFile();
            if (path != null)
            {
                try { ImportColors(path, _replaceImport); _importError = null; }
                catch (Exception ex) { _importError = ex.Message; }
            }
        }
        if (_importError != null) UI.Text(_importError, EditorStyle.Text.Secondary);
    }

    public void ImportColors(string path, bool replace = false)
    {
        var colors = ReadImport(path);
        var free = Enumerable.Range(0, ColorCount).Where(i => replace ||
            _pixels[i] == Color32.Transparent && !_gradientCells.ContainsKey(i) && GetColorName(i).Length == 0).ToArray();
        if (colors.Count > free.Length) throw new InvalidDataException("Not enough empty palette cells. Increase the grid size before importing.");
        Undo.Record(this);
        if (replace) { ResetPixels(Size, Color32.Transparent); _names.Clear(); }
        for (var i = 0; i < colors.Count; i++)
        {
            var slot = free[i];
            _pixels[slot] = colors[i].Color;
            if (colors[i].Name.Length > 0) _names[(slot % GridSize, slot / GridSize)] = colors[i].Name;
        }
        InvalidatePreview();
    }

    private static List<(Color32 Color, string Name)> ReadImport(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) throw new InvalidDataException("Empty palette file.");
        var colors = new List<(Color32, string)>();
        if (lines[0].Trim() == "JASC-PAL")
        {
            if (lines.Length < 3 || lines[1].Trim() != "0100" ||
                !int.TryParse(lines[2], out var count) || count < 0 || count > 65536)
                throw new InvalidDataException("Invalid JASC-PAL header.");
            foreach (var line in lines.Skip(3).Where(line => !string.IsNullOrWhiteSpace(line))) colors.Add(ParseColor(line));
            if (colors.Count != count) throw new InvalidDataException("PAL color count does not match its entries.");
        }
        else if (lines[0].Trim() == "GIMP Palette")
        {
            var body = false;
            foreach (var line in lines.Skip(1))
            {
                var text = line.Trim();
                if (text.Length == 0 || text.StartsWith('#')) continue;
                if (!body && (text.StartsWith("Name:") || text.StartsWith("Columns:"))) continue;
                body = true; colors.Add(ParseColor(text));
            }
        }
        else throw new InvalidDataException("Choose a JASC PAL or GIMP GPL palette.");
        if (colors.Count == 0) throw new InvalidDataException("Palette contains no colors.");
        return colors;

        static (Color32, string) ParseColor(string line)
        {
            var fields = line.Trim().Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || !byte.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ||
                !byte.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ||
                !byte.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                throw new InvalidDataException($"Invalid palette color: {line}");
            var name = fields.Length == 4 ? fields[3].Trim() : "";
            if (name.StartsWith('"') && name.EndsWith('"')) name = name[1..^1];
            return (new Color32(r, g, b), name);
        }
    }
}
