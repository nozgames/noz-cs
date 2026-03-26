//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public struct FontGlyph
{
    public Vector2 UVMin;
    public Vector2 UVMax;
    public Vector2 Size;
    public float Advance;
    public Vector2 Bearing;
    internal ushort NameStart;
    internal ushort NameLength;
}

public class Font : Asset
{
    internal const ushort Version = 6;

    public int FontSize { get; private set; }
    public int AtlasWidth { get; private set; }
    public int AtlasHeight { get; private set; }
    public float Ascent { get; private set; }
    public float Descent { get; private set; }
    public float LineHeight { get; private set; }
    public float Baseline { get; private set; }
    public float InternalLeading { get; private set; }
    public string FamilyName { get; private set; } = "";

    private readonly Dictionary<int, FontGlyph> _glyphs = new();
    private readonly Dictionary<uint, float> _kerning = new();
    private Texture? _atlasTexture;
    private char[]? _glyphNameBuffer;

    public Texture? AtlasTexture => _atlasTexture;
    public int GlyphCount => _glyphs.Count;
    public IEnumerable<int> Glyphs => _glyphs.Keys;

    public ReadOnlySpan<char> GetGlyphName(int codepoint)
    {
        if (_glyphNameBuffer != null && _glyphs.TryGetValue(codepoint, out var glyph) && glyph.NameLength > 0)
            return _glyphNameBuffer.AsSpan(glyph.NameStart, glyph.NameLength);
        return [];
    }

    private Font(string name) : base(AssetType.Font, name)
    {
    }

    public Font() : base(AssetType.Font) { }

    public FontGlyph GetGlyph(char c)
    {
        if (_glyphs.TryGetValue((int)c, out var glyph) && glyph.Advance > 0)
            return glyph;

        // Fallback to unknown glyph (ASCII DEL)
        const int unknownChar = 0x7F;
        if (_glyphs.TryGetValue(unknownChar, out var fallback) && fallback.Advance > 0)
            return fallback;

        return default;
    }

    public float GetKerning(char first, char second)
    {
        var key = ((uint)first << 16) | (uint)second;
        return _kerning.GetValueOrDefault(key, 0f);
    }

    protected override void Load(BinaryReader reader)
    {
        FontSize = (int)reader.ReadUInt32();
        AtlasWidth = (int)reader.ReadUInt32();
        AtlasHeight = (int)reader.ReadUInt32();
        Ascent = reader.ReadSingle();
        Descent = reader.ReadSingle();
        LineHeight = reader.ReadSingle();
        Baseline = reader.ReadSingle();
        InternalLeading = reader.ReadSingle();

        var familyNameLength = reader.ReadUInt16();
        FamilyName = familyNameLength > 0
            ? new string(reader.ReadChars(familyNameLength))
            : "";

        var glyphCount = reader.ReadUInt16();
        var glyphCodepoints = new int[glyphCount];
        for (var i = 0; i < glyphCount; i++)
        {
            var codepoint = reader.ReadUInt32();
            glyphCodepoints[i] = (int)codepoint;
            var uvMinX = reader.ReadSingle();
            var uvMinY = reader.ReadSingle();
            var uvMaxX = reader.ReadSingle();
            var uvMaxY = reader.ReadSingle();
            var sizeX = reader.ReadSingle();
            var sizeY = reader.ReadSingle();
            var advance = reader.ReadSingle();
            var bearingX = reader.ReadSingle();
            var bearingY = reader.ReadSingle();

            _glyphs[(int)codepoint] = new FontGlyph
            {
                UVMin = new Vector2(uvMinX, uvMinY),
                UVMax = new Vector2(uvMaxX, uvMaxY),
                Size = new Vector2(sizeX, sizeY),
                Advance = advance,
                Bearing = new Vector2(bearingX, bearingY)
            };
        }

        var kerningCount = reader.ReadUInt16();
        for (var i = 0; i < kerningCount; i++)
        {
            var first = reader.ReadUInt32();
            var second = reader.ReadUInt32();
            var amount = reader.ReadSingle();
            var key = (first << 16) | second;
            _kerning[key] = amount;
        }

        var atlasDataSize = AtlasWidth * AtlasHeight * 4;
        var rgbaData = reader.ReadBytes(atlasDataSize);
        _atlasTexture = Texture.Create(AtlasWidth, AtlasHeight, rgbaData, TextureFormat.RGBA8, name: Name + "_atlas");

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var nameBufferLength = reader.ReadUInt16();
            if (nameBufferLength > 0)
                _glyphNameBuffer = reader.ReadChars(nameBufferLength);

            for (var i = 0; i < glyphCount; i++)
            {
                var nameStart = reader.ReadUInt16();
                var nameLength = reader.ReadUInt16();
                var cp = glyphCodepoints[i];
                if (_glyphs.TryGetValue(cp, out var g))
                {
                    g.NameStart = nameStart;
                    g.NameLength = nameLength;
                    _glyphs[cp] = g;
                }
            }
        }
    }

    private static Font? Load(Stream stream, string name)
    {
        var font = new Font(name);
        using var reader = new BinaryReader(stream);
        font.Load(reader);
        return font;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Font, "Font", typeof(Font), Load, Version));
    }

    public override void Dispose()
    {
        _atlasTexture?.Dispose();
        _atlasTexture = null;
        base.Dispose();
    }
}
