namespace NoZ.Editor.Graphics3D;

public sealed partial class NativePaletteDocument
{
    public override void Save(StreamWriter writer)
    {
        writer.WriteLine("version 1");
        writer.WriteLine(FormattableString.Invariant($"grid {GridSize} {GridSize}"));
        writer.WriteLine("cell_pixels 8");
        writer.WriteLine($"texture {GenerateTexture.ToString().ToLowerInvariant()}");
        writer.WriteLine($"colors {GenerateColors.ToString().ToLowerInvariant()}");
        writer.WriteLine($"filter {Filter.ToString().ToLowerInvariant()}");
        for (var i = 0; i < ColorCount; i++)
        {
            var name = GetColorName(i);
            if (_pixels[i] == Color32.Transparent && !_gradientCells.ContainsKey(i) && name.Length == 0) continue;
            writer.WriteLine();
            writer.WriteLine(FormattableString.Invariant($"color \"{Escape(name)}\" position {i % GridSize} {i / GridSize} col {Rgba(_pixels[i])}"));
            if (!_gradientCells.TryGetValue(i, out var gradient)) continue;
            foreach (var segment in gradient.Segments)
                writer.WriteLine(FormattableString.Invariant($"    segment from {segment.StartX} {segment.StartY} to {segment.EndX} {segment.EndY} start {Rgba(segment.StartColor)} end {Rgba(segment.EndColor)}"));
        }
    }
    private static string Escape(string value) => AssetTextWriter.Quote(value)[1..^1];
    private static string Rgba(Color32 color) => FormattableString.Invariant($"{color.R} {color.G} {color.B} {color.A}");

    private static NativePaletteDocument Parse(string text)
    {
        var source = new NativePaletteDocument();
        try
        {
            var tk = new Tokenizer(text);
            Require(ref tk, "version");
            if (Integer(ref tk, 1, 1) != 1) throw new InvalidDataException("Unsupported palette version.");
            Require(ref tk, "grid");
            var columns = Integer(ref tk, 1, MaxSize / CellPixelSize);
            var rows = Integer(ref tk, 1, MaxSize / CellPixelSize);
            if (rows != columns) throw new InvalidDataException("Palette grids must be square.");
            source.ResetPixels(columns * CellPixelSize, Color32.Transparent);
            Require(ref tk, "cell_pixels"); Integer(ref tk, CellPixelSize, CellPixelSize);
            Require(ref tk, "texture"); source.GenerateTexture = Boolean(ref tk);
            Require(ref tk, "colors"); source.GenerateColors = Boolean(ref tk);
            Require(ref tk, "filter");
            if (tk.ExpectIdentifier("point")) source.SetFilter(TextureFilter.Point);
            else if (tk.ExpectIdentifier("linear")) source.SetFilter(TextureFilter.Linear);
            else throw new InvalidDataException("Expected point or linear palette filter.");
            var occupied = new HashSet<int>();
            while (!tk.IsEOF)
            {
                Require(ref tk, "color");
                if (!tk.ExpectQuotedString(out var name)) throw new InvalidDataException("Expected a quoted color name.");
                var nameReader = new AssetTextReader("\"" + name + "\"");
                name = nameReader.String();
                Require(ref tk, "position");
                var x = Integer(ref tk, 0, columns - 1); var y = Integer(ref tk, 0, rows - 1);
                var index = y * columns + x;
                if (!occupied.Add(index)) throw new InvalidDataException($"Duplicate palette cell ({x}, {y}).");
                Require(ref tk, "col"); source._pixels[index] = ReadColor(ref tk);
                if (name.Length > 0) source._names[(x, y)] = name;
                var segments = new List<GradientSegment>();
                while (tk.ExpectIdentifier("segment"))
                {
                    Require(ref tk, "from"); var sx = Integer(ref tk, 0, 255); var sy = Integer(ref tk, 0, 255);
                    Require(ref tk, "to"); var ex = Integer(ref tk, 0, 255); var ey = Integer(ref tk, 0, 255);
                    Require(ref tk, "start"); var start = ReadColor(ref tk);
                    Require(ref tk, "end"); var end = ReadColor(ref tk);
                    segments.Add(new(sx, sy, ex, ey, start, end));
                }
                if (segments.Count > 0)
                {
                    var gradient = new GradientCell([.. segments]);
                    source._gradientCells[index] = gradient;
                    source._pixels[index] = gradient.Sample(x * CellPixelSize + CellPixelSize / 2, y * CellPixelSize + CellPixelSize / 2);
                }
            }
            return source;
        }
        catch { source.Dispose(); throw; }
    }
    private static void Require(ref Tokenizer tk, string keyword)
    { if (!tk.ExpectIdentifier(keyword)) throw new InvalidDataException($"Expected '{keyword}' in palette source."); }
    private static int Integer(ref Tokenizer tk, int min, int max)
    { if (!tk.ExpectInt(out var value) || value < min || value > max) throw new InvalidDataException($"Expected an integer between {min} and {max}."); return value; }
    private static bool Boolean(ref Tokenizer tk)
    { if (!tk.ExpectBool(out var value)) throw new InvalidDataException("Expected true or false."); return value; }
    private static Color32 ReadColor(ref Tokenizer tk) => new((byte)Integer(ref tk, 0, 255), (byte)Integer(ref tk, 0, 255), (byte)Integer(ref tk, 0, 255), (byte)Integer(ref tk, 0, 255));
}
