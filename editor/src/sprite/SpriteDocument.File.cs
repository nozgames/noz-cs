//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract partial class SpriteDocument
{
    private void Load(ref Tokenizer tk)
    {
        Root.Clear();
        AnimFrames.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("type"))
            {
                // Type line is consumed during factory creation; skip here
                tk.ExpectIdentifier();
            }
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
            else if (tk.ExpectIdentifier("frame"))
                ParseAnimFrame(ref tk);
            else if (!TryLoadContentToken(ref tk))
            {
                tk.ExpectToken(out var badToken);
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }
    }

    protected virtual bool TryLoadContentToken(ref Tokenizer tk) => false;

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
                while (!tk.IsEOF)
                {
                    var name = tk.ExpectQuotedString();
                    if (name == null)
                        break;
                    var node = Root.Find<SpriteNode>(name);
                    if (node != null)
                        frame.VisibleLayers.Add(node);
                    else
                        ReportWarning($"Animation frame references unknown layer '{name}'");
                }
            }
            else
            {
                tk.ExpectToken(out var badToken);
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        AnimFrames.Add(frame);
    }

    public override void Save(StreamWriter writer)
    {
        SaveTypeHeader(writer);

        if (!Edges.IsZero)
            writer.WriteLine($"edges ({Edges.T},{Edges.L},{Edges.B},{Edges.R})");

        if (Skeleton.HasValue)
            writer.WriteLine($"skeleton \"{Skeleton.Name}\"");

        if (BoneName != null)
            writer.WriteLine($"bone \"{BoneName}\"");

        if (SortOrderId != null)
            writer.WriteLine($"sort \"{SortOrderId}\"");

        SaveContent(writer);
        SaveAnimFrames(writer);
    }

    protected virtual void SaveTypeHeader(StreamWriter writer) { }

    private void SaveAnimFrames(StreamWriter writer)
    {
        if (AnimFrames.Count == 0)
            return;

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

    protected static string FormatColor(Color32 c)
    {
        if (c.A < 255)
            return $"rgba({c.R},{c.G},{c.B},{c.A / 255f:G})";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
