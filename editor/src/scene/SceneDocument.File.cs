//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

public partial class SceneDocument
{
    private void Parse(ref Tokenizer tk)
    {
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("type"))
            {
                tk.ExpectIdentifier();
            }
            else if (tk.ExpectIdentifier("group"))
            {
                ParseGroup(ref tk, Root);
            }
            else if (tk.ExpectIdentifier("sprite"))
            {
                ParseSprite(ref tk, Root);
            }
            else
            {
                tk.ExpectToken(out var bad);
                ReportError(bad.Line, $"Unexpected token '{tk.GetString(bad)}'");
                break;
            }
        }
    }

    private void ParseGroup(ref Tokenizer tk, SceneNode parent)
    {
        var name = tk.ExpectQuotedString() ?? "";
        if (!tk.ExpectDelimiter('{'))
        {
            ReportError("Expected '{' after group name");
            return;
        }

        var group = new SceneGroup { Name = name };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;

            if (TryReadCommonField(ref tk, group))
                continue;

            if (tk.ExpectIdentifier("group"))
                ParseGroup(ref tk, group);
            else if (tk.ExpectIdentifier("sprite"))
                ParseSprite(ref tk, group);
            else
            {
                tk.ExpectToken(out var bad);
                ReportError(bad.Line, $"Unexpected token '{tk.GetString(bad)}' in group '{name}'");
                break;
            }
        }

        parent.Add(group);
    }

    private void ParseSprite(ref Tokenizer tk, SceneNode parent)
    {
        var name = tk.ExpectQuotedString() ?? "";
        if (!tk.ExpectDelimiter('{'))
        {
            ReportError("Expected '{' after sprite name");
            return;
        }

        var node = new SceneSprite { Name = name };

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}'))
                break;

            if (TryReadCommonField(ref tk, node))
                continue;

            if (tk.ExpectIdentifier("ref"))
            {
                node.Sprite.Name = tk.ExpectQuotedString();
            }
            else
            {
                tk.ExpectToken(out var bad);
                ReportError(bad.Line, $"Unexpected token '{tk.GetString(bad)}' in sprite '{name}'");
                break;
            }
        }

        parent.Add(node);
    }

    private static bool TryReadCommonField(ref Tokenizer tk, SceneNode node)
    {
        if (tk.ExpectIdentifier("position"))
        {
            node.Position = new Vector2(tk.ExpectFloat(), tk.ExpectFloat());
            return true;
        }
        if (tk.ExpectIdentifier("rotation"))
        {
            node.Rotation = tk.ExpectFloat() * MathF.PI / 180f;
            return true;
        }
        if (tk.ExpectIdentifier("scale"))
        {
            node.Scale = new Vector2(tk.ExpectFloat(), tk.ExpectFloat());
            return true;
        }
        if (tk.ExpectIdentifier("color"))
        {
            if (tk.ExpectColor(out var color))
                node.Color = color.ToColor32();
            return true;
        }
        if (tk.ExpectIdentifier("visible"))
        {
            node.Visible = tk.ExpectBool();
            return true;
        }
        if (tk.ExpectIdentifier("locked"))
        {
            node.Locked = tk.ExpectBool();
            return true;
        }
        if (tk.ExpectIdentifier("placeholder"))
        {
            node.Placeholder = tk.ExpectBool();
            return true;
        }
        return false;
    }

    public override void Save(StreamWriter writer)
    {
        writer.WriteLine("type scene");
        writer.WriteLine();
        foreach (var child in Root.Children)
            SaveNode(writer, child, 0);
    }

    private static void SaveNode(StreamWriter writer, SceneNode node, int depth)
    {
        if (node is SceneGroup g)
            SaveGroup(writer, g, depth);
        else if (node is SceneSprite s)
            SaveSprite(writer, s, depth);
    }

    private static void SaveGroup(StreamWriter writer, SceneGroup group, int depth)
    {
        var indent = new string(' ', depth * 2);
        writer.WriteLine($"{indent}group \"{group.Name}\" {{");
        SaveCommonFields(writer, group, depth + 1);
        foreach (var child in group.Children)
            SaveNode(writer, child, depth + 1);
        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }

    private static void SaveSprite(StreamWriter writer, SceneSprite sprite, int depth)
    {
        var indent = new string(' ', depth * 2);
        var prop = new string(' ', (depth + 1) * 2);
        writer.WriteLine($"{indent}sprite \"{sprite.Name}\" {{");
        if (!string.IsNullOrEmpty(sprite.Sprite.Name))
            writer.WriteLine($"{prop}ref \"{sprite.Sprite.Name}\"");
        SaveCommonFields(writer, sprite, depth + 1);
        writer.WriteLine($"{indent}}}");
        writer.WriteLine();
    }

    private static void SaveCommonFields(StreamWriter writer, SceneNode node, int depth)
    {
        var prop = new string(' ', depth * 2);
        var inv = CultureInfo.InvariantCulture;

        if (node.Position != Vector2.Zero)
            writer.WriteLine(string.Format(inv, "{0}position {1} {2}", prop, node.Position.X, node.Position.Y));
        if (node.Rotation != 0f)
            writer.WriteLine(string.Format(inv, "{0}rotation {1}", prop, node.Rotation * 180f / MathF.PI));
        if (node.Scale != Vector2.One)
            writer.WriteLine(string.Format(inv, "{0}scale {1} {2}", prop, node.Scale.X, node.Scale.Y));
        if (node.Color != Color32.White)
            writer.WriteLine($"{prop}color {FormatColor(node.Color)}");
        if (!node.Visible)
            writer.WriteLine($"{prop}visible false");
        if (node.Locked)
            writer.WriteLine($"{prop}locked true");
        if (node.Placeholder)
            writer.WriteLine($"{prop}placeholder true");
    }

    private static string FormatColor(Color32 c)
    {
        if (c.A < 255)
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
