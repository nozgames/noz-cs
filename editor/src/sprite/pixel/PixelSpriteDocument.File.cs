//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteDocument
{
    protected override bool TryLoadContentToken(ref Tokenizer tk)
    {
        if (tk.ExpectIdentifier("canvas"))
        {
            var w = tk.ExpectInt();
            var h = tk.ExpectInt();
            CanvasSize = new Vector2Int(w, h);
            return true;
        }
        if (tk.ExpectIdentifier("layer"))
        {
            ParseLayer(ref tk, Root);
            return true;
        }
        return false;
    }

    protected override void SaveTypeHeader(StreamWriter writer)
    {
        writer.WriteLine("type raster");
        writer.WriteLine($"canvas {CanvasSize.X} {CanvasSize.Y}");
    }

    protected override void SaveContent(StreamWriter writer)
    {
        writer.WriteLine();

        foreach (var child in Root.Children)
        {
            if (child is PixelLayer pixelLayer)
                SavePixelLayer(writer, pixelLayer, 0);
            else if (child is SpriteGroup group)
                SaveGroup(writer, group, 0);
        }
    }

    private void ParseLayer(ref Tokenizer tk, SpriteNode parent)
    {
        var name = tk.ExpectQuotedString() ?? "";
        tk.ExpectDelimiter('{');

        PixelData<Color32>? pixels = null;
        var hold = 0;

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("hold"))
                hold = tk.ExpectInt();
            else if (tk.ExpectIdentifier("pixels"))
            {
                var base64 = tk.ExpectQuotedString();
                if (base64 != null)
                {
                    var bytes = Convert.FromBase64String(base64);
                    pixels = new PixelData<Color32>(CanvasSize.X, CanvasSize.Y);
                    var colors = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Color32>(bytes);
                    var count = Math.Min(colors.Length, CanvasSize.X * CanvasSize.Y);
                    for (var i = 0; i < count; i++)
                        pixels[i] = colors[i];
                }
            }
            else
            {
                tk.ExpectToken(out var badToken);
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}' in layer '{name}'");
                break;
            }
        }

        parent.Add(new PixelLayer
        {
            Name = name,
            Hold = hold,
            Pixels = pixels ?? new PixelData<Color32>(CanvasSize.X, CanvasSize.Y)
        });
    }

    private static void SavePixelLayer(StreamWriter writer, PixelLayer layer, int depth)
    {
        var indent = new string(' ', depth * 2);
        var propIndent = new string(' ', (depth + 1) * 2);
        writer.WriteLine($"{indent}layer \"{layer.Name}\" {{");

        if (layer.Hold > 0)
            writer.WriteLine($"{propIndent}hold {layer.Hold}");

        if (layer.Pixels != null)
        {
            var bytes = layer.Pixels.AsByteSpan();
            var base64 = Convert.ToBase64String(bytes);
            writer.WriteLine($"{propIndent}pixels \"{base64}\"");
        }

        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }

    private void ParseGroup(ref Tokenizer tk, SpriteNode parent)
    {
        var name = tk.ExpectQuotedString() ?? "";
        tk.ExpectDelimiter('{');

        var group = new SpriteGroup { Name = name };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("hold"))
                group.Hold = tk.ExpectInt();
            else if (tk.ExpectIdentifier("layer"))
                ParseLayer(ref tk, group);
            else if (tk.ExpectIdentifier("group"))
                ParseGroup(ref tk, group);
            else
            {
                tk.ExpectToken(out var badToken);
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}' in group '{name}'");
                break;
            }
        }

        parent.Add(group);
    }

    private static void SaveGroup(StreamWriter writer, SpriteGroup group, int depth)
    {
        var indent = new string(' ', depth * 2);
        var propIndent = new string(' ', (depth + 1) * 2);
        writer.WriteLine($"{indent}group \"{group.Name}\" {{");

        if (group.Hold > 0)
            writer.WriteLine($"{propIndent}hold {group.Hold}");

        foreach (var child in group.Children)
        {
            if (child is PixelLayer pixelLayer)
                SavePixelLayer(writer, pixelLayer, depth + 1);
            else if (child is SpriteGroup childGroup)
                SaveGroup(writer, childGroup, depth + 1);
        }

        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }
}
