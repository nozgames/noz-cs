//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using StbImageSharp;

namespace NoZ.Editor;

public class PaletteDef
{
    public const int ColorCount = 64;
    public const int CellSize = 8;

    public string Name { get; }
    public int Id { get; }
    public Color[] Colors { get; } = new Color[ColorCount];

    public PaletteDef(string name, int id)
    {
        Name = name;
        Id = id;

        for (int i = 0; i < ColorCount; i++)
            Colors[i] = Color.Purple;
    }

    public void SampleColors(byte[] pixels, int width, int height)
    {
        int y = Id * CellSize + CellSize / 2;
        if (y < 0 || y >= height)
            return;

        for (int c = 0; c < ColorCount; c++)
        {
            int x = c * CellSize + CellSize / 2;
            if (x >= width)
                break;

            int pixelIndex = (y * width + x) * 4;
            Colors[c] = new Color(
                pixels[pixelIndex + 0] / 255f,
                pixels[pixelIndex + 1] / 255f,
                pixels[pixelIndex + 2] / 255f,
                pixels[pixelIndex + 3] / 255f
            );
        }
    }
}

public static class PaletteManager
{
    private static readonly List<PaletteDef> _palettes = [];
    private static readonly Dictionary<int, int> _paletteMap = [];
    private static string? _paletteTextureName;
    private static TextureDocument? _paletteTexture;

    public static IReadOnlyList<PaletteDef> Palettes => _palettes;

    public static void Init(EditorConfig config)
    {
        _palettes.Clear();
        _paletteMap.Clear();

        _paletteTextureName = config.Palette;

        foreach (var name in config.GetPaletteNames())
        {
            int id = config.GetPaletteIndex(name);
            _paletteMap[id] = _palettes.Count;
            _palettes.Add(new PaletteDef(name, id));
        }

        ReloadPaletteColors();
    }

    public static void Shutdown()
    {
        _palettes.Clear();
        _paletteMap.Clear();
        _paletteTextureName = null;
        _paletteTexture = null;
    }

    public static PaletteDef? GetPalette(int id)
    {
        return _paletteMap.TryGetValue(id, out var index) ? _palettes[index] : null;
    }

    public static PaletteDef? GetPalette(string name)
    {
        return _palettes.FirstOrDefault(p => p.Name == name);
    }

    public static void ReloadPaletteColors()
    {
        if (string.IsNullOrEmpty(_paletteTextureName))
            return;

        _paletteTexture = DocumentManager.Find(AssetType.Texture, _paletteTextureName) as TextureDocument;
        if (_paletteTexture == null)
        {
            Log.Error($"Palette texture not found: {_paletteTextureName}.png");
            return;
        }

        _paletteTexture.IsVisible = false;

        try
        {
            using var stream = File.OpenRead(_paletteTexture.Path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            foreach (var palette in _palettes)
                palette.SampleColors(image.Data, image.Width, image.Height);

            Log.Info($"Loaded palette {_paletteTexture.Path}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load palette texture: {ex.Message}");
        }
    }
}
