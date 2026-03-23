//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Sprite document text format parsing and serialization.
//

using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteDocument
{
    private void Load(ref Tokenizer tk)
    {
        RootLayer.Children.Clear();
        AnimFrames.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("layer"))
                ParseLayer(ref tk, RootLayer);
            else if (tk.ExpectIdentifier("path"))
                ParsePath(ref tk, RootLayer);
            else if (tk.ExpectIdentifier("frame"))
                ParseAnimFrame(ref tk);
            else if (tk.ExpectIdentifier("edges"))
            {
                if (tk.ExpectVec4(out var edgesVec))
                    Edges = new EdgeInsets(edgesVec.X, edgesVec.Y, edgesVec.Z, edgesVec.W);
            }
            else if (tk.ExpectIdentifier("skeleton"))
                Skeleton.Name = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("generate"))
                ParseGeneration(ref tk);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        ActiveLayer = RootLayer.Children.OfType<SpriteLayer>().FirstOrDefault() ?? RootLayer;
    }

    private void ParseLayer(ref Tokenizer tk, SpriteLayer parent)
    {
        var name = tk.ExpectQuotedString() ?? "";
        tk.ExpectDelimiter('{');

        var layer = new SpriteLayer { Name = name };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("layer"))
                ParseLayer(ref tk, layer);
            else if (tk.ExpectIdentifier("path"))
                ParsePath(ref tk, layer);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.ParseLayer: Unexpected token '{tk.GetString(badToken)}' in layer '{name}'");
                break;
            }
        }

        parent.Children.Add(layer);
    }

    private static void ParsePath(ref Tokenizer tk, SpriteLayer layer)
    {
        // Optional path name: path "name" { ... } or path { ... }
        string? pathName = null;
        if (!tk.ExpectDelimiter('{'))
        {
            pathName = tk.ExpectQuotedString();
            tk.ExpectDelimiter('{');
        }

        var path = new SpritePath { Name = pathName };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("fill"))
            {
                if (tk.ExpectColor(out var color))
                    path.FillColor = color.ToColor32();
                else
                {
                    path.FillColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(1.0f);
                    path.FillColor = path.FillColor.WithAlpha(legacyOpacity);
                }
            }
            else if (tk.ExpectIdentifier("stroke"))
            {
                if (tk.ExpectColor(out var color))
                    path.StrokeColor = color.ToColor32();
                else
                {
                    path.StrokeColor = PaletteManager.GetColor(0, tk.ExpectInt()).ToColor32();
                    var legacyOpacity = tk.ExpectFloat(0.0f);
                    path.StrokeColor = path.StrokeColor.WithAlpha(legacyOpacity);
                }
                path.StrokeWidth = (byte)tk.ExpectInt(path.StrokeWidth);
            }
            else if (tk.ExpectIdentifier("subtract"))
            {
                if (tk.ExpectBool())
                    path.Operation = SpritePathOperation.Subtract;
            }
            else if (tk.ExpectIdentifier("clip"))
            {
                if (tk.ExpectBool())
                    path.Operation = SpritePathOperation.Clip;
            }
            else if (tk.ExpectIdentifier("open"))
            {
                path.Open = tk.ExpectBool();
            }
            else if (tk.ExpectIdentifier("anchor"))
            {
                var x = tk.ExpectFloat();
                var y = tk.ExpectFloat();
                var curve = tk.ExpectFloat();
                path.Anchors.Add(new SpritePathAnchor
                {
                    Position = new Vector2(x, y),
                    Curve = curve,
                });
            }
            else
                break;
        }

        layer.Children.Add(path);
    }

    private void ParseAnimFrame(ref Tokenizer tk)
    {
        tk.ExpectDelimiter('{');
        var frame = new SpriteAnimFrame();

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("hold"))
                frame.Hold = tk.ExpectInt();
            else if (tk.ExpectIdentifier("visible"))
            {
                // Read layer names until we hit a non-string token
                while (!tk.IsEOF)
                {
                    var name = tk.ExpectQuotedString();
                    if (name == null)
                        break;
                    var layer = RootLayer.FindLayer(name);
                    if (layer != null)
                        frame.VisibleLayers.Add(layer);
                    else
                        Log.Warning($"SpriteDocument: Animation frame references unknown layer '{name}'");
                }
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.ParseAnimFrame: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        AnimFrames.Add(frame);
    }

    public override void Save(StreamWriter writer)
    {
        if (!Edges.IsZero)
            writer.WriteLine($"edges ({Edges.T},{Edges.L},{Edges.B},{Edges.R})");

        if (Skeleton.HasValue)
            writer.WriteLine($"skeleton \"{Skeleton.Name}\"");

        SaveLayers(writer);
        SaveGeneration(writer);
    }

    private void SaveLayers(StreamWriter writer)
    {
        writer.WriteLine();

        // Paths directly on root layer are written at top level (no wrapping layer)
        foreach (var child in RootLayer.Children)
        {
            if (child is SpritePath path)
                SavePathV2(writer, path, 0);
        }

        foreach (var child in RootLayer.Children)
        {
            if (child is SpriteLayer layer)
                SaveLayer(writer, layer, 0);
        }

        if (AnimFrames.Count > 0)
        {
            foreach (var frame in AnimFrames)
            {
                writer.Write("frame {");
                writer.WriteLine();

                if (frame.Hold > 0)
                    writer.WriteLine($"  hold {frame.Hold}");

                if (frame.VisibleLayers.Count > 0)
                {
                    writer.Write("  visible");
                    foreach (var layer in frame.VisibleLayers)
                        writer.Write($" \"{layer.Name}\"");
                    writer.WriteLine();
                }

                writer.WriteLine("}");
                writer.WriteLine();
            }
        }
    }

    private void SavePathV2(StreamWriter writer, SpritePath path, int depth)
    {
        var indent = new string(' ', depth * 2);
        var propIndent = new string(' ', (depth + 1) * 2);

        if (path.Name != null)
            writer.WriteLine($"{indent}path \"{path.Name}\" {{");
        else
            writer.WriteLine($"{indent}path {{");

        if (path.IsSubtract)
            writer.WriteLine($"{propIndent}subtract true");
        if (path.IsClip)
            writer.WriteLine($"{propIndent}clip true");
        if (path.Open)
            writer.WriteLine($"{propIndent}open true");
        writer.WriteLine($"{propIndent}fill {FormatColor(path.FillColor)}");

        if (path.StrokeColor.A > 0)
            writer.WriteLine($"{propIndent}stroke {FormatColor(path.StrokeColor)} {path.StrokeWidth}");

        foreach (var anchor in path.Anchors)
        {
            writer.Write(string.Format(CultureInfo.InvariantCulture, "{0}anchor {1} {2}", propIndent, anchor.Position.X, anchor.Position.Y));
            if (MathF.Abs(anchor.Curve) > float.Epsilon)
                writer.Write(string.Format(CultureInfo.InvariantCulture, " {0}", anchor.Curve));
            writer.WriteLine();
        }

        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }

    private void SaveLayer(StreamWriter writer, SpriteLayer layer, int depth)
    {
        var indent = new string(' ', depth * 2);
        writer.WriteLine($"{indent}layer \"{layer.Name}\" {{");

        foreach (var child in layer.Children)
        {
            if (child is SpritePath path)
                SavePathV2(writer, path, depth + 1);
            else if (child is SpriteLayer childLayer)
                SaveLayer(writer, childLayer, depth + 1);
        }

        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }

    private static string FormatColor(Color32 c)
    {
        if (c.A < 255)
            return $"rgba({c.R},{c.G},{c.B},{c.A / 255f:G})";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
