//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

public partial class VectorSpriteDocument
{
    protected override bool TryLoadContentToken(ref Tokenizer tk)
    {
        if (tk.ExpectIdentifier("group"))
        {
            ParseGroup(ref tk, Root);
            return true;
        }
        if (tk.ExpectIdentifier("layer"))
        {
            // Legacy: vector files shouldn't have pixel layers, but handle gracefully
            ParseLegacyLayer(ref tk, Root);
            return true;
        }
        if (tk.ExpectIdentifier("path"))
        {
            ParsePath(ref tk, Root);
            return true;
        }
        return false;
    }

    protected override void SaveContent(StreamWriter writer)
    {
        writer.WriteLine();

        foreach (var child in Root.Children)
        {
            if (child is SpritePath path)
                SavePath(writer, path, 0);
            else if (child is SpriteGroup group)
                SaveGroup(writer, group, 0);
        }
    }

    private void ParseGroup(ref Tokenizer tk, SpriteGroup parent)
    {
        var name = tk.ExpectQuotedString() ?? "";
        tk.ExpectDelimiter('{');

        var group = new SpriteGroup { Name = name };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;
            else if (tk.ExpectIdentifier("group"))
                ParseGroup(ref tk, group);
            else if (tk.ExpectIdentifier("path"))
                ParsePath(ref tk, group);
            else
            {
                tk.ExpectToken(out var badToken);
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}' in group '{name}'");
                break;
            }
        }

        parent.Add(group);
    }

    private static void ParseLegacyLayer(ref Tokenizer tk, SpriteGroup parent)
    {
        // Skip pixel layer content in vector files
        tk.ExpectQuotedString();
        tk.ExpectDelimiter('{');
        var depth = 1;
        while (!tk.IsEOF && depth > 0)
        {
            if (tk.ExpectDelimiter('{')) depth++;
            else if (tk.ExpectDelimiter('}')) depth--;
            else tk.ExpectToken(out _);
        }
    }

    internal static void ParsePath(ref Tokenizer tk, SpriteNode layer)
    {
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
                var newContour = new SpriteContour();
                path.Contours.Add(newContour);
            }
            else if (tk.ExpectIdentifier("anchor"))
            {
                var x = tk.ExpectFloat();
                var y = tk.ExpectFloat();
                var curve = tk.ExpectFloat();
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

    internal static void SavePath(StreamWriter writer, SpritePath path, int depth)
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

    internal static void SaveGroup(StreamWriter writer, SpriteGroup group, int depth)
    {
        var indent = new string(' ', depth * 2);
        writer.WriteLine($"{indent}group \"{group.Name}\" {{");

        foreach (var child in group.Children)
        {
            if (child is SpritePath path)
                SavePath(writer, path, depth + 1);
            else if (child is SpriteGroup childGroup)
                SaveGroup(writer, childGroup, depth + 1);
        }

        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }
}
