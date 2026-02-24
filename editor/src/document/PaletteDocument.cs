//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class PaletteDocument : Document
{
    public static readonly AssetType PaletteAssetType = AssetType.FromString("PAL_");
    public const int MaxColors = 256;

    public override bool CanSave => true;

    public Color[] Colors { get; private set; } = new Color[MaxColors];
    public string?[] ColorNames { get; private set; } = new string?[MaxColors];
    public int ColorCount { get; set; }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = PaletteAssetType,
            Extension = ".pal",
            Factory = () => new PaletteDocument(),
            EditorFactory = doc => new PaletteEditor((PaletteDocument)doc),
            NewFile = NewFile,
        });
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("JASC-PAL");
        writer.WriteLine("0100");
        writer.WriteLine("16");
        for (int i = 0; i < 16; i++)
            writer.WriteLine("0 0 0");
    }

    public override void Load()
    {
        IsEditorOnly = true;
        ParsePalFile();
    }

    public override void Reload()
    {
        ParsePalFile();
        PaletteManager.ReloadPaletteColors();
    }

    public override void Save(StreamWriter sw)
    {
        sw.WriteLine("JASC-PAL");
        sw.WriteLine("0100");
        sw.WriteLine(ColorCount.ToString());
        for (int i = 0; i < ColorCount; i++)
        {
            var c = Colors[i];
            int r = (int)(Math.Clamp(c.R, 0, 1) * 255);
            int g = (int)(Math.Clamp(c.G, 0, 1) * 255);
            int b = (int)(Math.Clamp(c.B, 0, 1) * 255);

            if (!string.IsNullOrEmpty(ColorNames[i]))
                sw.WriteLine($"{r} {g} {b} \"{ColorNames[i]}\"");
            else
                sw.WriteLine($"{r} {g} {b}");
        }
    }

    public override void Draw()
    {
        var columns = 8;
        var cellSize = 1f / columns;
        var rows = (ColorCount + columns - 1) / columns;
        var totalHeight = rows * cellSize;

        Bounds = new Rect(-0.5f, -totalHeight * 0.5f, columns * cellSize, totalHeight);

        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTexture(Graphics.WhiteTexture);

            for (int i = 0; i < ColorCount; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float x = -0.5f + col * cellSize;
                float y = -totalHeight * 0.5f + row * cellSize;

                Graphics.SetColor(Colors[i]);
                Graphics.Draw(x, y, cellSize, cellSize);
            }
        }
    }

    private void ParsePalFile()
    {
        if (!File.Exists(Path))
            return;

        var lines = File.ReadAllLines(Path);
        if (lines.Length < 3) return;

        if (lines[0].Trim() != "JASC-PAL") return;
        // lines[1] is version "0100" -- skip

        if (!int.TryParse(lines[2].Trim(), out int count)) return;
        count = Math.Min(count, MaxColors);

        ColorCount = 0;
        for (int i = 0; i < count && (i + 3) < lines.Length; i++)
        {
            var line = lines[i + 3].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var tk = new Tokenizer(line);
            if (!tk.ExpectInt(out int r)) continue;
            if (!tk.ExpectInt(out int g)) continue;
            if (!tk.ExpectInt(out int b)) continue;

            Colors[ColorCount] = new Color(r / 255f, g / 255f, b / 255f, 1f);
            ColorNames[ColorCount] = tk.ExpectQuotedString();
            ColorCount++;
        }
    }
}
