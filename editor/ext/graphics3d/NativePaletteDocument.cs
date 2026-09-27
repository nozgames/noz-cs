using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor.Graphics3D;

/// <summary>Native palette authoring source. Its PNG is an owned generated companion.</summary>
public sealed partial class NativePaletteDocument : PaletteTextureDocument
{
    public const string Extension = ".palette";
    private Dictionary<(int X, int Y), string> _names = [];
    public bool GenerateTexture { get; private set; } = true;
    public bool GenerateColors { get; private set; }
    public override bool ExportTexture => GenerateTexture;
    public override bool ExportColorConstants => GenerateColors;
    public string ImageFilePath => System.IO.Path.ChangeExtension(Path, ".png");
    public override bool NeedsExport => GenerateTexture && !File.Exists(ImageFilePath);

    public new static void RegisterDef() => DocumentDef<NativePaletteDocument>.Register(new DocumentDef
    {
        Type = AssetType.Texture, Name = "Palette", Extensions = [Extension], CompanionExtensions = [".png"],
        Factory = _ => new NativePaletteDocument(),
        EditorFactory = doc => new PaletteTextureEditor((NativePaletteDocument)doc),
        CreateNew = position => CreateNew(position: position), Icon = () => EditorAssets.Sprites.IconPalette,
    });

    public new static Document? CreateNew(string? name = null, System.Numerics.Vector2? position = null) =>
        Project.New(AssetType.Texture, Extension, name, stream =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            using var palette = new NativePaletteDocument();
            palette.Save(writer);
        }, position);

    public override void Load()
    {
        using var source = Parse(File.ReadAllText(Path));
        Clone(source);
    }
    public override void Reload() => Load();
    public override void LoadMetadata(PropertySet meta) { }
    public override void SaveMetadata(PropertySet meta)
    {
        meta.ClearGroup("palette_texture");
        meta.ClearGroup("texture");
    }
    public override void Clone(Document source)
    {
        base.Clone(source);
        if (source is NativePaletteDocument native)
        {
            _names = new(native._names);
            GenerateTexture = native.GenerateTexture;
            GenerateColors = native.GenerateColors;
        }
        else { _names.Clear(); GenerateTexture = true; GenerateColors = false; }
        PaletteManager.ReloadPaletteColors();
    }
    public override string? GetPaletteColorName(int index) =>
        _names.GetValueOrDefault((index % GridSize, index / GridSize)) ?? base.GetPaletteColorName(index);
    public string GetColorName(int index) => _names.GetValueOrDefault((index % GridSize, index / GridSize), "");
    public override bool CanResize(int size) => base.CanResize(size) && _names.Keys.All(cell =>
        cell.X < size / CellPixelSize && cell.Y < size / CellPixelSize);
    public void SetColorName(int index, string name)
    {
        if ((uint)index >= ColorCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (string.IsNullOrEmpty(name)) _names.Remove((index % GridSize, index / GridSize));
        else _names[(index % GridSize, index / GridSize)] = name;
        PaletteManager.ReloadPaletteColors();
    }
    public void SetOutputs(bool texture, bool colors)
    {
        GenerateTexture = texture; GenerateColors = colors;
        PaletteManager.ReloadPaletteColors();
    }

    protected override void Save(Stream stream)
    {
        using (var writer = new StreamWriter(stream, leaveOpen: true)) Save(writer);
        WriteCompanion();
    }
    private void WriteCompanion()
    {
        if (!GenerateTexture)
        {
            if (File.Exists(ImageFilePath)) File.Delete(ImageFilePath);
            return;
        }
        var pixels = CreateTexturePixels();
        // Preserve an equivalent PNG's encoding too: Blender can embed these exact bytes.
        if (File.Exists(ImageFilePath))
        {
            using var existing = Image.Load<Rgba32>(ImageFilePath);
            var equal = existing.Width == Size && existing.Height == Size;
            for (var y = 0; equal && y < Size; y++)
            for (var x = 0; equal && x < Size; x++)
            {
                var color = pixels[y * Size + x];
                equal = existing[x, y] == new Rgba32(color.R, color.G, color.B, color.A);
            }
            if (equal) return;
        }
        using var image = Image.LoadPixelData<Rgba32>(MemoryMarshal.AsBytes(pixels.AsSpan()), Size, Size);
        using var stream = new MemoryStream(); image.SaveAsPng(stream);
        WriteChanged(ImageFilePath, stream.ToArray());
    }
    private static void WriteChanged(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public override void Export(string outputPath, PropertySet meta)
    {
        using var source = new NativePaletteDocument { Path = Path, Name = Name };
        source.Load(); source.WriteCompanion();
        if (!source.GenerateTexture)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            return;
        }
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            var pixels = source.CreateTexturePixels();
            TextureAssetWriter.WriteRgba8(writer, source.Size, source.Size, source.Filter, MemoryMarshal.AsBytes(pixels.AsSpan()));
        }
        WriteChanged(outputPath, stream.ToArray());
    }
}
