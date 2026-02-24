//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PaletteDef
{
    public const int MaxColorCount = 256;

    public string Id { get; }
    public string? DisplayName { get; }
    public string Label { get; }
    public Color[] Colors { get; } = new Color[MaxColorCount];
    public string?[] ColorNames { get; } = new string?[MaxColorCount];
    public int Count { get; internal set; }

    internal PaletteDocument? SourceDocument { get; set; }

    public PaletteDef(string id, string? displayName)
    {
        Id = id;
        DisplayName = displayName;
        Label = displayName ?? id;
    }

    internal void SyncFromDocument()
    {
        if (SourceDocument == null) return;

        Count = SourceDocument.ColorCount;
        for (int i = 0; i < Count; i++)
        {
            Colors[i] = SourceDocument.Colors[i];
            ColorNames[i] = SourceDocument.ColorNames[i];
        }
    }
}

public static class PaletteManager
{
    private static readonly List<PaletteDef> _palettes = [];
    private static readonly Dictionary<string, int> _paletteIdMap = [];

    public static IReadOnlyList<PaletteDef> Palettes => _palettes;

    public static void Init()
    {
        _palettes.Clear();
        _paletteIdMap.Clear();
    }

    public static void DiscoverPalettes()
    {
        _palettes.Clear();
        _paletteIdMap.Clear();

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is not PaletteDocument palDoc) continue;

            var id = palDoc.Name;
            var def = new PaletteDef(id, null);
            def.SourceDocument = palDoc;
            def.SyncFromDocument();

            _paletteIdMap[id.ToLower()] = _palettes.Count;
            _palettes.Add(def);
        }

        if (_palettes.Count > 0)
            Log.Info($"Discovered {_palettes.Count} palette(s)");
    }

    public static void Shutdown()
    {
        _palettes.Clear();
        _paletteIdMap.Clear();
    }

    public static PaletteDef? GetPalette(int index)
    {
        if (_palettes.Count == 0) return null;
        if (index < 0 || index >= _palettes.Count) return _palettes[0];
        return _palettes[index];
    }

    public static PaletteDef? GetPalette(string id) =>
        _paletteIdMap.TryGetValue(id.ToLower(), out var index) ? _palettes[index] : null;

    public static bool TryGetPalette(string? id, out PaletteDef palette)
    {
        if (!string.IsNullOrEmpty(id) && _paletteIdMap.TryGetValue(id.ToLower(), out var index))
        {
            palette = _palettes[index];
            return true;
        }
        palette = _palettes.Count > 0 ? _palettes[0] : null!;
        return false;
    }

    public static Color GetColor(int paletteIndex, int colorId)
    {
        var palette = GetPalette(paletteIndex);
        if (palette == null || colorId < 0 || colorId >= PaletteDef.MaxColorCount)
            return Color.White;
        return palette.Colors[colorId];
    }

    public static Color GetColor(string paletteName, int colorId)
    {
        var palette = GetPalette(paletteName);
        if (palette == null || colorId < 0 || colorId >= PaletteDef.MaxColorCount)
            return Color.White;
        return palette.Colors[colorId];
    }

    public static byte FindColorIndex(int paletteIndex, Color32 color)
    {
        var palette = GetPalette(paletteIndex);
        if (palette == null) return 0;
        for (int i = 0; i < palette.Count; i++)
        {
            var pc = palette.Colors[i].ToColor32();
            if (pc.R == color.R && pc.G == color.G && pc.B == color.B)
                return (byte)i;
        }
        return 0;
    }

    public static void ReloadPaletteColors()
    {
        foreach (var palette in _palettes)
            palette.SyncFromDocument();

        AssetManifest.IsModified = true;
    }
}
