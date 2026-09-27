namespace NoZ.Editor;

/// <summary>Editor swatches with stable slots, independent of their source and runtime export.</summary>
public interface IPaletteSource
{
    int ColorCount { get; }
    int Columns { get; }
    Color GetPaletteColor(int index);
    string? GetPaletteColorName(int index);
    bool ExportColorConstants { get; }
    bool ExportTexture => false;
}
