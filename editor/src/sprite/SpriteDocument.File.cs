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
        RootLayer.Clear();
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
            else if (tk.ExpectIdentifier("bone"))
                BoneName = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("sort"))
                SortOrderId = tk.ExpectQuotedString();
            else if (tk.ExpectIdentifier("generate"))
                ParseGeneration(ref tk);
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"SpriteDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

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

        parent.Add(layer);
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

        var path = new SpritePath { Name = pathName ?? "" };

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
            else if (tk.ExpectIdentifier("operation"))
            {
                if (tk.ExpectIdentifier(out var op))
                {
                    if (op == "subtract")
                        path.Operation = SpritePathOperation.Subtract;
                    else if (op == "clip")
                        path.Operation = SpritePathOperation.Clip;
                }
            }
            else if (tk.ExpectIdentifier("strokejoin"))
            {
                if (tk.ExpectIdentifier(out var join))
                {
                    if (join == "miter")
                        path.StrokeJoin = SpriteStrokeJoin.Miter;
                    else if (join == "bevel")
                        path.StrokeJoin = SpriteStrokeJoin.Bevel;
                }
            }
            else if (tk.ExpectIdentifier("gradient"))
            {
                if (tk.ExpectIdentifier("linear"))
                {
                    tk.ExpectColor(out var c1);
                    var x1 = tk.ExpectFloat();
                    var y1 = tk.ExpectFloat();
                    tk.ExpectColor(out var c2);
                    var x2 = tk.ExpectFloat();
                    var y2 = tk.ExpectFloat();
                    path.FillType = SpriteFillType.Linear;
                    path.FillGradient = new SpriteFillGradient
                    {
                        StartColor = c1.ToColor32(), Start = new Vector2(x1, y1),
                        EndColor = c2.ToColor32(), End = new Vector2(x2, y2),
                    };
                }
            }
            else if (tk.ExpectIdentifier("open"))
            {
                path.Open = tk.ExpectBool();
            }
            else if (tk.ExpectIdentifier("translate"))
            {
                path.PathTranslation = new Vector2(tk.ExpectFloat(), tk.ExpectFloat());
            }
            else if (tk.ExpectIdentifier("rotate"))
            {
                path.PathRotation = tk.ExpectFloat() * MathF.PI / 180f;
            }
            else if (tk.ExpectIdentifier("scale"))
            {
                path.PathScale = new Vector2(tk.ExpectFloat(), tk.ExpectFloat());
            }
            else if (tk.ExpectIdentifier("contour"))
            {
                // Start a new contour within this path
                var newContour = new SpriteContour();
                path.Contours.Add(newContour);
            }
            else if (tk.ExpectIdentifier("anchor"))
            {
                var x = tk.ExpectFloat();
                var y = tk.ExpectFloat();
                var curve = tk.ExpectFloat();
                // Add to the last contour (primary if no 'contour' keyword seen)
                path.Contours[^1].Anchors.Add(new SpritePathAnchor
                {
                    Position = new Vector2(x, y),
                    Curve = curve,
                });
            }
            else
                break;
        }

        layer.Add(path);
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

        if (BoneName != null)
            writer.WriteLine($"bone \"{BoneName}\"");

        if (SortOrderId != null)
            writer.WriteLine($"sort \"{SortOrderId}\"");

        SaveLayers(writer);
        SaveGeneration(writer);
    }

    private void SaveLayers(StreamWriter writer)
    {
        writer.WriteLine();

        foreach (var child in RootLayer.Children)
        {
            if (child is SpritePath path)
                SavePath(writer, path, 0);
            else if (child is SpriteLayer layer)
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

    private void SavePath(StreamWriter writer, SpritePath path, int depth)
    {
        var indent = new string(' ', depth * 2);
        var propIndent = new string(' ', (depth + 1) * 2);

        if (path.Name != null)
            writer.WriteLine($"{indent}path \"{path.Name}\" {{");
        else
            writer.WriteLine($"{indent}path {{");

        if (path.HasTransform)
        {
            if (path.PathTranslation != Vector2.Zero)
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}translate {1} {2}", propIndent, path.PathTranslation.X, path.PathTranslation.Y));
            if (path.PathRotation != 0f)
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}rotate {1}", propIndent, path.PathRotation * 180f / MathF.PI));
            if (path.PathScale != Vector2.One)
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}scale {1} {2}", propIndent, path.PathScale.X, path.PathScale.Y));
        }

        if (path.Operation != SpritePathOperation.Normal)
            writer.WriteLine($"{propIndent}operation {path.Operation.ToString().ToLowerInvariant()}");
        if (path.Open)
            writer.WriteLine($"{propIndent}open true");
        writer.WriteLine($"{propIndent}fill {FormatColor(path.FillColor)}");

        if (path.FillType == SpriteFillType.Linear)
        {
            var g = path.FillGradient;
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0}gradient linear {1} {2} {3} {4} {5} {6}",
                propIndent, FormatColor(g.StartColor), g.Start.X, g.Start.Y,
                FormatColor(g.EndColor), g.End.X, g.End.Y));
        }

        if (path.StrokeColor.A > 0)
        {
            writer.WriteLine($"{propIndent}stroke {FormatColor(path.StrokeColor)} {path.StrokeWidth}");
            if (path.StrokeJoin != SpriteStrokeJoin.Round)
                writer.WriteLine($"{propIndent}strokejoin {path.StrokeJoin.ToString().ToLowerInvariant()}");
        }

        for (var ci = 0; ci < path.Contours.Count; ci++)
        {
            if (ci > 0)
                writer.WriteLine($"{propIndent}contour");

            foreach (var anchor in path.Contours[ci].Anchors)
            {
                writer.Write(string.Format(CultureInfo.InvariantCulture, "{0}anchor {1} {2}", propIndent, anchor.Position.X, anchor.Position.Y));
                if (MathF.Abs(anchor.Curve) > float.Epsilon)
                    writer.Write(string.Format(CultureInfo.InvariantCulture, " {0}", anchor.Curve));
                writer.WriteLine();
            }
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
                SavePath(writer, path, depth + 1);
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
